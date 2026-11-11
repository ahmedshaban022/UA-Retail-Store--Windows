using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Backup;
using StoreFlow.Infrastructure.Backup;

namespace StoreFlow.Infrastructure.Persistence;

/// <summary>
/// Decides whether a migration needs a backup before it is applied.
/// </summary>
/// <remarks>
/// <para>The default is to back up. A migration is treated as safe only when somebody has listed it
/// as safe; every other migration — every migration nobody has looked at yet — takes a backup of
/// the shop first (PRD §8.4, NFR-12).</para>
/// <para>That way the rule cannot be broken by forgetting: adding a migration and not adding it to
/// the safe list means the shop keeps a copy of itself before the change, which is exactly what
/// should happen when nobody is yet sure what the change does.</para>
/// </remarks>
public static class MigrationSafety
{
    /// <summary>
    /// The migrations that are known to change nothing that the shop would miss: they add tables
    /// and columns and leave the existing data alone.
    /// </summary>
    public static IReadOnlySet<string> SafeMigrationIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "20261025164151_InitialCreate",
        "20261027143322_CatalogCaseInsensitiveIdentifiers",
        "20261101140727_AddHeldCarts",
        "20261101161754_AddInvoiceDiscountShare",
        "20261101193637_AddSalesReturnsAndReversals",

        // The version of the stock balance became a real concurrency token. The shape of the table
        // is unchanged and the rows are untouched, so there is nothing for the shop to miss.
        "20261110231741_MakeBalanceVersionAConcurrencyToken",
    };

    /// <summary>
    /// The migrations that have to be applied and would change the data's shape.
    /// </summary>
    /// <param name="pendingMigrationIds">The migrations that are waiting to be applied.</param>
    /// <returns>The migrations that need a backup first.</returns>
    public static IReadOnlyList<string> SelectMigrationsNeedingBackup(IReadOnlyList<string> pendingMigrationIds)
    {
        ArgumentNullException.ThrowIfNull(pendingMigrationIds);

        return pendingMigrationIds
            .Where(id => !SafeMigrationIds.Contains(id))
            .ToList();
    }
}

/// <summary>
/// Takes the backup that a migration may need before it is applied.
/// </summary>
public interface IMigrationBackupHook
{
    /// <summary>
    /// Takes a backup of the shop before the given migrations are applied.
    /// </summary>
    /// <param name="pendingMigrationIds">The migrations that are waiting to be applied.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The path of the backup, or null when no backup was needed.</returns>
    Task<string?> BackUpAsync(IReadOnlyList<string> pendingMigrationIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// The backup the hook takes: a copy of the shop, written where the shop's own backups are kept,
/// and named for the migration that made it necessary.
/// </summary>
public sealed class MigrationBackupHook(
    AppPaths paths,
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IBackupCaptureService captureService,
    IClock clock) : IMigrationBackupHook
{
    /// <summary>The prefix a pre-migration backup carries.</summary>
    public const string FileNamePrefix = "storeflow-pre-migration-";

    /// <inheritdoc />
    public async Task<string?> BackUpAsync(
        IReadOnlyList<string> pendingMigrationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pendingMigrationIds);

        IReadOnlyList<string> needingBackup = MigrationSafety.SelectMigrationsNeedingBackup(pendingMigrationIds);

        if (needingBackup.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(paths.BackupsDirectory);

        string name = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{FileNamePrefix}{clock.UtcNow.UtcDateTime:yyyyMMdd-HHmmss}Z{BackupArchive.FileExtension}");

        string path = Path.Combine(paths.BackupsDirectory, name);

        await using (StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            // The context is only opened so that the migration history is read before the copy is
            // taken; the copy itself is made by the capture service, which does not need it.
            _ = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        }

        BackupCaptureResult capture = await captureService.CaptureAsync(path, cancellationToken);

        return capture.ArchivePath;
    }
}
