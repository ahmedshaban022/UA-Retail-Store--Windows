using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Pos;
using StoreFlow.Application.Settings;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Infrastructure.Backup;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Pos;
using StoreFlow.Infrastructure.Services;
using StoreFlow.Infrastructure.Settings;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The backup engine against a real SQLite database: the capture, the archive, the restore into a
/// second profile, the retention, the restore point and the refusal of a backup that was changed.
/// </summary>
public sealed class BackupTests : IAsyncLifetime
{
    private static readonly DateOnly BusinessDate = new(2026, 1, 1);

    private Profile _shop = null!;
    private AllowAllAuthorization _authorization = null!;
    private EfAuditWriter _auditWriter = null!;
    private ShopSession _session = null!;
    private MutableClock _clock = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _shop = await Profile.CreateAsync();
        _authorization = new AllowAllAuthorization();
        _session = new ShopSession();
        _clock = new MutableClock();
        _auditWriter = new EfAuditWriter(_shop.Factory, _clock, _session);

        _session.UserId = await CreateUserAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _shop.DisposeAsync();
    }

    [Fact]
    public async Task The_shop_is_captured_into_an_archive_that_says_what_it_holds()
    {
        await SeedShopAsync();

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        BackupCaptureResult capture = await CaptureAsync(_shop, path);

        capture.SizeBytes.ShouldBeGreaterThan(0);
        capture.Sha256.ShouldNotBeNullOrWhiteSpace();
        capture.Manifest.Parts.ShouldContain(part => part.Path == BackupArchive.DatabaseEntryPath);
        capture.Manifest.Parts.ShouldContain(part => part.Path == BackupArchive.SettingsEntryPath);

        BackupArchiveInspection inspection = BackupArchive.Inspect(path);

        inspection.IsIntegrityOk.ShouldBeTrue(inspection.IntegrityMessage);
        inspection.Manifest!.SchemaVersion.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_product_pictures_go_into_the_backup()
    {
        await SeedShopAsync();

        Directory.CreateDirectory(Path.Combine(_shop.Paths.MediaDirectory, "products"));

        string picture = Path.Combine(_shop.Paths.MediaDirectory, "products", "shelf.png");

        await File.WriteAllTextAsync(picture, "not really a picture, but a file the shop keeps");

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        await CaptureAsync(_shop, path);

        using ZipArchive archive = ZipFile.OpenRead(path);

        archive.GetEntry("media/products/shelf.png").ShouldNotBeNull();
    }

    [Fact]
    public async Task A_backup_that_was_changed_after_it_was_written_is_refused_by_the_restore()
    {
        await SeedShopAsync();

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        await CaptureAsync(_shop, path);

        // The archive is rewritten with a different database inside it, and the manifest is left
        // alone: the manifest still carries the checksum of the database that was written.
        string tampered = Path.Combine(_shop.Root, "backups", "storeflow-tampered.sfb");

        using (FileStream file = new(tampered, FileMode.Create, FileAccess.Write))
        using (ZipArchive archive = new(file, ZipArchiveMode.Create))
        using (ZipArchive original = ZipFile.OpenRead(path))
        {
            foreach (ZipArchiveEntry entry in original.Entries)
            {
                ZipArchiveEntry copy = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);

                using Stream target = copy.Open();

                if (string.Equals(entry.FullName, BackupArchive.DatabaseEntryPath, StringComparison.Ordinal))
                {
                    target.Write("this is not the database that was backed up"u8);

                    continue;
                }

                using Stream source = entry.Open();

                source.CopyTo(target);
            }
        }

        RestoreOutcome outcome = await RestoreAsync(_shop, tampered);

        outcome.IsSucceeded.ShouldBeFalse();
        outcome.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_restore_without_the_permission_is_refused_by_the_application_layer()
    {
        await SeedShopAsync();

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        await CaptureAsync(_shop, path);

        var refusal = new DenyRestoreAuthorization();

        var handler = new RestoreBackupCommandHandler(
            refusal,
            new BackupRestoreService(_shop.Paths, _shop.Factory, new SaleActivityMonitor(), _clock),
            new SaleActivityMonitor(),
            _auditWriter,
            _session);

        Result<RestoreOutcome> result = await handler.HandleAsync(
            new RestoreBackupCommand(path),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthorizationService.NotAuthorizedCode);

        // The shop's data is untouched: the refusal happens before anything is read or written.
        await using StoreFlowDbContext context = await _shop.Factory.CreateDbContextAsync();

        (await context.SalesInvoices.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_restore_is_refused_while_a_sale_is_being_posted()
    {
        await SeedShopAsync();

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        await CaptureAsync(_shop, path);

        var monitor = new SaleActivityMonitor();

        using (monitor.Begin())
        {
            RestoreOutcome outcome = await RestoreAsync(_shop, path, monitor);

            outcome.IsSucceeded.ShouldBeFalse();
            outcome.Message.ShouldContain("sale", Case.Insensitive);
        }
    }

    [Fact]
    public async Task A_restore_into_a_second_profile_gives_the_same_shop()
    {
        await SeedShopAsync();

        // The shop's own settings are part of the shop, so they travel with it.
        var settingsStore = new BackupSettingsStore(_shop.Factory, _clock);

        await settingsStore.SaveAsync(StoreSettings.Defaults with
        {
            BackupDirectory = @"D:\shop-backups",
            DailyBackupRetention = 5,
        });

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        await CaptureAsync(_shop, path);

        await using Profile second = await Profile.CreateAsync();

        RestoreOutcome outcome = await RestoreAsync(second, path);

        outcome.IsSucceeded.ShouldBeTrue(outcome.Message);
        outcome.RequiresRestart.ShouldBeTrue();

        await using StoreFlowDbContext original = await _shop.Factory.CreateDbContextAsync();
        await using StoreFlowDbContext restored = await second.Factory.CreateDbContextAsync();

        // The invoice count.
        (await restored.SalesInvoices.CountAsync()).ShouldBe(await original.SalesInvoices.CountAsync());

        // The sales total.
        decimal originalTotal = (await original.SalesInvoices.ToListAsync())
            .Sum(invoice => invoice.Total.Amount);

        decimal restoredTotal = (await restored.SalesInvoices.ToListAsync())
            .Sum(invoice => invoice.Total.Amount);

        restoredTotal.ShouldBe(originalTotal);
        restoredTotal.ShouldBe(200m);

        // Every on-hand quantity.
        List<(int ProductId, long OnHand)> originalStock = await original.StockBalances
            .AsNoTracking()
            .OrderBy(balance => balance.ProductId)
            .Select(balance => new ValueTuple<int, long>(balance.ProductId, balance.QuantityOnHandScaled))
            .ToListAsync();

        List<(int ProductId, long OnHand)> restoredStock = await restored.StockBalances
            .AsNoTracking()
            .OrderBy(balance => balance.ProductId)
            .Select(balance => new ValueTuple<int, long>(balance.ProductId, balance.QuantityOnHandScaled))
            .ToListAsync();

        restoredStock.ShouldBe(originalStock);
        restoredStock.Single(stock => stock.ProductId == 1).OnHand.ShouldBe(8);

        // Every stock value.
        List<(int ProductId, decimal Value)> originalValue = await original.StockBalances
            .AsNoTracking()
            .OrderBy(balance => balance.ProductId)
            .Select(balance => new ValueTuple<int, decimal>(
                balance.ProductId,
                balance.QuantityOnHandScaled * balance.AverageUnitCost.Amount))
            .ToListAsync();

        List<(int ProductId, decimal Value)> restoredValue = await restored.StockBalances
            .AsNoTracking()
            .OrderBy(balance => balance.ProductId)
            .Select(balance => new ValueTuple<int, decimal>(
                balance.ProductId,
                balance.QuantityOnHandScaled * balance.AverageUnitCost.Amount))
            .ToListAsync();

        restoredValue.ShouldBe(originalValue);

        // The shop's own settings come with it.
        var reader = new StoreSettingsReader(second.Factory);

        StoreSettings restoredSettings = await reader.GetAsync();

        restoredSettings.BackupDirectory.ShouldBe(@"D:\shop-backups");
        restoredSettings.DailyBackupRetention.ShouldBe(5);
    }

    [Fact]
    public async Task Restoring_over_a_live_database_leaves_the_old_database_recoverable()
    {
        await SeedShopAsync();

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        await CaptureAsync(_shop, path);

        // The shop sells something after the backup was taken.
        await PostSaleAsync(productId: 1, quantity: 1m);

        await using (StoreFlowDbContext before = await _shop.Factory.CreateDbContextAsync())
        {
            (await before.SalesInvoices.CountAsync()).ShouldBe(2);
        }

        RestoreOutcome outcome = await RestoreAsync(_shop, path);

        outcome.IsSucceeded.ShouldBeTrue(outcome.Message);
        outcome.RestorePointPath.ShouldNotBeNullOrWhiteSpace();
        File.Exists(outcome.RestorePointPath!).ShouldBeTrue();

        // The shop is back to the state the backup was taken in.
        await using (StoreFlowDbContext after = await _shop.Factory.CreateDbContextAsync())
        {
            (await after.SalesInvoices.CountAsync()).ShouldBe(1);
        }

        // And the data that was replaced can be read back from the restore point.
        await using var restorePoint = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = outcome.RestorePointPath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            }.ToString());

        restorePoint.Open();

        using Microsoft.Data.Sqlite.SqliteCommand command = restorePoint.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM \"SalesInvoices\";";

        long invoicesInRestorePoint = (long)command.ExecuteScalar()!;

        invoicesInRestorePoint.ShouldBe(2);
    }

    [Fact]
    public async Task A_restore_that_could_not_swap_the_database_leaves_the_shop_s_data_alone()
    {
        await SeedShopAsync();

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        await CaptureAsync(_shop, path);

        // The shop sells something after the backup was taken.
        await PostSaleAsync(productId: 1, quantity: 1m);

        // The shop's own database cannot be written over, so the swap cannot happen. The restore
        // has to fail loudly and leave the shop's data exactly as it was.
        File.SetAttributes(_shop.Paths.DatabasePath, FileAttributes.ReadOnly);

        try
        {
            RestoreOutcome outcome = await RestoreAsync(_shop, path);

            outcome.IsSucceeded.ShouldBeFalse();
            outcome.RequiresRestart.ShouldBeFalse();
        }
        finally
        {
            File.SetAttributes(_shop.Paths.DatabasePath, FileAttributes.Normal);
        }

        // The shop's own sales are still there: the restore did not half-happen.
        await using (StoreFlowDbContext after = await _shop.Factory.CreateDbContextAsync())
        {
            (await after.SalesInvoices.CountAsync()).ShouldBe(2);
        }

        // And the staged copy of the shop's data is not left lying beside it.
        File.Exists(_shop.Paths.DatabasePath + ".restore").ShouldBeFalse();
    }

    [Fact]
    public async Task A_restore_whose_pictures_cannot_be_restored_still_puts_the_shop_back()
    {
        await SeedShopAsync();

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        await CaptureAsync(_shop, path);

        // The folder the pictures go in is taken by a file, so it cannot be created. By then the
        // shop's data has already been put back, and a picture that cannot be written must not
        // undo that, and must not stop the programme from restarting on the restored shop.
        Directory.Delete(_shop.Paths.MediaDirectory, recursive: true);

        await File.WriteAllTextAsync(_shop.Paths.MediaDirectory, "this is a file, not a folder");

        RestoreOutcome outcome = await RestoreAsync(_shop, path);

        outcome.IsSucceeded.ShouldBeTrue(outcome.Message);
        outcome.RequiresRestart.ShouldBeTrue();
        outcome.Message.ShouldContain("picture", Case.Insensitive);

        await using StoreFlowDbContext after = await _shop.Factory.CreateDbContextAsync();

        (await after.SalesInvoices.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_restore_that_cannot_keep_a_restore_point_still_puts_the_shop_back()
    {
        await SeedShopAsync();

        string path = Path.Combine(_shop.Root, "backups", "storeflow-manual.sfb");

        await CaptureAsync(_shop, path);

        // The folder the restore point goes in is taken by a file, so it cannot be created. This is
        // the situation the recovery wizard exists for: the shop's own folder is in a state where
        // not everything can be written. A restore point that cannot be kept is a reason to go
        // ahead without one, not a reason to refuse to put the shop back.
        Directory.Delete(_shop.Paths.RestorePointsDirectory, recursive: true);

        await File.WriteAllTextAsync(_shop.Paths.RestorePointsDirectory, "this is a file, not a folder");

        RestoreOutcome outcome = await RestoreAsync(_shop, path);

        outcome.IsSucceeded.ShouldBeTrue(outcome.Message);
        outcome.RestorePointPath.ShouldBeNull();

        // The shop is back, and the copy that was taken is the copy that was put back.
        await using StoreFlowDbContext after = await _shop.Factory.CreateDbContextAsync();

        (await after.SalesInvoices.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task The_copy_past_the_number_the_shop_keeps_is_the_one_that_goes()
    {
        string folder = Path.Combine(_shop.Root, "retention");

        Directory.CreateDirectory(folder);

        await new BackupSettingsStore(_shop.Factory, _clock).SaveAsync(
            StoreSettings.Defaults with { BackupDirectory = folder, DailyBackupRetention = 2 },
            CancellationToken.None);

        BackupRunner runner = CreateRunner(_shop);

        for (int day = 1; day <= 3; day++)
        {
            // Each backup is a day older than the one before it.
            _clock.UtcNow = new DateTimeOffset(2026, 11, day, 22, 0, 0, TimeSpan.Zero);

            BackupRunResult result = await runner.RunAsync(
                LocalFolderBackupTarget.KindName,
                BackupScheduleKind.Daily,
                null,
                CancellationToken.None);

            result.IsSucceeded.ShouldBeTrue(result.Message);
        }

        string[] remaining = Directory.GetFiles(folder, "*.sfb");

        remaining.Length.ShouldBe(2);

        remaining.Any(name => name.Contains("2026-11-01", StringComparison.Ordinal)).ShouldBeFalse();
        remaining.Any(name => name.Contains("2026-11-03", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_backup_is_written_to_the_log_with_the_checksum_that_proves_it()
    {
        string folder = Path.Combine(_shop.Root, "logged");

        Directory.CreateDirectory(folder);

        BackupRunner runner = CreateRunner(_shop);

        BackupRunResult result = await runner.RunAsync(
            LocalFolderBackupTarget.KindName,
            BackupScheduleKind.Manual,
            _session.UserId,
            CancellationToken.None);

        result.IsSucceeded.ShouldBeTrue(result.Message);

        await using StoreFlowDbContext context = await _shop.Factory.CreateDbContextAsync();

        BackupLog log = await context.BackupLogs.SingleAsync();

        log.Result.ShouldBe(BackupResult.Succeeded);
        log.Sha256.ShouldBe(result.Sha256);
        log.TargetKind.ShouldBe(LocalFolderBackupTarget.KindName);
        log.SizeBytes.ShouldBe(result.SizeBytes);
        log.CreatedUtc.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task The_local_folder_target_writes_lists_reads_and_deletes()
    {
        string folder = Path.Combine(_shop.Root, "target");

        var target = new LocalFolderBackupTarget(folder);

        BackupTargetStatus status = await target.TestConnectionAsync();

        status.IsReachable.ShouldBeTrue();

        using (var content = new MemoryStream("the backup"u8.ToArray()))
        {
            BackupUploadResult upload = await target.UploadAsync("storeflow-manual.sfb", content);

            upload.RemoteId.ShouldBe(Path.Combine(folder, "storeflow-manual.sfb"));
        }

        IReadOnlyList<RemoteBackupFile> files = await target.ListAsync();

        files.Count.ShouldBe(1);
        files[0].Name.ShouldBe("storeflow-manual.sfb");
        files[0].SizeBytes.ShouldBe(10);

        using var destination = new MemoryStream();

        await target.DownloadAsync(files[0].RemoteId, destination);

        destination.Length.ShouldBe(10);

        await target.DeleteAsync(files[0].RemoteId);

        (await target.ListAsync()).Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_migration_that_nobody_has_reviewed_takes_a_backup_first()
    {
        MigrationSafety.SelectMigrationsNeedingBackup(["20261025164151_InitialCreate"]).ShouldBeEmpty();

        IReadOnlyList<string> needing = MigrationSafety.SelectMigrationsNeedingBackup(
        [
            "20261025164151_InitialCreate",
            "20261102000000_SomethingNobodyReviewed",
        ]);

        needing.Count.ShouldBe(1);
        needing[0].ShouldBe("20261102000000_SomethingNobodyReviewed");
    }

    [Fact]
    public async Task The_migration_hook_writes_a_backup_before_a_migration_that_needs_one()
    {
        var capture = new RecordingCaptureService();

        var hook = new MigrationBackupHook(_shop.Paths, _shop.Factory, capture, _clock);

        string? taken = await hook.BackUpAsync(["20261025164151_InitialCreate"]);

        taken.ShouldBeNull();
        capture.Calls.ShouldBe(0);

        taken = await hook.BackUpAsync(["20261102000000_SomethingNobodyReviewed"]);

        taken.ShouldNotBeNullOrWhiteSpace();
        capture.Calls.ShouldBe(1);
        taken!.Contains(MigrationBackupHook.FileNamePrefix, StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task The_scheduler_takes_one_backup_when_the_shop_was_closed()
    {
        string folder = Path.Combine(_shop.Root, "scheduled");

        Directory.CreateDirectory(folder);

        var store = new BackupSettingsStore(_shop.Factory, _clock);

        await store.SaveAsync(
            new BackupScheduleSettings(LocalFolderBackupTarget.KindName, 23 * 60, 6, 23 * 60),
            CancellationToken.None);

        await new BackupSettingsStore(_shop.Factory, _clock).SaveAsync(
            StoreSettings.Defaults with { BackupDirectory = folder },
            CancellationToken.None);

        _clock.UtcNow = new DateTimeOffset(2026, 11, 7, 23, 30, 0, TimeSpan.Zero);

        var handler = new RunScheduledBackupsCommandHandler(
            CreateRunner(_shop),
            store,
            new StoreSettingsReader(_shop.Factory),
            _shop.Factory,
            _clock);

        Result<int> first = await handler.HandleAsync(new RunScheduledBackupsCommand(), CancellationToken.None);

        first.IsFailure.ShouldBeFalse();
        first.Value.ShouldBe(2);

        // The shop opens again the next morning: nothing is due, because both were taken.
        _clock.UtcNow = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

        Result<int> second = await handler.HandleAsync(new RunScheduledBackupsCommand(), CancellationToken.None);

        second.Value.ShouldBe(0);

        Directory.GetFiles(folder, "*.sfb").Length.ShouldBe(2);
    }

    [Fact]
    public async Task The_scheduler_takes_one_backup_when_a_day_was_missed()
    {
        string folder = Path.Combine(_shop.Root, "catchup");

        Directory.CreateDirectory(folder);

        var store = new BackupSettingsStore(_shop.Factory, _clock);

        await store.SaveAsync(
            new BackupScheduleSettings(LocalFolderBackupTarget.KindName, 23 * 60, 6, 23 * 60),
            CancellationToken.None);

        await new BackupSettingsStore(_shop.Factory, _clock).SaveAsync(
            StoreSettings.Defaults with { BackupDirectory = folder, WeeklyBackupEnabled = false },
            CancellationToken.None);

        // The shop was shut for a week and opens on the Friday.
        _clock.UtcNow = new DateTimeOffset(2026, 11, 6, 23, 30, 0, TimeSpan.Zero);

        var handler = new RunScheduledBackupsCommandHandler(
            CreateRunner(_shop),
            store,
            new StoreSettingsReader(_shop.Factory),
            _shop.Factory,
            _clock);

        Result<int> result = await handler.HandleAsync(new RunScheduledBackupsCommand(), CancellationToken.None);

        result.Value.ShouldBe(1);

        // One backup, not one for every missed day.
        Directory.GetFiles(folder, "*.sfb").Length.ShouldBe(1);
    }

    [Fact]
    public async Task The_manager_may_back_the_shop_up_but_may_not_put_it_back()
    {
        await using StoreFlowDbContext context = await _shop.Factory.CreateDbContextAsync();

        List<Permission> managerPermissions = await context.Roles
            .Where(role => role.Code == DatabaseInitializer.ManagerRoleCode)
            .SelectMany(role => role.RolePermissions)
            .Select(permission => permission.Permission)
            .ToListAsync();

        managerPermissions.ShouldContain(Permission.BackupCreate);
        managerPermissions.ShouldNotContain(Permission.BackupRestore);

        List<Permission> ownerPermissions = await context.Roles
            .Where(role => role.Code == DatabaseInitializer.OwnerRoleCode)
            .SelectMany(role => role.RolePermissions)
            .Select(permission => permission.Permission)
            .ToListAsync();

        ownerPermissions.ShouldContain(Permission.BackupCreate);
        ownerPermissions.ShouldContain(Permission.BackupRestore);

        // The behaviour, and not only the rows in the table.
        var manager = new ShopSession { UserId = 2, IsAdministrator = false };
        manager.Permissions.Add(Permission.BackupCreate);

        var managerAuthorization = new AuthorizationService(manager);

        (await managerAuthorization.RequireAsync(Permission.BackupCreate)).IsSuccess.ShouldBeTrue();
        (await managerAuthorization.RequireAsync(Permission.BackupRestore)).IsFailure.ShouldBeTrue();

        var owner = new ShopSession { UserId = 1, IsAdministrator = true };

        var ownerAuthorization = new AuthorizationService(owner);

        (await ownerAuthorization.RequireAsync(Permission.BackupRestore)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Builds a runner that writes into a folder of the profile's own.
    /// </summary>
    private BackupRunner CreateRunner(Profile profile) => new(
        profile.Factory,
        new SqliteBackupCaptureService(profile.Paths, profile.Factory, _clock),
        new BackupTargetFactory(profile.Paths, new StoreSettingsReader(profile.Factory), new NoCloudConfiguration()),
        new StoreSettingsReader(profile.Factory),
        _auditWriter,
        _clock);

    private static async Task<BackupCaptureResult> CaptureAsync(Profile profile, string path)
    {
        var capture = new SqliteBackupCaptureService(profile.Paths, profile.Factory, new MutableClock());

        return await capture.CaptureAsync(path, CancellationToken.None);
    }

    private static async Task<RestoreOutcome> RestoreAsync(
        Profile profile,
        string path,
        SaleActivityMonitor? monitor = null)
    {
        var service = new BackupRestoreService(
            profile.Paths,
            profile.Factory,
            monitor ?? new SaleActivityMonitor(),
            new MutableClock());

        return await service.RestoreAsync(path, createRestorePoint: true, CancellationToken.None);
    }

    /// <summary>
    /// Gives the shop something to lose: a product, ten on the shelf, and one sale of two.
    /// </summary>
    private async Task SeedShopAsync()
    {
        int productId = await CreateProductAsync("BCK-001", 100m);

        await PostPurchaseAsync(productId, 10m, 50m);

        await PostSaleAsync(productId, 2m);
    }

    private async Task<int> CreateProductAsync(string sku, decimal salePrice)
    {
        await using StoreFlowDbContext context = await _shop.Factory.CreateDbContextAsync();

        Unit unit = await context.Units.FirstAsync();

        TaxCategory tax = await context.TaxCategories.FirstAsync();

        var product = new Product
        {
            Sku = sku,
            Name = sku,
            UnitId = unit.Id,
            TaxCategoryId = tax.Id,
            SalePrice = new Money(salePrice),
            CostPrice = new Money(50m),
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        context.Products.Add(product);

        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task PostPurchaseAsync(int productId, decimal quantity, decimal unitCost)
    {
        Result<int> saved = await new SavePurchaseCommandHandler(
            _shop.Factory, _authorization, _auditWriter, _clock, _session)
            .HandleAsync(
                new SavePurchaseCommand(
                    null,
                    null,
                    BusinessDate,
                    [new PurchaseLineInput(productId, quantity, new Money(unitCost), Money.Zero, null)],
                    Money.Zero,
                    null),
                CancellationToken.None);

        saved.IsFailure.ShouldBeFalse(saved.Error.Message);

        Result<int> posted = await new PostPurchaseCommandHandler(
            _shop.Factory, _authorization, _auditWriter, _clock, _session)
            .HandleAsync(new PostPurchaseCommand(saved.Value), CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);
    }

    private async Task PostSaleAsync(int productId, decimal quantity)
    {
        Result<SaleReceipt> posted = await new PostSaleCommandHandler(
            _shop.Factory,
            _authorization,
            _auditWriter,
            _clock,
            _session,
            new StoreSettingsReader(_shop.Factory),
            new SaleActivityMonitor())
            .HandleAsync(
                new PostSaleCommand(
                    Guid.NewGuid(),
                    BusinessDate,
                    [new SaleLineInput(productId, quantity, new Money(100m), DiscountSpec.None)],
                    DiscountSpec.None,
                    [new PaymentInput(PaymentMethod.Cash, new Money(quantity * 100m), new Money(quantity * 100m), null)]),
                CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);
    }

    private async Task<int> CreateUserAsync()
    {
        await using StoreFlowDbContext context = await _shop.Factory.CreateDbContextAsync();

        Role role = await context.Roles.FirstAsync(candidate => candidate.Code == DatabaseInitializer.OwnerRoleCode);

        var user = new User
        {
            Username = "owner",
            DisplayName = "Owner",
            PasswordHash = "pbkdf2$sha256$1$c2FsdA$aGFzaA==",
            RoleId = role.Id,
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        context.Users.Add(user);

        await context.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>Authorises everything, so these tests exercise the engine and not the roles.</summary>
    private sealed class AllowAllAuthorization : IAuthorizationService
    {
        public bool IsAllowed(Permission permission) => true;

        public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>Refuses the restore, as a manager does: the manager may back up, not restore.</summary>
    private sealed class DenyRestoreAuthorization : IAuthorizationService
    {
        public bool IsAllowed(Permission permission) => permission != Permission.BackupRestore;

        public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
            permission == Permission.BackupRestore
                ? Task.FromResult(Result.Failure(
                    AuthorizationService.NotAuthorizedCode,
                    "Your role does not allow this action."))
                : Task.FromResult(Result.Success());
    }

    /// <summary>A cloud target the shop has not set up.</summary>
    private sealed class NoCloudConfiguration : ICloudBackupConfigurationProvider
    {
        public Task<CloudBackupConfiguration?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CloudBackupConfiguration?>(null);
    }

    /// <summary>Records that a capture was asked for, without writing one.</summary>
    private sealed class RecordingCaptureService : IBackupCaptureService
    {
        public int Calls { get; private set; }

        public Task<BackupCaptureResult> CaptureAsync(string archivePath, CancellationToken cancellationToken = default)
        {
            Calls++;

            var manifest = new BackupManifest
            {
                FormatVersion = BackupArchive.FormatVersion,
                AppVersion = "1.0.0",
                SchemaVersion = "test",
                CreatedUtc = DateTimeOffset.UtcNow,
                MachineName = "TEST",
            };

            return Task.FromResult(new BackupCaptureResult(archivePath, 0, "hash", manifest));
        }
    }
}
