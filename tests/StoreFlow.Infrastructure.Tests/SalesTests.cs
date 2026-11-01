using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Pos;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Pos;
using StoreFlow.Infrastructure.Sales;
using StoreFlow.Infrastructure.Services;
using StoreFlow.Infrastructure.Settings;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The sales register and the returns against a real SQLite database: the real return document, the
/// real refund, the real stock movements and the real immutability triggers.
/// </summary>
/// <remarks>
/// Every test buys its own product, so no test shares stock with another, and every test works on
/// a database of its own, so the document numbers of one test never disturb another.
/// </remarks>
public sealed class SalesTests : IAsyncLifetime
{
    /// <summary>The document date every sale in this file is written under.</summary>
    private static readonly DateOnly BusinessDate = new(2026, 1, 1);

    private readonly DatabaseFixture _fixture = new();

    private AllowAllAuthorization _authorization = null!;
    private EfAuditWriter _auditWriter = null!;
    private TestSession _session = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        _authorization = new AllowAllAuthorization();
        _session = new TestSession();
        _auditWriter = new EfAuditWriter(_fixture.Factory, new TestClock(), _session);

        _session.UserId = await CreateUserAsync("cashier", "Shop cashier");
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_return_is_a_document_linked_to_the_invoice_it_undoes()
    {
        int productId = await CreateProductAsync("RET-001", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        Result<RefundReceipt> posted = await PostReturnAsync(
            invoiceId,
            [new ReturnLineInput(await LineId(invoiceId), 1m, true)]);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);
        posted.Value.Kind.ShouldBe(SalesDocumentKind.Return);
        posted.Value.Number.ShouldBe("RET-2026-0001");
        posted.Value.OriginalInvoiceNumber.ShouldBe("INV-2026-0001");
        posted.Value.RefundTotal.ShouldBe(new Money(100m));

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesReturn salesReturn = await context.SalesReturns.SingleAsync();

        salesReturn.OriginalInvoiceId.ShouldBe(invoiceId);
        salesReturn.Reason.ShouldBe("Wrong item");
        salesReturn.RefundTotal.ShouldBe(new Money(100m));
        salesReturn.RefundMethod.ShouldBe(PaymentMethod.Cash);
        salesReturn.RefundTax.ShouldBe(Money.Zero);

        SalesReturnLine returnLine = await context.SalesReturnLines.SingleAsync();

        returnLine.QuantityScaled.ShouldBe(1);
        returnLine.IsResellable.ShouldBeTrue();
        returnLine.RefundAmount.ShouldBe(new Money(100m));
        returnLine.OriginalUnitCost.ShouldBe(new Money(50m));

        // The refund is a payment row of its own, so every movement of money is in one place.
        Payment refund = await context.Payments.SingleAsync(payment => payment.IsRefund);

        refund.SalesReturnId.ShouldBe(salesReturn.Id);
        refund.SalesInvoiceId.ShouldBeNull();
        refund.Amount.ShouldBe(new Money(100m));
        refund.Method.ShouldBe(PaymentMethod.Cash);
    }

    [Fact]
    public async Task A_resellable_return_puts_the_stock_back_and_restores_the_average()
    {
        int productId = await CreateProductAsync("RET-002", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        await PostPurchaseAsync(productId, 10m, 70m);
        int invoiceId = await PostSaleAsync(productId, 5m, 100m);

        await using (StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync())
        {
            StockBalance balance = await context.StockBalances.SingleAsync();

            // Two receipts at 50 and 70 make the average 60, and the sale of five leaves 15 at 60.
            balance.QuantityOnHandScaled.ShouldBe(15);
            balance.AverageUnitCost.ShouldBe(new Money(60m));
        }

        Result<RefundReceipt> posted = await PostReturnAsync(
            invoiceId,
            [new ReturnLineInput(await LineId(invoiceId), 5m, true)]);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using StoreFlowDbContext after = await _fixture.Factory.CreateDbContextAsync();

        StockBalance restored = await after.StockBalances.SingleAsync();

        // The shelf has the five back and the average is the one it had before the sale, because a
        // resellable return is valued at the cost the line was sold at (ADR-0003).
        restored.QuantityOnHandScaled.ShouldBe(20);
        restored.AverageUnitCost.ShouldBe(new Money(60m));

        StockMovement movement = await after.StockMovements
            .SingleAsync(candidate => candidate.MovementType == StockMovementType.ReturnRestock);

        movement.QuantityDeltaScaled.ShouldBe(5);
        movement.Disposition.ShouldBe(StockDisposition.Sellable);
        movement.UnitCost.ShouldBe(new Money(60m));
    }

    [Fact]
    public async Task A_damaged_return_is_not_put_back_on_the_shelf()
    {
        int productId = await CreateProductAsync("RET-003", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        Result<RefundReceipt> posted = await PostReturnAsync(
            invoiceId,
            [new ReturnLineInput(await LineId(invoiceId), 1m, false)]);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        // The sellable quantity stays where the sale left it, and the damaged bucket takes the unit.
        balance.QuantityOnHandScaled.ShouldBe(7);
        balance.DamagedQuantityScaled.ShouldBe(1);

        StockMovement movement = await context.StockMovements
            .SingleAsync(candidate => candidate.MovementType == StockMovementType.ReturnDamaged);

        movement.QuantityDeltaScaled.ShouldBe(1);
        movement.Disposition.ShouldBe(StockDisposition.Damaged);
    }

    [Fact]
    public async Task Returning_more_than_was_sold_is_refused()
    {
        int productId = await CreateProductAsync("RET-004", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        Result<RefundReceipt> posted = await PostReturnAsync(
            invoiceId,
            [new ReturnLineInput(await LineId(invoiceId), 4m, true)]);

        posted.IsFailure.ShouldBeTrue();
        posted.Error.Code.ShouldBe(SalesErrorCodes.QuantityExceedsSold);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        // Nothing was written: no return, no refund, and the stock did not move.
        (await context.SalesReturns.CountAsync()).ShouldBe(0);
        (await context.StockMovements.CountAsync(candidate => candidate.MovementType == StockMovementType.ReturnRestock))
            .ShouldBe(0);
    }

    [Fact]
    public async Task The_returns_against_a_line_add_up_and_stop_at_what_was_sold()
    {
        int productId = await CreateProductAsync("RET-005", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);
        int lineId = await LineId(invoiceId);

        Result<RefundReceipt> first = await PostReturnAsync(invoiceId, [new ReturnLineInput(lineId, 2m, true)]);
        first.IsFailure.ShouldBeFalse(first.Error.Message);

        Result<RefundReceipt> second = await PostReturnAsync(invoiceId, [new ReturnLineInput(lineId, 1m, true)]);
        second.IsFailure.ShouldBeFalse(second.Error.Message);

        Result<RefundReceipt> third = await PostReturnAsync(invoiceId, [new ReturnLineInput(lineId, 1m, true)]);
        third.IsFailure.ShouldBeTrue();
        third.Error.Code.ShouldBe(SalesErrorCodes.QuantityExceedsRemaining);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoiceLine line = await context.SalesInvoiceLines.SingleAsync();

        line.ReturnedQuantityScaled.ShouldBe(3);

        // The counter on the line and the return lines agree with each other.
        long fromReturns = await context.SalesReturnLines.SumAsync(candidate => candidate.QuantityScaled);
        fromReturns.ShouldBe(3);
    }

    [Fact]
    public async Task A_return_without_a_reason_is_refused()
    {
        int productId = await CreateProductAsync("RET-006", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        Result<RefundReceipt> posted = await PostReturnAsync(
            invoiceId,
            [new ReturnLineInput(await LineId(invoiceId), 1m, true)],
            reason: "   ");

        posted.IsFailure.ShouldBeTrue();
        posted.Error.Code.ShouldBe(SalesErrorCodes.ReasonRequired);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.SalesReturns.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_posted_invoice_line_cannot_be_edited()
    {
        int productId = await CreateProductAsync("RET-007", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        await using (StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync())
        {
            SalesInvoiceLine line = await context.SalesInvoiceLines.SingleAsync();

            line.UnitPrice = new Money(999m);

            await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using StoreFlowDbContext after = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoiceLine untouched = await after.SalesInvoiceLines.SingleAsync();

        untouched.UnitPrice.ShouldBe(new Money(100m));
    }

    [Fact]
    public async Task A_reversal_undoes_the_invoice_without_editing_it()
    {
        int productId = await CreateProductAsync("REV-001", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        Result<RefundReceipt> posted = await ReverseAsync(invoiceId);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);
        posted.Value.Kind.ShouldBe(SalesDocumentKind.Reversal);
        posted.Value.Number.ShouldBe("REV-2026-0001");
        posted.Value.RefundTotal.ShouldBe(new Money(300m));

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoice original = await context.SalesInvoices
            .SingleAsync(candidate => candidate.Id == invoiceId);

        original.Status.ShouldBe(DocumentStatus.Reversed);

        SalesInvoice reversal = await context.SalesInvoices
            .Include(candidate => candidate.Lines)
            .SingleAsync(candidate => candidate.ReversesDocumentId == invoiceId);

        reversal.Number.ShouldBe("REV-2026-0001");
        reversal.Status.ShouldBe(DocumentStatus.Posted);
        reversal.Total.ShouldBe(new Money(300m));
        reversal.Lines.Count.ShouldBe(1);

        Payment refund = await context.Payments.SingleAsync(payment => payment.IsRefund);

        refund.SalesInvoiceId.ShouldBe(reversal.Id);
        refund.Amount.ShouldBe(new Money(300m));

        // The shelf has the goods back and the average is the one it had before the sale.
        StockBalance balance = await context.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(10);
        balance.AverageUnitCost.ShouldBe(new Money(50m));

        StockMovement movement = await context.StockMovements
            .SingleAsync(candidate => candidate.MovementType == StockMovementType.Reversal);

        movement.QuantityDeltaScaled.ShouldBe(3);
        movement.UnitCost.ShouldBe(new Money(50m));
    }

    [Fact]
    public async Task An_invoice_cannot_be_reversed_twice()
    {
        int productId = await CreateProductAsync("REV-002", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        Result<RefundReceipt> first = await ReverseAsync(invoiceId);
        first.IsFailure.ShouldBeFalse(first.Error.Message);

        Result<RefundReceipt> second = await ReverseAsync(invoiceId);
        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(SalesErrorCodes.InvoiceAlreadyReversed);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.SalesInvoices.CountAsync(candidate => candidate.ReversesDocumentId != null)).ShouldBe(1);
    }

    [Fact]
    public async Task A_reversed_invoice_cannot_be_returned_against()
    {
        int productId = await CreateProductAsync("REV-003", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        await ReverseAsync(invoiceId);

        Result<RefundReceipt> posted = await PostReturnAsync(
            invoiceId,
            [new ReturnLineInput(await LineId(invoiceId), 1m, true)]);

        posted.IsFailure.ShouldBeTrue();
        posted.Error.Code.ShouldBe(SalesErrorCodes.InvoiceAlreadyReversed);
    }

    [Fact]
    public async Task The_register_shows_the_sale_the_return_and_the_reversal()
    {
        int productId = await CreateProductAsync("REG-001", 100m);
        await PostPurchaseAsync(productId, 20m, 50m);
        int saleId = await PostSaleAsync(productId, 3m, 100m);
        await PostReturnAsync(saleId, [new ReturnLineInput(await LineId(saleId), 1m, true)]);

        int otherProductId = await CreateProductAsync("REG-002", 100m);
        await PostPurchaseAsync(otherProductId, 20m, 50m);
        int reversalId = await PostSaleAsync(otherProductId, 2m, 100m);
        await ReverseAsync(reversalId);

        Result<IReadOnlyList<SalesRegisterRow>> rows = await new ListSalesRegisterQueryHandler(
            _fixture.Factory,
            _authorization)
            .HandleAsync(new ListSalesRegisterQuery(new SalesFilter(string.Empty)), CancellationToken.None);

        rows.IsFailure.ShouldBeFalse(rows.Error.Message);

        SalesRegisterRow sale = rows.Value.Single(row => row.Number == "INV-2026-0001");
        sale.Kind.ShouldBe(SalesDocumentKind.Invoice);
        sale.Total.ShouldBe(new Money(300m));
        sale.ReturnedAmount.ShouldBe(new Money(100m));

        SalesRegisterRow returned = rows.Value.Single(row => row.Number == "RET-2026-0001");
        returned.Kind.ShouldBe(SalesDocumentKind.Return);
        returned.Total.ShouldBe(new Money(-100m));
        returned.RelatedNumber.ShouldBe("INV-2026-0001");

        SalesRegisterRow reversal = rows.Value.Single(row => row.Number == "REV-2026-0001");
        reversal.Kind.ShouldBe(SalesDocumentKind.Reversal);
        reversal.Total.ShouldBe(new Money(-200m));
        reversal.RelatedNumber.ShouldBe("INV-2026-0002");
    }

    [Fact]
    public async Task The_detail_of_an_invoice_shows_what_has_been_given_back()
    {
        int productId = await CreateProductAsync("DET-001", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);
        int lineId = await LineId(invoiceId);

        await PostReturnAsync(invoiceId, [new ReturnLineInput(lineId, 1m, true)]);

        Result<SalesInvoiceDetails> details = await new GetSalesInvoiceQueryHandler(
            _fixture.Factory,
            _authorization)
            .HandleAsync(new GetSalesInvoiceQuery(invoiceId), CancellationToken.None);

        details.IsFailure.ShouldBeFalse(details.Error.Message);
        details.Value.ReturnedAmount.ShouldBe(new Money(100m));
        details.Value.Returns.Count.ShouldBe(1);
        details.Value.Returns[0].Number.ShouldBe("RET-2026-0001");
        details.Value.Returns[0].RefundTotal.ShouldBe(new Money(100m));
        details.Value.Returns[0].IsFullReturn.ShouldBeFalse();
        details.Value.Lines[0].ReturnedQuantityScaled.ShouldBe(1);
    }

    [Fact]
    public async Task A_return_that_takes_the_whole_invoice_is_marked_as_a_full_return()
    {
        int productId = await CreateProductAsync("DET-002", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        await PostReturnAsync(invoiceId, [new ReturnLineInput(await LineId(invoiceId), 3m, true)]);

        Result<SalesInvoiceDetails> details = await new GetSalesInvoiceQueryHandler(
            _fixture.Factory,
            _authorization)
            .HandleAsync(new GetSalesInvoiceQuery(invoiceId), CancellationToken.None);

        details.IsFailure.ShouldBeFalse(details.Error.Message);
        details.Value.Returns[0].IsFullReturn.ShouldBeTrue();
        details.Value.ReturnedAmount.ShouldBe(new Money(300m));
    }

    [Fact]
    public async Task The_price_of_a_product_does_not_change_the_invoice_that_was_already_posted()
    {
        int productId = await CreateProductAsync("PRC-001", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);
        int invoiceId = await PostSaleAsync(productId, 3m, 100m);

        await using (StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync())
        {
            Product product = await context.Products.SingleAsync(candidate => candidate.Id == productId);

            product.SalePrice = new Money(500m);

            await context.SaveChangesAsync();
        }

        Result<SalesInvoiceDetails> details = await new GetSalesInvoiceQueryHandler(
            _fixture.Factory,
            _authorization)
            .HandleAsync(new GetSalesInvoiceQuery(invoiceId), CancellationToken.None);

        details.IsFailure.ShouldBeFalse(details.Error.Message);
        details.Value.Total.ShouldBe(new Money(300m));
        details.Value.Lines[0].UnitPrice.ShouldBe(new Money(100m));
    }

    private async Task<Result<RefundReceipt>> PostReturnAsync(
        int invoiceId,
        IReadOnlyList<ReturnLineInput> lines,
        string reason = "Wrong item") =>
        await new PostReturnCommandHandler(
            _fixture.Factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _session)
            .HandleAsync(
                new PostReturnCommand(invoiceId, BusinessDate, lines, PaymentMethod.Cash, reason),
                CancellationToken.None);

    private async Task<Result<RefundReceipt>> ReverseAsync(int invoiceId) =>
        await new ReverseInvoiceCommandHandler(
            _fixture.Factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _session)
            .HandleAsync(
                new ReverseInvoiceCommand(invoiceId, BusinessDate, PaymentMethod.Cash, "Keyed by mistake"),
                CancellationToken.None);

    private async Task<int> PostSaleAsync(int productId, decimal quantity, decimal unitPrice)
    {
        Result<SaleReceipt> posted = await new PostSaleCommandHandler(
            _fixture.Factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _session,
            new StoreSettingsReader(_fixture.Factory))
            .HandleAsync(
                new PostSaleCommand(
                    Guid.NewGuid(),
                    BusinessDate,
                    [new SaleLineInput(productId, quantity, new Money(unitPrice), DiscountSpec.None)],
                    DiscountSpec.None,
                    [new PaymentInput(PaymentMethod.Cash, new Money(quantity * unitPrice), new Money(quantity * unitPrice), null)]),
                CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        return posted.Value.InvoiceId;
    }

    private async Task<int> LineId(int invoiceId)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoiceLine line = await context.SalesInvoiceLines
            .SingleAsync(candidate => candidate.SalesInvoiceId == invoiceId);

        return line.Id;
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

    private async Task<int> CreateProductAsync(string sku, decimal salePrice)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Unit unit = await context.Units.FirstAsync(candidate => candidate.Precision == 0);
        TaxCategory taxCategory = await context.TaxCategories.FirstAsync();

        var product = new Product
        {
            Sku = sku,
            Name = sku,
            UnitId = unit.Id,
            TaxCategoryId = taxCategory.Id,
            SalePrice = new Money(salePrice),
            CostPrice = new Money(50m),
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        context.Products.Add(product);

        await context.SaveChangesAsync();

        return product.Id;
    }

    /// <summary>Puts stock on the shelf the way the shop does it: a posted purchase.</summary>
    private async Task PostPurchaseAsync(int productId, decimal quantity, decimal unitCost)
    {
        Result<int> saved = await new SavePurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
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
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(new PostPurchaseCommand(saved.Value), CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);
    }

    /// <summary>Authorises everything, so these tests exercise the register and not the roles.</summary>
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
