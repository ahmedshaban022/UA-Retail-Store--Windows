using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Pos;
using StoreFlow.Infrastructure.Services;
using StoreFlow.Infrastructure.Settings;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The point of sale against a real SQLite database: the real invoice, the real payment, the real
/// stock ledger and the real idempotency key.
/// </summary>
/// <remarks>
/// <para>Every test buys its own product, so no test shares stock with another, and every test
/// works on a database of its own, so the invoice numbers of one test never disturb another.</para>
/// </remarks>
public sealed class SaleTests : IAsyncLifetime
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
    public async Task A_sale_is_posted()
    {
        int productId = await CreateProductAsync("SALE-001", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        Result<SaleReceipt> posted = await CreateHandler().HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 2m, 100m, [CashPayment(200m, 200m)]),
            CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoice invoice = await context.SalesInvoices.SingleAsync();

        invoice.Number.ShouldBe("INV-2026-0001");
        invoice.Status.ShouldBe(DocumentStatus.Posted);
        invoice.BusinessDate.ShouldBe(BusinessDate);
        invoice.Total.ShouldBe(new Money(200m));
        invoice.ClientOperationId.ShouldNotBe(Guid.Empty);

        SalesInvoiceLine line = await context.SalesInvoiceLines.SingleAsync();

        line.ProductId.ShouldBe(productId);
        line.QuantityScaled.ShouldBe(2);
        line.QuantityScale.ShouldBe(0);

        Payment payment = await context.Payments.SingleAsync();

        payment.Method.ShouldBe(PaymentMethod.Cash);
        payment.Amount.ShouldBe(new Money(200m));
        payment.CashReceived.ShouldBe(new Money(200m));

        StockMovement movement = await context.StockMovements
            .SingleAsync(candidate => candidate.MovementType == StockMovementType.Sale);

        movement.ProductId.ShouldBe(productId);
        movement.QuantityDeltaScaled.ShouldBe(-2);
        movement.Disposition.ShouldBe(StockDisposition.Sellable);
        movement.DocumentId.ShouldBe(invoice.Id);
    }

    [Fact]
    public async Task The_stock_comes_off_the_shelf()
    {
        int productId = await CreateProductAsync("SALE-002", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        await using (StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync())
        {
            StockBalance before = await context.StockBalances.SingleAsync();

            before.QuantityOnHandScaled.ShouldBe(10);
        }

        Result<SaleReceipt> posted = await CreateHandler().HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 2m, 100m, [CashPayment(200m, 200m)]),
            CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using StoreFlowDbContext contextAfter = await _fixture.Factory.CreateDbContextAsync();

        StockBalance after = await contextAfter.StockBalances.SingleAsync();

        after.QuantityOnHandScaled.ShouldBe(8);
    }

    [Fact]
    public async Task The_same_checkout_posted_twice_is_one_sale()
    {
        int productId = await CreateProductAsync("SALE-003", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        Guid operationId = Guid.NewGuid();
        PostSaleCommand command = SaleCommand(operationId, productId, 2m, 100m, [CashPayment(200m, 200m)]);

        PostSaleCommandHandler handler = CreateHandler();

        Result<SaleReceipt> first = await handler.HandleAsync(command, CancellationToken.None);
        Result<SaleReceipt> second = await handler.HandleAsync(command, CancellationToken.None);

        first.IsFailure.ShouldBeFalse(first.Error.Message);
        second.IsFailure.ShouldBeFalse(second.Error.Message);

        first.Value.WasReplay.ShouldBeFalse();
        second.Value.WasReplay.ShouldBeTrue();
        second.Value.InvoiceId.ShouldBe(first.Value.InvoiceId);
        second.Value.Number.ShouldBe(first.Value.Number);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.SalesInvoices.CountAsync()).ShouldBe(1);
        (await context.SalesInvoiceLines.CountAsync()).ShouldBe(1);
        (await context.Payments.CountAsync()).ShouldBe(1);
        (await context.StockMovements.CountAsync(movement => movement.MovementType == StockMovementType.Sale))
            .ShouldBe(1);
    }

    [Fact]
    public async Task The_same_checkout_key_with_a_different_cart_is_refused()
    {
        int productId = await CreateProductAsync("SALE-004", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        Guid operationId = Guid.NewGuid();
        PostSaleCommandHandler handler = CreateHandler();

        Result<SaleReceipt> first = await handler.HandleAsync(
            SaleCommand(operationId, productId, 2m, 100m, [CashPayment(200m, 200m)]),
            CancellationToken.None);

        first.IsFailure.ShouldBeFalse(first.Error.Message);

        // The same checkout key, but a different cart: a mistake worth refusing, not a second sale.
        Result<SaleReceipt> conflict = await handler.HandleAsync(
            SaleCommand(operationId, productId, 3m, 100m, [CashPayment(300m, 300m)]),
            CancellationToken.None);

        conflict.IsFailure.ShouldBeTrue();
        conflict.Error.Code.ShouldBe(PosErrorCodes.IdempotencyConflict);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.SalesInvoices.CountAsync()).ShouldBe(1);
        (await context.SalesInvoiceLines.CountAsync()).ShouldBe(1);
        (await context.StockMovements.CountAsync(movement => movement.MovementType == StockMovementType.Sale))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Selling_more_than_the_shelf_holds_is_refused()
    {
        int productId = await CreateProductAsync("SALE-005", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        Result<SaleReceipt> refused = await CreateHandler().HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 11m, 100m, [CashPayment(1100m, 1100m)]),
            CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(PosErrorCodes.InsufficientStock);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.SalesInvoices.CountAsync()).ShouldBe(0);
        (await context.SalesInvoiceLines.CountAsync()).ShouldBe(0);
        (await context.Payments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Selling_more_than_the_shelf_holds_leaves_the_stock_alone()
    {
        int productId = await CreateProductAsync("SALE-006", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        Result<SaleReceipt> refused = await CreateHandler().HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 11m, 100m, [CashPayment(1100m, 1100m)]),
            CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(PosErrorCodes.InsufficientStock);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await context.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(10);
        (await context.StockMovements.CountAsync()).ShouldBe(1);
        (await context.SalesInvoices.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Cash_that_is_short_is_refused()
    {
        int productId = await CreateProductAsync("SALE-007", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        // The sale is 200.00. The cashier applies the whole 200.00 to the invoice, but only
        // hands over 150.00 in cash.
        Result<SaleReceipt> refused = await CreateHandler().HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 2m, 100m, [CashPayment(200m, 150m)]),
            CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(PosErrorCodes.InsufficientCash);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.SalesInvoices.CountAsync()).ShouldBe(0);
        (await context.SalesInvoiceLines.CountAsync()).ShouldBe(0);
        (await context.Payments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_change_is_the_cash_over_the_total()
    {
        int productId = await CreateProductAsync("SALE-008", 30m);
        await PostPurchaseAsync(productId, 10m, 15m);

        // Two pieces at 30.00 is a sale of 60.00. The till applies the 60.00 to the invoice and
        // takes 100.00 in cash, so 40.00 comes back.
        Result<SaleReceipt> posted = await CreateHandler().HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 2m, 30m, [CashPayment(60m, 100m)]),
            CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        posted.Value.Totals.Total.ShouldBe(new Money(60m));
        posted.Value.Totals.Paid.ShouldBe(new Money(60m));
        posted.Value.Totals.Change.ShouldBe(new Money(40m));

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoice invoice = await context.SalesInvoices.SingleAsync();

        invoice.Total.ShouldBe(new Money(60m));
        invoice.Paid.ShouldBe(new Money(60m));
        invoice.Change.ShouldBe(new Money(40m));

        Payment payment = await context.Payments.SingleAsync();

        payment.Amount.ShouldBe(new Money(60m));
        payment.CashReceived.ShouldBe(new Money(100m));
        payment.Change.ShouldBe(new Money(40m));
    }

    [Fact]
    public async Task The_invoice_totals_are_the_cart_totals()
    {
        int productId = await CreateProductAsync("SALE-009", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        IReadOnlyList<SaleLineInput> lines =
        [
            new SaleLineInput(productId, 2m, new Money(100m), DiscountSpec.None),
        ];

        var facts = new Dictionary<int, PosProductFacts>
        {
            [productId] = await ReadProductFactsAsync(productId),
        };

        PosCart cart = PosCartBuilder.Build(lines, facts, DiscountSpec.None);

        cart.IsValid.ShouldBeTrue();

        Result<SaleReceipt> posted = await CreateHandler().HandleAsync(
            new PostSaleCommand(
                Guid.NewGuid(),
                BusinessDate,
                lines,
                DiscountSpec.None,
                [CashPayment(200m, 200m)]),
            CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoice invoice = await context.SalesInvoices.SingleAsync();

        invoice.Subtotal.ShouldBe(cart.Totals.Subtotal);
        invoice.Discount.ShouldBe(cart.Totals.Discount);
        invoice.Tax.ShouldBe(cart.Totals.Tax);
        invoice.Total.ShouldBe(cart.Totals.Total);

        invoice.Subtotal.Amount.ShouldBe(200m);
        invoice.Discount.Amount.ShouldBe(0m);
        invoice.Tax.Amount.ShouldBe(0m);
        invoice.Total.Amount.ShouldBe(200m);
    }

    [Fact]
    public async Task The_line_keeps_a_snapshot_of_what_it_sold()
    {
        int productId = await CreateProductAsync("SALE-010", 100m, "Sella Tea 250g");
        await PostPurchaseAsync(productId, 10m, 50m);

        Result<SaleReceipt> posted = await CreateHandler().HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 2m, 100m, [CashPayment(200m, 200m)]),
            CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using (StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync())
        {
            // The catalogue changes after the sale; the invoice may not.
            Product product = await context.Products.SingleAsync(candidate => candidate.Id == productId);

            product.Name = "Sella Tea 500g";
            product.SalePrice = new Money(250m);

            await context.SaveChangesAsync();
        }

        await using StoreFlowDbContext contextAfter = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoiceLine line = await contextAfter.SalesInvoiceLines.SingleAsync();

        line.NameSnapshot.ShouldBe("Sella Tea 250g");
        line.SkuSnapshot.ShouldBe("SALE-010");
        line.UnitSnapshot.ShouldBe("Piece");
        line.UnitPrice.ShouldBe(new Money(100m));
        line.QuantityScaled.ShouldBe(2);
    }

    [Fact]
    public async Task The_invoice_number_is_sequential()
    {
        int productId = await CreateProductAsync("SALE-011", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        PostSaleCommandHandler handler = CreateHandler();

        Result<SaleReceipt> first = await handler.HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 2m, 100m, [CashPayment(200m, 200m)]),
            CancellationToken.None);

        Result<SaleReceipt> second = await handler.HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 2m, 100m, [CashPayment(200m, 200m)]),
            CancellationToken.None);

        first.IsFailure.ShouldBeFalse(first.Error.Message);
        second.IsFailure.ShouldBeFalse(second.Error.Message);

        first.Value.Number.ShouldBe("INV-2026-0001");
        second.Value.Number.ShouldBe("INV-2026-0002");
    }

    [Fact]
    public async Task The_tax_comes_from_the_catalogue_and_is_snapshotted()
    {
        int productId = await CreateProductAsync("SALE-012", 100m, taxBasisPoints: 1000);
        await PostPurchaseAsync(productId, 10m, 50m);

        // 200.00 of goods at ten per cent is 20.00 of tax, so the customer owes 220.00.
        Result<SaleReceipt> posted = await CreateHandler().HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 2m, 100m, [CashPayment(220m, 220m)]),
            CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoice invoice = await context.SalesInvoices.SingleAsync();

        invoice.Subtotal.Amount.ShouldBe(200m);
        invoice.Tax.Amount.ShouldBe(20m);
        invoice.Total.Amount.ShouldBe(220m);

        SalesInvoiceLine line = await context.SalesInvoiceLines.SingleAsync();

        line.Tax.ShouldBe(new Money(20m));
        line.TaxRateSnapshotBasisPoints.ShouldBe(1000);
        line.TaxPriceIncludedSnapshot.ShouldBeFalse();
    }

    /// <summary>
    /// The invoice and the payment row have to tell the same story about the change. The till sends
    /// the amount it applied to the invoice and the cash it took, and the change is the difference
    /// between them. When the two disagreed, the printed receipt said there was no change while
    /// the customer was owed forty pounds.
    /// </summary>
    [Fact]
    public async Task The_payment_row_and_the_invoice_say_the_same_thing_about_the_change()
    {
        int productId = await CreateProductAsync("SALE-013", 30m);
        await PostPurchaseAsync(productId, 10m, 15m);

        // The sale is 60.00. The till applies the 60.00 to the invoice and takes 100.00.
        Result<SaleReceipt> posted = await CreateHandler().HandleAsync(
            SaleCommand(Guid.NewGuid(), productId, 2m, 30m, [CashPayment(60m, 100m)]),
            CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoice invoice = await context.SalesInvoices.SingleAsync();

        invoice.Total.ShouldBe(new Money(60m));
        invoice.Paid.ShouldBe(new Money(60m));

        Payment payment = await context.Payments.SingleAsync();

        payment.CashReceived.ShouldBe(new Money(100m));
        payment.Change.ShouldBe(new Money(40m));

        // The invoice and the receipt now agree with the payment row.
        invoice.Change.ShouldBe(payment.Change);
        posted.Value.Totals.Change.ShouldBe(payment.Change);
        posted.Value.Payments[0].Change.ShouldBe(payment.Change);
    }

    [Fact]
    public async Task The_invoice_foots_to_its_lines()
    {
        int productId = await CreateProductAsync("SALE-014", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        // Two pieces at 100.00 is 200.00, and the cashier gives 10.00 off the whole sale.
        Result<SaleReceipt> posted = await CreateHandler().HandleAsync(
            SaleCommand(
                Guid.NewGuid(),
                productId,
                2m,
                100m,
                [CashPayment(190m, 190m)],
                DiscountSpec.FixedAmount(10m)),
            CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoice invoice = await context.SalesInvoices.SingleAsync();
        List<SalesInvoiceLine> lines = await context.SalesInvoiceLines.ToListAsync();

        invoice.Subtotal.ShouldBe(new Money(200m));
        invoice.Discount.ShouldBe(new Money(10m));
        invoice.Total.ShouldBe(new Money(190m));

        // Every discount on the invoice is on one of its lines: the line's own discount and the
        // share of the whole-sale discount that was spread onto it.
        lines.Sum(line => line.Discount.Amount + line.InvoiceDiscountShare.Amount)
            .ShouldBe(invoice.Discount.Amount);

        lines.Sum(line => line.Total.Amount).ShouldBe(invoice.Total.Amount);
    }

    private PostSaleCommandHandler CreateHandler() =>        new(
            _fixture.Factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _session,
            new StoreSettingsReader(_fixture.Factory));

    private static PostSaleCommand SaleCommand(
        Guid operationId,
        int productId,
        decimal quantity,
        decimal unitPrice,
        IReadOnlyList<PaymentInput> payments) =>
        SaleCommand(operationId, productId, quantity, unitPrice, payments, DiscountSpec.None);

    private static PostSaleCommand SaleCommand(
        Guid operationId,
        int productId,
        decimal quantity,
        decimal unitPrice,
        IReadOnlyList<PaymentInput> payments,
        DiscountSpec invoiceDiscount) =>
        new(
            operationId,
            BusinessDate,
            [new SaleLineInput(productId, quantity, new Money(unitPrice), DiscountSpec.None)],
            invoiceDiscount,
            payments);

    private static PaymentInput CashPayment(decimal amount, decimal cashReceived) =>
        new(PaymentMethod.Cash, new Money(amount), new Money(cashReceived), null);

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

    private async Task<int> CreateProductAsync(
        string sku,
        decimal salePrice,
        string? name = null,
        int? taxBasisPoints = null)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Unit unit = await context.Units.FirstAsync(candidate => candidate.Precision == 0);

        int taxCategoryId;

        if (taxBasisPoints is null)
        {
            taxCategoryId = (await context.TaxCategories.FirstAsync()).Id;
        }
        else
        {
            var taxed = new TaxCategory
            {
                Code = $"tax-{taxBasisPoints.Value}",
                Name = "Taxed",
                RateBasisPoints = taxBasisPoints.Value,
                PricesIncludeTax = false,
                EffectiveFromUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            };

            context.TaxCategories.Add(taxed);
            await context.SaveChangesAsync();

            taxCategoryId = taxed.Id;
        }

        var product = new Product
        {
            Sku = sku,
            Name = name ?? sku,
            UnitId = unit.Id,
            TaxCategoryId = taxCategoryId,
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

    /// <summary>
    /// Reads what the till knows about a product, the way the till's own product reader does, so
    /// the expected cart can be built from the same facts.
    /// </summary>
    private async Task<PosProductFacts> ReadProductFactsAsync(int productId)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Product product = await context.Products
            .AsNoTracking()
            .Include(candidate => candidate.Unit)
            .Include(candidate => candidate.TaxCategory)
            .SingleAsync(candidate => candidate.Id == productId);

        long onHand = await context.StockBalances
            .AsNoTracking()
            .Where(balance => balance.ProductId == productId)
            .Select(balance => balance.QuantityOnHandScaled)
            .SingleOrDefaultAsync();

        return new PosProductFacts(
            product.Id,
            product.Sku,
            product.Name,
            product.NameAr ?? string.Empty,
            product.Barcode,
            product.Unit?.Name ?? string.Empty,
            product.Unit?.Precision ?? 0,
            product.SalePrice,
            product.TaxCategory?.RateBasisPoints ?? 0,
            product.TaxCategory?.PricesIncludeTax ?? false,
            product.IsActive,
            onHand);
    }

    /// <summary>Authorises everything, so these tests exercise the till and not the roles.</summary>
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
