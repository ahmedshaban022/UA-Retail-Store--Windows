using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Approvals;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Application.Identity;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Pos;
using StoreFlow.Application.Reporting;
using StoreFlow.Application.Sales;
using StoreFlow.Application.Security;
using StoreFlow.Application.Settings;
using StoreFlow.Application.Setup;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Infrastructure.Approvals;
using StoreFlow.Infrastructure.Backup;
using StoreFlow.Infrastructure.Catalog;
using StoreFlow.Infrastructure.Identity;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Pos;
using StoreFlow.Infrastructure.Reporting;
using StoreFlow.Infrastructure.Sales;
using StoreFlow.Infrastructure.Services;
using StoreFlow.Infrastructure.Settings;
using StoreFlow.Infrastructure.Setup;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// A failure at every named write against real SQLite proves that the shop keeps the whole
/// business transaction or none of it, and makes the separate audit write visible (PRD NFR-05).
/// </summary>
public sealed class WriteFaultMatrixTests : IAsyncLifetime
{
    private const string Password = "StoreFlow!2026";
    private static readonly DateOnly BusinessDate = new(2026, 1, 1);

    private readonly DatabaseFixture _fixture = new();
    private readonly AllowAllAuthorization _authorization = new();
    private readonly TestSession _session = new();
    private readonly TestClock _clock = new();

    private EfAuditWriter AuditWriter => new(_fixture.Factory, _clock, _session);

    /// <inheritdoc />
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>
    /// Every catalogue member has exactly one executable scenario, so a new or phantom write
    /// point cannot quietly escape the matrix.
    /// </summary>
    [Fact]
    public void Every_write_point_has_exactly_one_scenario_in_the_matrix()
    {
        WritePoint[] exercised = typeof(WriteFaultMatrixTests).GetMethods()
            .Where(method => method.IsDefined(typeof(TheoryAttribute)))
            .SelectMany(method => method.CustomAttributes)
            .Where(attribute => attribute.AttributeType == typeof(InlineDataAttribute))
            .SelectMany(attribute => attribute.ConstructorArguments)
            .SelectMany(argument => (IEnumerable<CustomAttributeTypedArgument>)
                (argument.Value ?? throw new InvalidOperationException("A matrix row has no write point.")))
            .Select(argument => (WritePoint)
                (argument.Value ?? throw new InvalidOperationException("A matrix row has a null write point.")))
            .ToArray();

        exercised.Length.ShouldBe(exercised.Distinct().Count(), "Each point needs exactly one scenario.");
        exercised.Order().ShouldBe(Enum.GetValues<WritePoint>().Order());
    }

    /// <summary>
    /// A sale that fails before either save or its commit leaves no invoice, payment, movement
    /// or pending request outcome behind, so the cashier can safely start again.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.PostSaleInvoice)]
    [InlineData(WritePoint.PostSaleStockMovement)]
    [InlineData(WritePoint.PostSaleAudit)]
    [InlineData(WritePoint.PostSaleCommit)]
    public async Task A_failure_while_posting_a_sale_leaves_the_shop_as_it_was(WritePoint point)
    {
        int productId = await PrepareShopAsync();

        await ProveRollbackAsync(point, () => SaleHandler().HandleAsync(SaleCommand(productId), CancellationToken.None));
    }

    /// <summary>
    /// Failed returns and reversals preserve the original invoice and its returned quantities
    /// as well as the stock and money, so a correction cannot undo only half a sale.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.PostReturnDocuments)]
    [InlineData(WritePoint.PostReturnRestock)]
    [InlineData(WritePoint.PostReturnCommit)]
    [InlineData(WritePoint.ReverseInvoiceDocuments)]
    [InlineData(WritePoint.ReverseInvoiceStockMovement)]
    [InlineData(WritePoint.ReverseInvoiceCommit)]
    public async Task A_failure_while_correcting_a_sale_preserves_the_original_sale(WritePoint point)
    {
        int productId = await PrepareShopAsync();
        Result<SaleReceipt> sale = await SaleHandler().HandleAsync(SaleCommand(productId), CancellationToken.None);
        sale.IsSuccess.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        int lineId = await context.SalesInvoiceLines.Select(line => line.Id).SingleAsync();

        Func<Task> command = point switch
        {
            WritePoint.PostReturnDocuments or WritePoint.PostReturnRestock or WritePoint.PostReturnCommit =>
                () => new PostReturnCommandHandler(_fixture.Factory, _authorization, AuditWriter, _clock, _session)
                    .HandleAsync(new PostReturnCommand(sale.Value.InvoiceId, BusinessDate,
                        [new ReturnLineInput(lineId, 1m, true)], PaymentMethod.Cash, "Changed mind"), CancellationToken.None),
            WritePoint.ReverseInvoiceDocuments or WritePoint.ReverseInvoiceStockMovement or WritePoint.ReverseInvoiceCommit =>
                () => new ReverseInvoiceCommandHandler(_fixture.Factory, _authorization, AuditWriter, _clock, _session)
                    .HandleAsync(new ReverseInvoiceCommand(sale.Value.InvoiceId, BusinessDate,
                        PaymentMethod.Cash, "Wrong sale", true), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(point)),
        };

        await ProveRollbackAsync(point, command);
    }

    /// <summary>
    /// A failed purchase write preserves the draft or posted receipt exactly, so a receipt and
    /// its stock cannot disagree about whether the goods arrived or were sent back.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.SavePurchase)]
    [InlineData(WritePoint.PostPurchaseStock)]
    [InlineData(WritePoint.PostPurchaseCommit)]
    [InlineData(WritePoint.ReversePurchaseDocument)]
    [InlineData(WritePoint.ReversePurchaseStock)]
    [InlineData(WritePoint.ReversePurchaseCommit)]
    public async Task A_failure_in_a_purchase_leaves_its_documents_and_stock_unchanged(WritePoint point)
    {
        int productId = await PrepareShopAsync();
        int purchaseId = await SavePurchaseAsync(productId);

        if (point is WritePoint.ReversePurchaseDocument or WritePoint.ReversePurchaseStock or WritePoint.ReversePurchaseCommit)
        {
            Result<int> posted = await PostPurchaseAsync(purchaseId);
            posted.IsSuccess.ShouldBeTrue();
        }

        Func<Task> command = point switch
        {
            // Saving is one SaveChanges transaction, with no separate product commit hook.
            WritePoint.SavePurchase => () => new SavePurchaseCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock, _session)
                .HandleAsync(PurchaseCommand(productId) with { PurchaseId = purchaseId, Notes = "Edited draft" }, CancellationToken.None),
            WritePoint.PostPurchaseStock or WritePoint.PostPurchaseCommit => () => PostPurchaseAsync(purchaseId),
            WritePoint.ReversePurchaseDocument or WritePoint.ReversePurchaseStock or WritePoint.ReversePurchaseCommit =>
                () => new ReversePurchaseCommandHandler(_fixture.Factory, _authorization, AuditWriter, _clock, _session)
                    .HandleAsync(new ReversePurchaseCommand(purchaseId, "Wrong delivery"), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(point)),
        };

        await ProveRollbackAsync(point, command);
    }

    /// <summary>
    /// A failed count preserves its lines, approval state and ledger together, so the counted
    /// shelf cannot be posted without the document that explains it.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.SaveStockCount)]
    [InlineData(WritePoint.SubmitStockCount)]
    [InlineData(WritePoint.ApproveStockCount)]
    [InlineData(WritePoint.CancelStockCount)]
    [InlineData(WritePoint.PostStockCountMovements)]
    [InlineData(WritePoint.PostStockCountCommit)]
    public async Task A_failure_in_a_stock_count_preserves_the_count_and_the_shelf(WritePoint point)
    {
        int productId = await PrepareShopAsync();
        Result<int> saved = await new SaveStockCountCommandHandler(
            _fixture.Factory, _authorization, AuditWriter, _clock, _session)
            .HandleAsync(CountCommand(productId), CancellationToken.None);
        saved.IsSuccess.ShouldBeTrue();
        int countId = saved.Value;

        if (point is WritePoint.ApproveStockCount or WritePoint.PostStockCountMovements or WritePoint.PostStockCountCommit)
        {
            Result submitted = await SubmitCountAsync(countId);
            submitted.IsSuccess.ShouldBeTrue();
        }

        _session.UserId = await CreateUserAsync("approver");

        if (point is WritePoint.PostStockCountMovements or WritePoint.PostStockCountCommit)
        {
            Result approved = await ApproveCountAsync(countId);
            approved.IsSuccess.ShouldBeTrue();
        }

        // Save, submit, approve and cancel each use a single SaveChanges transaction. Only posting
        // has a separate commit point; the matrix does not invent one for the other transitions.
        Func<Task> command = point switch
        {
            WritePoint.SaveStockCount => () => new SaveStockCountCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock, _session)
                .HandleAsync(CountCommand(productId) with
                {
                    StockCountId = countId,
                    Lines = [new StockCountLineInput(productId, 6m)],
                }, CancellationToken.None),
            WritePoint.SubmitStockCount => () => SubmitCountAsync(countId),
            WritePoint.ApproveStockCount => () => ApproveCountAsync(countId),
            WritePoint.CancelStockCount => () => new CancelStockCountCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _session)
                .HandleAsync(new CancelStockCountCommand(countId), CancellationToken.None),
            WritePoint.PostStockCountMovements or WritePoint.PostStockCountCommit =>
                () => new PostStockCountCommandHandler(_fixture.Factory, _authorization, AuditWriter, _clock, _session)
                    .HandleAsync(new PostStockCountCommand(countId), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(point)),
        };

        await ProveRollbackAsync(point, command);
    }

    /// <summary>
    /// Failed opening balances, adjustments and approvals leave no ledger change or partially
    /// decided request behind, including failures after their saves but before their commits.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.RecordOpeningStock)]
    [InlineData(WritePoint.RecordOpeningStockCommit)]
    [InlineData(WritePoint.RequestStockAdjustmentApproval)]
    [InlineData(WritePoint.PostStockAdjustment)]
    [InlineData(WritePoint.PostStockAdjustmentCommit)]
    [InlineData(WritePoint.ApproveStockAdjustment)]
    [InlineData(WritePoint.ApproveStockAdjustmentCommit)]
    public async Task A_failure_in_a_stock_adjustment_preserves_the_ledger_and_approval(WritePoint point)
    {
        int productId = await PrepareShopAsync();
        Func<Task> command;

        if (point is WritePoint.RecordOpeningStock or WritePoint.RecordOpeningStockCommit)
        {
            int newProductId = await CreateProductAsync("UNOPENED");
            command = () => OpeningStockAsync(newProductId);
        }
        else if (point is WritePoint.ApproveStockAdjustment or WritePoint.ApproveStockAdjustmentCommit)
        {
            Result<StockAdjustmentOutcome> requested = await AdjustStockAsync(productId, 200m);
            requested.IsSuccess.ShouldBeTrue();
            requested.Value.Posted.ShouldBeFalse();
            int requestId = requested.Value.ApprovalRequestId
                ?? throw new InvalidOperationException("The large adjustment needs an approval request.");
            _session.UserId = await CreateUserAsync("approver");

            command = () => new ApproveStockAdjustmentCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock, _session)
                .HandleAsync(new ApproveStockAdjustmentCommand(requestId, "Checked the shelf"), CancellationToken.None);
        }
        else
        {
            // Asking for approval has one SaveChanges transaction and no separate commit point.
            decimal quantity = point == WritePoint.RequestStockAdjustmentApproval ? 200m : -2m;
            command = () => AdjustStockAsync(productId, quantity);
        }

        await ProveRollbackAsync(point, command);
    }

    /// <summary>
    /// A failed cart write keeps the waiting cart exactly as it was and never reaches stock,
    /// so taking a cart back up cannot quietly lose it.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.HoldCart)]
    [InlineData(WritePoint.ResumeHeldCart)]
    [InlineData(WritePoint.ClearHeldCart)]
    public async Task A_failure_in_a_held_cart_preserves_the_waiting_carts(WritePoint point)
    {
        int productId = await PrepareShopAsync();
        var hold = new HoldCartCommand("Waiting customer", SaleCommand(productId).Lines, DiscountSpec.None);
        var handler = new HoldCartCommandHandler(_fixture.Factory, _authorization, AuditWriter, _clock, _session);
        Result<HeldCartSummary> saved = await handler.HandleAsync(hold, CancellationToken.None);
        saved.IsSuccess.ShouldBeTrue();

        // Each cart operation saves once; none has a separate commit hook.
        Func<Task> command = point switch
        {
            WritePoint.HoldCart => () => handler.HandleAsync(hold with { Label = "Another customer" }, CancellationToken.None),
            WritePoint.ResumeHeldCart => () => new ResumeHeldCartCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _session)
                .HandleAsync(new ResumeHeldCartCommand(saved.Value.Id), CancellationToken.None),
            WritePoint.ClearHeldCart => () => new ClearHeldCartCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _session)
                .HandleAsync(new ClearHeldCartCommand(saved.Value.Id), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(point)),
        };

        await ProveRollbackAsync(point, command);
    }

    /// <summary>
    /// A failed catalogue write keeps every field and barcode, so an edit or import cannot
    /// leave only part of the catalogue the shop asked for.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.SaveProduct)]
    [InlineData(WritePoint.SetProductActive)]
    [InlineData(WritePoint.ImportProducts)]
    [InlineData(WritePoint.SaveCategory)]
    [InlineData(WritePoint.SetCategoryActive)]
    [InlineData(WritePoint.SaveUnit)]
    [InlineData(WritePoint.SetUnitActive)]
    [InlineData(WritePoint.SaveSupplier)]
    [InlineData(WritePoint.SetSupplierActive)]
    public async Task A_failure_in_the_catalogue_preserves_every_record_and_field(WritePoint point)
    {
        int productId = await PrepareShopAsync();
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        Product product = await context.Products.AsNoTracking().SingleAsync();
        var category = new Category { Name = "Groceries", CreatedUtc = _clock.UtcNow, UpdatedUtc = _clock.UtcNow };
        var supplier = new Supplier { Name = "Supplier", CreatedUtc = _clock.UtcNow, UpdatedUtc = _clock.UtcNow };
        context.Categories.Add(category);
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        string importPath = Path.Combine(Path.GetDirectoryName(_fixture.DatabasePath)
            ?? throw new InvalidOperationException("The fixture needs a directory."), "products.csv");
        await File.WriteAllTextAsync(importPath,
            "SKU,Name,SalePrice,CostPrice,Barcode\nMATRIX,Edited rice,120.00,55.00,6221000000001\n" +
            "IMPORTED,Imported rice,100.00,50.00,6221000000002\n");

        // Each catalogue operation uses one SaveChanges transaction, including the whole import.
        // There is no separate product commit hook for these operations.
        Func<Task> command = point switch
        {
            WritePoint.SaveProduct => () => new SaveProductCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock, new TestImageStore())
                .HandleAsync(new SaveProductCommand(productId, "MATRIX", "Edited rice", null, "Edited description",
                    category.Id, product.UnitId, product.TaxCategoryId, new Money(120m), new Money(55m), 2m,
                    "6221000000001", ["6221000000002", "6221000000003"], null, true), CancellationToken.None),
            WritePoint.SetProductActive => () => new SetProductActiveCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock)
                .HandleAsync(new SetProductActiveCommand(productId, false), CancellationToken.None),
            WritePoint.ImportProducts => () => new ImportProductsCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock)
                .HandleAsync(new ImportProductsCommand(importPath, ProductImportMode.UpsertBySku,
                    new Dictionary<string, string> { [ProductCsvMapper.FieldSku] = "SKU" }), CancellationToken.None),
            WritePoint.SaveCategory => () => new SaveCategoryCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock)
                .HandleAsync(new SaveCategoryCommand(category.Id, "Edited groceries", "Edited", true), CancellationToken.None),
            WritePoint.SetCategoryActive => () => new SetCategoryActiveCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock)
                .HandleAsync(new SetCategoryActiveCommand(category.Id, false), CancellationToken.None),
            WritePoint.SaveUnit => () => new SaveUnitCommandHandler(_fixture.Factory, _authorization, AuditWriter)
                .HandleAsync(new SaveUnitCommand(product.UnitId, "Edited piece", "Edited", 0, true), CancellationToken.None),
            WritePoint.SetUnitActive => () => new SetUnitActiveCommandHandler(_fixture.Factory, _authorization, AuditWriter)
                .HandleAsync(new SetUnitActiveCommand(product.UnitId, false), CancellationToken.None),
            WritePoint.SaveSupplier => () => new SaveSupplierCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock)
                .HandleAsync(new SaveSupplierCommand(supplier.Id, "Edited supplier", "Edited", "01001234567", "Edited", true), CancellationToken.None),
            WritePoint.SetSupplierActive => () => new SetSupplierActiveCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock)
                .HandleAsync(new SetSupplierActiveCommand(supplier.Id, false), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(point)),
        };

        await ProveRollbackAsync(point, command);
    }

    /// <summary>
    /// A failed account write preserves the accounts, roles and last-login values, so the
    /// application cannot report an account change or sign-in that was only partly recorded.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.CreateUser)]
    [InlineData(WritePoint.ChangeUserRole)]
    [InlineData(WritePoint.SetUserActive)]
    [InlineData(WritePoint.Authenticate)]
    public async Task A_failure_in_an_account_write_preserves_the_accounts(WritePoint point)
    {
        await PrepareShopAsync();
        int userId = await CreateUserAsync("clerk");
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        int cashierRoleId = await context.Roles.Where(role => role.Code == "cashier").Select(role => role.Id).SingleAsync();

        // Account changes and sign-in each save once; there is no separate commit hook.
        Func<Task> command = point switch
        {
            WritePoint.CreateUser => () => new CreateUserCommandHandler(
                _fixture.Factory, _authorization, new PasswordHasher(), _clock, AuditWriter, _session)
                .HandleAsync(new CreateUserCommand("new-clerk", "New clerk", Password, cashierRoleId), CancellationToken.None),
            WritePoint.ChangeUserRole => () => new ChangeUserRoleCommandHandler(
                _fixture.Factory, _authorization, _clock, AuditWriter, _session)
                .HandleAsync(new ChangeUserRoleCommand(userId, cashierRoleId), CancellationToken.None),
            WritePoint.SetUserActive => () => new SetUserActiveCommandHandler(
                _fixture.Factory, _authorization, _clock, AuditWriter, _session)
                .HandleAsync(new SetUserActiveCommand(userId, false), CancellationToken.None),
            WritePoint.Authenticate => () => new AuthenticateCommandHandler(
                _fixture.Factory, new PasswordHasher(), _clock, AuditWriter)
                .HandleAsync(new AuthenticateCommand("clerk", Password), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(point)),
        };

        await ProveRollbackAsync(point, command);
    }

    /// <summary>
    /// Failed approval writes leave neither a new pending request nor a partial decision, so
    /// the second pair of eyes sees the same request after the failure.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.RequestDiscountApproval)]
    [InlineData(WritePoint.DecideApprovalRequest)]
    public async Task A_failure_in_a_discount_approval_preserves_the_requests(WritePoint point)
    {
        int productId = await PrepareShopAsync();
        var request = new RequestDiscountApprovalCommand(SaleCommand(productId).Lines, new DiscountSpec(DiscountKind.Percent, 20m));
        var handler = new RequestDiscountApprovalCommandHandler(_fixture.Factory, _authorization, AuditWriter, _clock, _session);
        Func<Task> command;

        if (point == WritePoint.DecideApprovalRequest)
        {
            Result<ApprovalRequestSummary> requested = await handler.HandleAsync(request, CancellationToken.None);
            requested.IsSuccess.ShouldBeTrue();
            _session.UserId = await CreateUserAsync("approver");
            command = () => new DecideApprovalRequestCommandHandler(
                _fixture.Factory, _authorization, AuditWriter, _clock, _session)
                .HandleAsync(new DecideApprovalRequestCommand(requested.Value.Id, true, "Checked"), CancellationToken.None);
        }
        else
        {
            command = () => handler.HandleAsync(request, CancellationToken.None);
        }

        // Each transition is one SaveChanges transaction, with no separate commit point.
        await ProveRollbackAsync(point, command);
    }

    /// <summary>
    /// Failed settings, drawer and backup-log writes preserve their stored values, so none can
    /// report that the shop's configuration or counts were saved when they were not.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.RecordDrawerCash)]
    [InlineData(WritePoint.SaveStoreSettings)]
    [InlineData(WritePoint.SaveBackupSchedule)]
    [InlineData(WritePoint.SaveCloudAddress)]
    [InlineData(WritePoint.WriteBackupLog)]
    public async Task A_failure_in_the_shop_settings_or_log_preserves_the_stored_values(WritePoint point)
    {
        await PrepareShopAsync();
        var settings = new BackupSettingsStore(_fixture.Factory, _clock);
        var cloud = new CloudTargetAddress("https://backup.example", "shop", "region", "original", "key");
        await settings.SaveAsync(BackupScheduleSettings.Defaults);
        await settings.SaveCloudAddressAsync(cloud);

        // These are single SaveChanges transactions, without separate commit hooks. The backup
        // scenario uses an unknown target: its failure log is a real product write, with no file
        // capture or upload to obscure the database assertion.
        Func<Task> command = point switch
        {
            WritePoint.RecordDrawerCash => () => new RecordDrawerCashCommandHandler(
                _fixture.Factory, _authorization, _clock, AuditWriter, _session)
                .HandleAsync(new RecordDrawerCashCommand(BusinessDate, _session.UserId, new Money(50m), new Money(250m)), CancellationToken.None),
            WritePoint.SaveStoreSettings => () => settings.SaveAsync(StoreSettings.Defaults with { PrintCopies = 2 }),
            WritePoint.SaveBackupSchedule => () => settings.SaveAsync(BackupScheduleSettings.Defaults with { DailyTimeMinutes = 60 }),
            WritePoint.SaveCloudAddress => () => settings.SaveCloudAddressAsync(cloud with { Prefix = "changed" }),
            WritePoint.WriteBackupLog => () => new BackupRunner(_fixture.Factory, new UnusedBackupCaptureService(),
                new UnknownBackupTargetFactory(), new StoreSettingsReader(_fixture.Factory), AuditWriter, _clock)
                .RunAsync("Unknown", BackupScheduleKind.Manual, _session.UserId, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(point)),
        };

        await ProveRollbackAsync(point, command);
    }

    /// <summary>
    /// A failed first-run save leaves no administrator or changed settings behind, so setup
    /// remains available rather than looking complete after only part of it was written.
    /// </summary>
    [Theory]
    [InlineData(WritePoint.CompleteSetup)]
    public async Task A_failure_during_setup_preserves_the_unconfigured_shop(WritePoint point)
    {
        // A fresh fixture deliberately has no users. Setup is a single SaveChanges transaction
        // for the account and both settings rows, with no separate product commit hook.
        await ProveRollbackAsync(point, () => new CompleteSetupCommandHandler(
            _fixture.Factory, new PasswordHasher(), _clock, AuditWriter)
            .HandleAsync(new CompleteSetupCommand("owner", Password, "Owner",
                new StoreDetails("Matrix shop", null, "New address", "01001234567", null, null),
                StoreSettings.Defaults with { PrintCopies = 2 }), CancellationToken.None));
    }

    /// <summary>A standalone audit write failure leaves the database unchanged.</summary>
    [Theory]
    [InlineData(WritePoint.WriteAuditEntry)]
    public async Task A_failure_writing_a_standalone_audit_leaves_no_entry(WritePoint point)
    {
        await ProveRollbackAsync(point, () => AuditWriter.WriteAsync(
            new AuditEntry("test.action", "Test"), CancellationToken.None));
    }

    /// <summary>A committed sale has one audit row even when the checkout is replayed.</summary>
    [Fact]
    public async Task A_committed_sale_and_its_replay_have_one_audit_entry()
    {
        int productId = await PrepareShopAsync();
        PostSaleCommand command = SaleCommand(productId);
        Result<SaleReceipt> sale = await SaleHandler().HandleAsync(command, CancellationToken.None);
        Result<SaleReceipt> replay = await SaleHandler().HandleAsync(command, CancellationToken.None);
        sale.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        replay.Value.WasReplay.ShouldBeTrue();
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        SalesInvoice invoice = await context.SalesInvoices.AsNoTracking().SingleAsync();
        invoice.Status.ShouldBe(DocumentStatus.Posted);
        invoice.ClientOperationId.ShouldBe(command.ClientOperationId);
        invoice.Total.ShouldBe(new Money(200m));
        invoice.Paid.ShouldBe(new Money(200m));
        SalesInvoiceLine line = await context.SalesInvoiceLines.AsNoTracking().SingleAsync();
        line.SalesInvoiceId.ShouldBe(invoice.Id);
        line.ProductId.ShouldBe(productId);
        line.QuantityScaled.ShouldBe(2);
        line.Total.ShouldBe(new Money(200m));
        line.UnitCostSnapshot.ShouldBe(new Money(50m));
        Payment payment = await context.Payments.AsNoTracking().SingleAsync();
        payment.SalesInvoiceId.ShouldBe(invoice.Id);
        payment.Amount.ShouldBe(new Money(200m));
        payment.IsRefund.ShouldBeFalse();
        StockMovement movement = await context.StockMovements.AsNoTracking()
            .SingleAsync(candidate => candidate.MovementType == StockMovementType.Sale);
        movement.DocumentId.ShouldBe(invoice.Id);
        movement.ProductId.ShouldBe(productId);
        movement.QuantityDeltaScaled.ShouldBe(-2);
        (await context.StockMovements.CountAsync()).ShouldBe(2);
        StockBalance balance = await context.StockBalances.AsNoTracking().SingleAsync();
        balance.ProductId.ShouldBe(productId);
        balance.QuantityOnHandScaled.ShouldBe(8);
        balance.AverageUnitCost.ShouldBe(new Money(50m));
        balance.DamagedQuantityScaled.ShouldBe(0);
        RequestOutcome outcome = await context.RequestOutcomes.AsNoTracking().SingleAsync();
        outcome.ClientOperationId.ShouldBe(command.ClientOperationId.ToString("D"));
        outcome.Status.ShouldBe(RequestOutcomeStatus.Committed);
        outcome.CompletedUtc.ShouldNotBeNull();
        outcome.ResultJson.ShouldNotBeNullOrWhiteSpace();
        SaleReceipt? stored = JsonSerializer.Deserialize<SaleReceipt>(outcome.ResultJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        stored.ShouldNotBeNull();
        stored.InvoiceId.ShouldBe(invoice.Id);
        replay.Value.InvoiceId.ShouldBe(invoice.Id);
        AuditLog audit = await context.AuditLogs.SingleAsync(log => log.Action == "sale.post");
        audit.EntityId.ShouldBe(invoice.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task ProveRollbackAsync(WritePoint point, Func<Task> command)
    {
        Dictionary<string, string[]> before = await ReadSnapshotAsync();

        await ProveFaultReachedAsync(point, command);

        ShouldMatchSnapshot(await ReadSnapshotAsync(), before);
    }

    private async Task ProveFaultReachedAsync(WritePoint point, Func<Task> command)
    {
        _fixture.Faults.Failed.ShouldBeEmpty("All prerequisite writes must finish before the fault is armed.");
        _fixture.Faults.FailAt = point;

        // These handlers propagate WriteFaultException; a returned Result, even a failed one,
        // would be a different contract and must fail this assertion.
        WriteFaultException exception = await Should.ThrowAsync<WriteFaultException>(command);
        exception.Point.ShouldBe(point);
        _fixture.Faults.Failed.ToArray().ShouldBe(new[] { point }, "The intended hook must be reached exactly once.");
    }

    private static void ShouldMatchSnapshot(Dictionary<string, string[]> actual, Dictionary<string, string[]> expected)
    {
        actual.Keys.Order(StringComparer.Ordinal).ShouldBe(expected.Keys.Order(StringComparer.Ordinal));
        foreach ((string table, string[] rows) in expected)
        {
            actual[table].ShouldBe(rows, $"Every persisted value in {table} must survive unchanged.");
        }
    }

    /// <summary>
    /// Reads every mapped table through a fresh connection, including audit, settings and request
    /// outcomes. Sorted complete rows catch edits and deletions that row counts would miss.
    /// SQLite's internal sequence and migration bookkeeping are not business records.
    /// </summary>
    private async Task<Dictionary<string, string[]>> ReadSnapshotAsync()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        DbConnection connection = context.Database.GetDbConnection();
        var snapshot = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (string table in context.Model.GetEntityTypes().Select(entity => entity.GetTableName())
            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            await using DbCommand query = connection.CreateCommand();
            // The identifier comes from EF metadata, and quoting also handles embedded quotes.
            query.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            await using DbDataReader reader = await query.ExecuteReaderAsync();
            var rows = new List<string>();

            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(JsonSerializer.Serialize(values));
            }

            snapshot.Add(table, rows.Order(StringComparer.Ordinal).ToArray());
        }

        return snapshot;
    }

    private static string TableName<TEntity>(StoreFlowDbContext context)
        where TEntity : class => context.Model.FindEntityType(typeof(TEntity))?.GetTableName()
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} needs a mapped table.");

    private async Task<int> PrepareShopAsync()
    {
        _session.UserId = await CreateUserAsync("owner");
        int productId = await CreateProductAsync("MATRIX");
        Result<int> opening = await OpeningStockAsync(productId);
        opening.IsSuccess.ShouldBeTrue();
        return productId;
    }

    private async Task<int> CreateUserAsync(string username)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        int ownerRoleId = await context.Roles.Where(role => role.Code == "owner").Select(role => role.Id).SingleAsync();
        var user = new User
        {
            Username = username,
            DisplayName = username,
            PasswordHash = new PasswordHasher().Hash(Password),
            RoleId = ownerRoleId,
            CreatedUtc = _clock.UtcNow,
            UpdatedUtc = _clock.UtcNow,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> CreateProductAsync(string sku)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        int unitId = await context.Units.Where(unit => unit.Precision == 0).Select(unit => unit.Id).FirstAsync();
        int taxId = await context.TaxCategories.Select(tax => tax.Id).FirstAsync();
        var product = new Product
        {
            Sku = sku,
            Name = sku,
            UnitId = unitId,
            TaxCategoryId = taxId,
            SalePrice = new Money(100m),
            CostPrice = new Money(50m),
            CreatedUtc = _clock.UtcNow,
            UpdatedUtc = _clock.UtcNow,
        };
        product.Barcodes.Add(new ProductBarcode { Barcode = $"EXTRA-{sku}" });
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product.Id;
    }

    private PostSaleCommandHandler SaleHandler() => new(_fixture.Factory, _authorization, AuditWriter,
        _clock, _session, new StoreSettingsReader(_fixture.Factory), new SaleActivityMonitor());

    private static PostSaleCommand SaleCommand(int productId) => new(Guid.NewGuid(), BusinessDate,
        [new SaleLineInput(productId, 2m, new Money(100m), DiscountSpec.None)], DiscountSpec.None,
        [new PaymentInput(PaymentMethod.Cash, new Money(200m), new Money(200m), null)]);

    private static SavePurchaseCommand PurchaseCommand(int productId) => new(null, null, BusinessDate,
        [new PurchaseLineInput(productId, 3m, new Money(60m), Money.Zero)], Money.Zero, null);

    private async Task<int> SavePurchaseAsync(int productId)
    {
        Result<int> saved = await new SavePurchaseCommandHandler(
            _fixture.Factory, _authorization, AuditWriter, _clock, _session)
            .HandleAsync(PurchaseCommand(productId), CancellationToken.None);
        saved.IsSuccess.ShouldBeTrue();
        return saved.Value;
    }

    private Task<Result<int>> PostPurchaseAsync(int purchaseId) => new PostPurchaseCommandHandler(
        _fixture.Factory, _authorization, AuditWriter, _clock, _session)
        .HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

    private static SaveStockCountCommand CountCommand(int productId) => new(null, BusinessDate,
        [new StockCountLineInput(productId, 8m)]);

    private Task<Result> SubmitCountAsync(int countId) => new SubmitStockCountCommandHandler(
        _fixture.Factory, _authorization, AuditWriter, _session)
        .HandleAsync(new SubmitStockCountCommand(countId), CancellationToken.None);

    private Task<Result> ApproveCountAsync(int countId) => new ApproveStockCountCommandHandler(
        _fixture.Factory, _authorization, AuditWriter, _clock, _session)
        .HandleAsync(new ApproveStockCountCommand(countId, "Checked the shelf"), CancellationToken.None);

    private Task<Result<int>> OpeningStockAsync(int productId) => new RecordOpeningStockCommandHandler(
        _fixture.Factory, _authorization, AuditWriter, _clock, _session)
        .HandleAsync(new RecordOpeningStockCommand([new OpeningStockLine(productId, 10m, new Money(50m))],
            BusinessDate, "Opening stock"), CancellationToken.None);

    private Task<Result<StockAdjustmentOutcome>> AdjustStockAsync(int productId, decimal quantity) =>
        new PostStockAdjustmentCommandHandler(_fixture.Factory, _authorization, AuditWriter, _clock, _session)
            .HandleAsync(new PostStockAdjustmentCommand(productId, quantity, StockDisposition.Sellable,
                new Money(50m), "Checked the shelf"), CancellationToken.None);

    /// <summary>Authorises everything, so the matrix exercises persistence rather than permission refusals.</summary>
    private sealed class AllowAllAuthorization : IAuthorizationService
    {
        public bool IsAllowed(Permission permission) => true;

        public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>A signed-in actor the matrix can change before an approval, so the second person is real.</summary>
    private sealed class TestSession : IUserSession
    {
        public int UserId { get; set; }

        public UserSession? Current => new(UserId, "tester", "Tester", new HashSet<Permission>(), false);

        public bool IsAuthenticated => true;

        public bool IsLocked => false;

        public void SignIn(UserSession session)
        {
        }

        public void SignOut()
        {
        }

        public void Lock()
        {
        }

        public void Unlock()
        {
        }
    }

    /// <summary>The catalogue scenario has no picture; the image port keeps it away from the filesystem.</summary>
    private sealed class TestImageStore : IProductImageStore
    {
        public Task<Result<string>> SaveAsync(string sourceFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string>.Success("products/test.png"));

        public Task<Result> DeleteAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public string ResolvePath(string relativePath) => relativePath;

        public bool Exists(string relativePath) => true;
    }

    /// <summary>An unknown destination makes the runner write its failure log before any file is created.</summary>
    private sealed class UnknownBackupTargetFactory : IBackupTargetFactory
    {
        public IReadOnlyList<string> GetKnownKinds() => [];

        public Task<IBackupTarget> ResolveAsync(string kind, CancellationToken cancellationToken = default) =>
            throw new ArgumentException("Unknown backup target.", nameof(kind));
    }

    /// <summary>Capture must not be reached when the runner cannot resolve the requested target.</summary>
    private sealed class UnusedBackupCaptureService : IBackupCaptureService
    {
        public Task<BackupCaptureResult> CaptureAsync(string archivePath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("An unknown target must be refused before capture.");
    }
}
