using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Approvals;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Export;
using StoreFlow.Infrastructure.Approvals;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Services;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The inventory and purchase behaviour against a real SQLite database, with the real migrations,
/// the real unique indexes and the real triggers.
/// </summary>
public sealed class InventoryTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture = new();

    private AllowAllAuthorization _authorization = null!;
    private EfAuditWriter _auditWriter = null!;
    private TestSession _session = null!;
    private int _secondUserId;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        _authorization = new AllowAllAuthorization();
        _session = new TestSession();
        _auditWriter = new EfAuditWriter(_fixture.Factory, new TestClock(), _session);

        _session.UserId = await CreateUserAsync("clerk", "Inventory clerk");
        _secondUserId = await CreateUserAsync("approver", "Inventory approver");
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_draft_purchase_changes_no_stock()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        int purchaseId = await SavePurchaseAsync(productId, 10m, 50m);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.StockBalances.CountAsync()).ShouldBe(0);
        (await context.StockMovements.CountAsync()).ShouldBe(0);

        PurchaseDocument? document = await context.PurchaseDocuments.FirstAsync();

        document.Status.ShouldBe(DocumentStatus.Draft);
        document.Number.ShouldStartWith("PUR-");
        purchaseId.ShouldBe(document.Id);
    }

    [Fact]
    public async Task A_posted_purchase_increases_stock_exactly_once()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        int purchaseId = await SavePurchaseAsync(productId, 10m, 50m);

        Result<int> posted = await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

        posted.IsSuccess.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(10);
        balance.AverageUnitCost.ShouldBe(new Money(50m));

        List<StockMovement> movements = await context.StockMovements.ToListAsync();

        movements.Count.ShouldBe(1);
        movements[0].MovementType.ShouldBe(StockMovementType.PurchaseReceipt);
        movements[0].QuantityDeltaScaled.ShouldBe(10);
        movements[0].DocumentId.ShouldBe(purchaseId);
        movements[0].RunningBalanceScaled.ShouldBe(10);

        PurchaseDocument document = await context.PurchaseDocuments.SingleAsync();

        document.Status.ShouldBe(DocumentStatus.Posted);
        document.PostedUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_posted_purchase_cannot_be_posted_twice()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        int purchaseId = await SavePurchaseAsync(productId, 10m, 50m);

        var handler = new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session);

        (await handler.HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        Result<int> again = await handler.HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

        again.IsFailure.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.StockMovements.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task The_ledger_reconciles_to_the_balance_after_every_kind_of_movement()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        int purchaseId = await SavePurchaseAsync(productId, 10m, 50m);

        await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

        await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, -2m, StockDisposition.Sellable, new Money(50m), "Damaged in the shop"),
                CancellationToken.None);

        await new RecordOpeningStockCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new RecordOpeningStockCommand(
                    [new OpeningStockLine(productId, 5m, new Money(60m))],
                    new DateOnly(2026, 1, 1),
                    "Top-up"),
                CancellationToken.None);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        foreach (StockBalance balance in await context.StockBalances.ToListAsync())
        {
            long ledgerSum = await context.StockMovements
                .Where(movement => movement.ProductId == balance.ProductId && movement.Disposition == StockDisposition.Sellable)
                .SumAsync(movement => movement.QuantityDeltaScaled);

            ledgerSum.ShouldBe(balance.QuantityOnHandScaled);
        }
    }

    [Fact]
    public async Task The_ledger_names_the_product_that_moved()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        int purchaseId = await SavePurchaseAsync(productId, 10m, 50m);

        await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

        Result<IReadOnlyList<StockMovementSummary>> movements = await new ListStockMovementsQueryHandler(
            _fixture.Factory, _authorization)
            .HandleAsync(new ListStockMovementsQuery(null, null, null, 100), CancellationToken.None);

        movements.IsSuccess.ShouldBeTrue();

        StockMovementSummary movement = movements.Value.ShouldHaveSingleItem();

        movement.Sku.ShouldBe("RICE-001", "the ledger has to say which product moved");
        movement.ProductName.ShouldBe("RICE-001");
        movement.QuantityDelta.ShouldBe(10m);
    }

    [Fact]
    public async Task A_posted_purchase_can_be_reversed()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        int purchaseId = await SavePurchaseAsync(productId, 10m, 50m);

        await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

        Result<int> reversed = await new ReversePurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new ReversePurchaseCommand(purchaseId, "Wrong delivery"), CancellationToken.None);

        reversed.IsSuccess.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(0);

        List<StockMovement> movements = await context.StockMovements.OrderBy(movement => movement.Id).ToListAsync();

        movements.Count.ShouldBe(2);
        movements[1].MovementType.ShouldBe(StockMovementType.Reversal);
        movements[1].QuantityDeltaScaled.ShouldBe(-10);
        movements[1].Reason.ShouldBe("Wrong delivery");

        PurchaseDocument original = await context.PurchaseDocuments.SingleAsync(document => document.Id == purchaseId);
        PurchaseDocument reversing = await context.PurchaseDocuments.SingleAsync(document => document.Id == reversed.Value);

        original.Status.ShouldBe(DocumentStatus.Reversed);
        reversing.ReversesDocumentId.ShouldBe(purchaseId);
    }

    [Fact]
    public async Task A_reversal_is_refused_when_the_stock_has_already_gone()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        int purchaseId = await SavePurchaseAsync(productId, 10m, 50m);

        await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

        await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, -10m, StockDisposition.Sellable, new Money(50m), "Sold outside the till"),
                CancellationToken.None);

        Result<int> reversed = await new ReversePurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new ReversePurchaseCommand(purchaseId, "Wrong delivery"), CancellationToken.None);

        reversed.IsFailure.ShouldBeTrue();
        reversed.Error.Code.ShouldBe(InventoryErrorCodes.NegativeStock);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.StockMovements.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task A_stock_count_does_not_change_stock_until_it_is_approved_and_posted()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 7m, 50m);

        int countId = await SaveStockCountAsync(productId, 5m);

        await using (StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync())
        {
            StockBalance balance = await context.StockBalances.SingleAsync();

            balance.QuantityOnHandScaled.ShouldBe(7);
        }

        Result submitted = await new SubmitStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, _session)
            .HandleAsync(new SubmitStockCountCommand(countId), CancellationToken.None);

        submitted.IsSuccess.ShouldBeTrue();

        // A second authorised person approves the count: the counter may not sign off their own count.
        _session.UserId = _secondUserId;

        Result approved = await new ApproveStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new ApproveStockCountCommand(countId, "Recount of the shelf"), CancellationToken.None);

        approved.IsSuccess.ShouldBeTrue();

        _session.UserId = _secondUserId;

        await using (StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync())
        {
            StockBalance balance = await context.StockBalances.SingleAsync();

            balance.QuantityOnHandScaled.ShouldBe(7);
        }

        Result<int> posted = await new PostStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostStockCountCommand(countId), CancellationToken.None);

        posted.IsSuccess.ShouldBeTrue();

        await using StoreFlowDbContext contextAfter = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balanceAfter = await contextAfter.StockBalances.SingleAsync();

        balanceAfter.QuantityOnHandScaled.ShouldBe(5);

        StockMovement correction = await contextAfter.StockMovements.SingleAsync(
            movement => movement.MovementType == StockMovementType.CountCorrection);

        correction.QuantityDeltaScaled.ShouldBe(-2);
        correction.Reason.ShouldBe("Recount of the shelf");
        correction.DocumentId.ShouldBe(countId);
    }

    [Fact]
    public async Task A_count_brings_the_books_to_the_shelf_even_when_the_stock_moved_in_between()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        // The counter counts twelve on the shelf. At that moment the books say ten.
        int countId = await SaveStockCountAsync(productId, 12m);

        // Three of them are sold while the count waits for a second person to approve it.
        await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, -3m, StockDisposition.Sellable, new Money(50m), "Sold at the till"),
                CancellationToken.None);

        await ApproveAndPostCountAsync(countId);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        // The shelf holds twelve, so the books say twelve — not the nine that replaying the
        // difference captured at save time would have left behind.
        balance.QuantityOnHandScaled.ShouldBe(12);

        StockMovement correction = await context.StockMovements.SingleAsync(
            movement => movement.MovementType == StockMovementType.CountCorrection);

        correction.QuantityDeltaScaled.ShouldBe(5);
    }

    [Fact]
    public async Task A_count_that_would_have_run_the_stock_negative_now_lands_the_books_on_the_shelf()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        // The counter counts five on the shelf, while the books say ten.
        int countId = await SaveStockCountAsync(productId, 5m);

        // Six of them are sold while the count waits for its approval.
        await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, -6m, StockDisposition.Sellable, new Money(50m), "Sold at the till"),
                CancellationToken.None);

        await ApproveAndPostCountAsync(countId);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        // Four were left on the shelf, the counter found five, so one came back into the books.
        // Replaying the difference captured at save time would have tried to remove five from four
        // and the whole count would have rolled back with a misleading message.
        balance.QuantityOnHandScaled.ShouldBe(5);

        StockMovement correction = await context.StockMovements.SingleAsync(
            movement => movement.MovementType == StockMovementType.CountCorrection);

        correction.QuantityDeltaScaled.ShouldBe(1);
    }

    [Fact]
    public async Task A_posted_count_records_the_correction_it_actually_applied()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        int countId = await SaveStockCountAsync(productId, 12m);

        await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, -3m, StockDisposition.Sellable, new Money(50m), "Sold at the till"),
                CancellationToken.None);

        await ApproveAndPostCountAsync(countId);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockCountLine line = await context.StockCountLines.SingleAsync();

        // The count document tells the same story as the ledger: the books said seven, the shelf
        // said twelve, and five were corrected.
        line.ExpectedQuantityScaled.ShouldBe(7);
        line.CountedQuantityScaled.ShouldBe(12);
        line.DifferenceScaled.ShouldBe(5);
    }

    [Fact]
    public async Task A_balance_that_somebody_else_has_moved_is_not_overwritten()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        await using (StoreFlowDbContext first = await _fixture.Factory.CreateDbContextAsync())
        {
            StockBalance balance = await first.StockBalances.SingleAsync();

            balance.QuantityOnHandScaled.ShouldBe(10);

            // Somebody else moves the stock while this one is holding the balance it read.
            await using (StoreFlowDbContext second = await _fixture.Factory.CreateDbContextAsync())
            {
                StockBalance other = await second.StockBalances.SingleAsync();

                other.QuantityOnHandScaled = 20;
                other.Version += 1;

                await second.SaveChangesAsync();
            }

            balance.QuantityOnHandScaled = 30;
            balance.Version += 1;

            // The write carries the version it read, so it lands on a balance that has moved and is
            // refused, rather than quietly overwriting what the other writer put there. The ledger
            // and the balance have to agree, and this is what keeps them agreeing.
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());
        }

        await using StoreFlowDbContext after = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balanceAfter = await after.StockBalances.SingleAsync();

        balanceAfter.QuantityOnHandScaled.ShouldBe(20);
    }

    [Fact]
    public async Task A_stock_count_cannot_be_approved_by_the_person_who_counted_it()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 7m, 50m);

        int countId = await SaveStockCountAsync(productId, 5m);

        await new SubmitStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, _session)
            .HandleAsync(new SubmitStockCountCommand(countId), CancellationToken.None);

        Result approved = await new ApproveStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new ApproveStockCountCommand(countId, "Recount"), CancellationToken.None);

        approved.IsFailure.ShouldBeTrue();
        approved.Error.Code.ShouldBe(InventoryErrorCodes.SelfApproval);
    }

    [Fact]
    public async Task The_owner_of_the_shop_can_approve_a_count_they_took_themselves()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 7m, 50m);

        int countId = await SaveStockCountAsync(productId, 5m);

        await new SubmitStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, _session)
            .HandleAsync(new SubmitStockCountCommand(countId), CancellationToken.None);

        // The owner of a one-account shop is the only person who can sign off the count.
        _session.IsAdministrator = true;

        Result approved = await new ApproveStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new ApproveStockCountCommand(countId, "Recount of the shelf"), CancellationToken.None);

        approved.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_large_adjustment_waits_for_a_second_approver()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        Result<StockAdjustmentOutcome> outcome = await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, 200m, StockDisposition.Sellable, new Money(50m), "Found in the back store"),
                CancellationToken.None);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Value.Posted.ShouldBeFalse();
        outcome.Value.ApprovalRequestId.ShouldNotBeNull();

        await using (StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync())
        {
            StockBalance balance = await context.StockBalances.SingleAsync();

            balance.QuantityOnHandScaled.ShouldBe(10);
        }

        _session.UserId = _secondUserId;

        Result<ApprovalRequestSummary> approved = await new DecideApprovalRequestCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new DecideApprovalRequestCommand(outcome.Value.ApprovalRequestId!.Value, true, "Checked"),
                CancellationToken.None);

        approved.IsSuccess.ShouldBeTrue();

        await using StoreFlowDbContext contextAfter = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balanceAfter = await contextAfter.StockBalances.SingleAsync();

        balanceAfter.QuantityOnHandScaled.ShouldBe(210);
    }

    [Fact]
    public async Task A_small_adjustment_reaches_the_ledger_straight_away()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        Result<StockAdjustmentOutcome> outcome = await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, -2m, StockDisposition.Sellable, new Money(50m), "Broken"),
                CancellationToken.None);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Value.Posted.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(8);
        balance.AverageUnitCost.ShouldBe(new Money(50m));
    }

    [Fact]
    public async Task An_adjustment_the_selling_unit_cannot_express_is_refused()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        // 2.6 pieces of a product sold by the piece cannot be written down. Rounding it to 3 on the
        // way to the ledger would leave the book claiming three more bags than the counter found.
        Result<StockAdjustmentOutcome> refused = await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, 2.6m, StockDisposition.Sellable, new Money(50m), "Found in the back store"),
                CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(InventoryValidation.QuantityTooPreciseCode);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(10);
        (await context.StockMovements.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task An_adjustment_the_selling_unit_can_express_reaches_the_ledger_as_it_was_typed()
    {
        // The kilogram is the seeded unit with a precision of three, so 2.6 of it is a quantity the
        // unit can express and must reach the ledger unscaled.
        int productId = await CreateProductAsync("RICE-001", 3);

        await PostPurchaseAsync(productId, 10m, 50m);

        Result<StockAdjustmentOutcome> outcome = await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, 2.6m, StockDisposition.Sellable, new Money(50m), "Found in the back store"),
                CancellationToken.None);

        outcome.IsSuccess.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(12600);
    }

    [Fact]
    public async Task Damaged_stock_never_inflates_the_sellable_quantity()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        await new PostStockAdjustmentCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, 3m, StockDisposition.Damaged, new Money(50m), "Water damage"),
                CancellationToken.None);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(10);
        balance.DamagedQuantityScaled.ShouldBe(3);
    }

    [Fact]
    public async Task Opening_stock_is_recorded_as_an_opening_movement()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        Result<int> recorded = await new RecordOpeningStockCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new RecordOpeningStockCommand(
                    [new OpeningStockLine(productId, 12m, new Money(40m))],
                    new DateOnly(2026, 1, 1),
                    "Opening balance"),
                CancellationToken.None);

        recorded.IsSuccess.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockMovement movement = await context.StockMovements.SingleAsync();

        movement.MovementType.ShouldBe(StockMovementType.Opening);
        movement.QuantityDeltaScaled.ShouldBe(12);

        StockBalance balance = await context.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(12);
        balance.AverageUnitCost.ShouldBe(new Money(40m));
    }

    [Fact]
    public async Task Opening_stock_is_refused_when_the_product_already_has_movements()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        Result<int> recorded = await new RecordOpeningStockCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new RecordOpeningStockCommand(
                    [new OpeningStockLine(productId, 12m, new Money(40m))],
                    new DateOnly(2026, 1, 1),
                    null),
                CancellationToken.None);

        recorded.IsFailure.ShouldBeTrue();
        recorded.Error.Code.ShouldBe(InventoryErrorCodes.AlreadyStocked);
    }

    [Fact]
    public async Task The_valuation_uses_the_moving_average()
    {
        int productId = await CreateProductAsync("VAL-004", 0);

        await PostPurchaseAsync(productId, 10m, 50m);

        int secondPurchase = await SavePurchaseAsync(productId, 10m, 70m);

        await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(secondPurchase), CancellationToken.None);

        Result<InventoryValuation> valuation = await new GetInventoryValuationQueryHandler(
            _fixture.Factory, _authorization, new TestClock())
            .HandleAsync(new GetInventoryValuationQuery(), CancellationToken.None);

        valuation.IsSuccess.ShouldBeTrue();
        valuation.Value.ProductCount.ShouldBe(1);
        valuation.Value.TotalQuantity.ShouldBe(20m);
        valuation.Value.TotalValue.ShouldBe(new Money(1200m));
    }

    [Fact]
    public async Task The_valuation_at_a_past_date_replays_the_ledger()
    {
        int productId = await CreateProductAsync("VAL-004", 0);

        await PostPurchaseAsync(productId, 10m, 50m, new DateOnly(2026, 1, 1));

        int secondPurchase = await SavePurchaseAsync(productId, 10m, 70m, new DateOnly(2026, 2, 1));

        await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(secondPurchase), CancellationToken.None);

        var handler = new GetInventoryValuationQueryHandler(_fixture.Factory, _authorization, new TestClock());

        Result<InventoryValuation> january = await handler.HandleAsync(
            new GetInventoryValuationQuery(new DateOnly(2026, 1, 31)), CancellationToken.None);

        january.IsSuccess.ShouldBeTrue();
        january.Value.TotalQuantity.ShouldBe(10m);
        january.Value.TotalValue.ShouldBe(new Money(500m));

        Result<InventoryValuation> february = await handler.HandleAsync(
            new GetInventoryValuationQuery(new DateOnly(2026, 2, 28)), CancellationToken.None);

        february.IsSuccess.ShouldBeTrue();
        february.Value.TotalQuantity.ShouldBe(20m);
        february.Value.TotalValue.ShouldBe(new Money(1200m));
    }

    [Fact]
    public async Task Low_stock_lists_the_products_at_or_below_the_reorder_level()
    {
        int lowId = await CreateProductAsync("LOW-001", 0, reorderLevel: 5m);
        int fineId = await CreateProductAsync("FINE-001", 0, reorderLevel: 5m);

        await PostPurchaseAsync(lowId, 3m, 50m);
        await PostPurchaseAsync(fineId, 20m, 50m);

        Result<IReadOnlyList<LowStockItem>> low = await new ListLowStockQueryHandler(
            _fixture.Factory, _authorization)
            .HandleAsync(new ListLowStockQuery(), CancellationToken.None);

        low.IsSuccess.ShouldBeTrue();
        low.Value.Count.ShouldBe(1);
        low.Value[0].ProductId.ShouldBe(lowId);
        low.Value[0].QuantityOnHand.ShouldBe(3m);
        low.Value[0].ReorderLevel.ShouldBe(5m);
    }

    [Fact]
    public async Task The_purchase_register_can_be_exported_to_a_csv_file()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        int purchaseId = await SavePurchaseAsync(productId, 10m, 50m);

        await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

        string path = Path.Combine(Path.GetTempPath(), $"storeflow-purchases-{Guid.NewGuid():N}.csv");

        try
        {
            Result<int> exported = await new ExportPurchasesCommandHandler(
                _fixture.Factory, _authorization, _auditWriter, new CsvExportService(), _session)
                .HandleAsync(
                    new ExportPurchasesCommand(
                        path,
                        new PurchaseFilter(),
                        new Dictionary<string, string> { ["number"] = "Number" }),
                    CancellationToken.None);

            exported.IsSuccess.ShouldBeTrue();
            exported.Value.ShouldBe(1);

            string[] lines = await File.ReadAllLinesAsync(path);

            lines.Length.ShouldBe(2);
            lines[0].ShouldStartWith("Number,");
            lines[1].ShouldContain("PUR-");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task A_purchase_cannot_be_posted_without_lines()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        int purchaseId = await SavePurchaseAsync(productId, 10m, 50m);

        await using (StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync())
        {
            PurchaseDocument document = await context.PurchaseDocuments.SingleAsync();

            document.Status.ShouldBe(DocumentStatus.Draft);

            context.PurchaseLines.RemoveRange(context.PurchaseLines.ToList());
            await context.SaveChangesAsync();
        }

        Result<int> posted = await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

        posted.IsFailure.ShouldBeTrue();
        posted.Error.Code.ShouldBe(InventoryErrorCodes.EmptyDocument);
    }

    [Fact]
    public async Task The_register_lists_the_purchases_the_owner_searched_for()
    {
        int productId = await CreateProductAsync("RICE-001", 0);

        await SavePurchaseAsync(productId, 10m, 50m);
        await SavePurchaseAsync(productId, 5m, 60m);

        var handler = new ListPurchasesQueryHandler(_fixture.Factory, _authorization);

        Result<IReadOnlyList<PurchaseSummary>> all = await handler.HandleAsync(
            new ListPurchasesQuery(new PurchaseFilter()), CancellationToken.None);

        all.IsSuccess.ShouldBeTrue();
        all.Value.Count.ShouldBe(2);

        string number = all.Value[0].Number;

        Result<IReadOnlyList<PurchaseSummary>> found = await handler.HandleAsync(
            new ListPurchasesQuery(new PurchaseFilter(Search: number)), CancellationToken.None);

        found.IsSuccess.ShouldBeTrue();
        found.Value.Count.ShouldBe(1);
        found.Value[0].Number.ShouldBe(number);
    }

    private async Task<int> CreateUserAsync(string username, string displayName)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Role role = await context.Roles.FirstAsync(candidate => candidate.Code == "owner");

        var user = new User
        {
            Username = username,
            DisplayName = displayName,
            PasswordHash = "pbkdf2$sha256$1$c2FsdA$aGFzaA==",
            RoleId = role.Id,
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        context.Users.Add(user);

        await context.SaveChangesAsync();

        return user.Id;
    }

    private async Task<int> CreateProductAsync(string sku, int unitPrecision, decimal reorderLevel = 0m)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Unit unit = await context.Units.FirstAsync(candidate => candidate.Precision == unitPrecision);

        var product = new Product
        {
            Sku = sku,
            Name = sku,
            UnitId = unit.Id,
            TaxCategoryId = (await context.TaxCategories.FirstAsync()).Id,
            SalePrice = new Money(100m),
            CostPrice = new Money(50m),
            ReorderLevelScaled = (long)(reorderLevel * (decimal)Math.Pow(10, unitPrecision)),
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        context.Products.Add(product);

        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task<int> SavePurchaseAsync(
        int productId,
        decimal quantity,
        decimal unitCost,
        DateOnly? businessDate = null)
    {
        Result<int> saved = await new SavePurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new SavePurchaseCommand(
                    null,
                    null,
                    businessDate ?? new DateOnly(2026, 1, 1),
                    [new PurchaseLineInput(productId, quantity, new Money(unitCost), Money.Zero)],
                    Money.Zero,
                    null),
                CancellationToken.None);

        saved.IsFailure.ShouldBeFalse(saved.Error.Message);

        return saved.Value;
    }

    private async Task PostPurchaseAsync(
        int productId,
        decimal quantity,
        decimal unitCost,
        DateOnly? businessDate = null)
    {
        int purchaseId = await SavePurchaseAsync(productId, quantity, unitCost, businessDate);

        Result<int> posted = await new PostPurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(purchaseId), CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);
    }

    private async Task<int> SaveStockCountAsync(int productId, decimal countedQuantity)
    {
        Result<int> saved = await new SaveStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new SaveStockCountCommand(
                    null,
                    new DateOnly(2026, 1, 1),
                    [new StockCountLineInput(productId, countedQuantity)]),
                CancellationToken.None);

        saved.IsFailure.ShouldBeFalse(saved.Error.Message);

        return saved.Value;
    }

    /// <summary>
    /// Submits a count, has it approved by somebody other than the counter, and posts it.
    /// </summary>
    private async Task ApproveAndPostCountAsync(int countId)
    {
        Result submitted = await new SubmitStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, _session)
            .HandleAsync(new SubmitStockCountCommand(countId), CancellationToken.None);

        submitted.IsSuccess.ShouldBeTrue();

        // The counter may not sign off their own count, so the second user approves it.
        _session.UserId = _secondUserId;

        Result approved = await new ApproveStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new ApproveStockCountCommand(countId, "Recount of the shelf"), CancellationToken.None);

        approved.IsSuccess.ShouldBeTrue();

        Result<int> posted = await new PostStockCountCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostStockCountCommand(countId), CancellationToken.None);

        posted.IsSuccess.ShouldBeTrue(posted.Error.Message);
    }

    /// <summary>Authorises everything, so these tests exercise the inventory and not the roles.</summary>
    private sealed class AllowAllAuthorization : IAuthorizationService
    {
        public bool IsAllowed(Permission permission) => true;

        public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>A session with one signed-in user, so the audit trail names an actor.</summary>
    private sealed class TestSession : IUserSession
    {
        public int UserId { get; set; }

        public string DisplayName { get; set; } = "Tester";

        /// <summary>
        /// False by default: an ordinary clerk may not approve their own count. The owner of the
        /// shop is an administrator, and an administrator may.
        /// </summary>
        public bool IsAdministrator { get; set; }

        public UserSession? Current => new(
            UserId,
            "tester",
            DisplayName,
            new HashSet<Permission>(),
            IsAdministrator);

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
}
