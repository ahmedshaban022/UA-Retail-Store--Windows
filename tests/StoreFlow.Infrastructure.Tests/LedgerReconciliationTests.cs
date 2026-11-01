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
/// The ledger reconciliation the PRD asks for: a thousand mixed stock operations, and then a check
/// that the cached balances and the signed movements say exactly the same thing.
/// </summary>
/// <remarks>
/// <para>The test drives the real handlers — receipts, sales, returns, counts and adjustments —
/// against a real database, so every movement goes through the same ledger the shop uses. Nothing
/// is written to the balances directly.</para>
/// <para>After the thousand operations, the balance of every product is compared with the sum of
/// its signed movements, sellable and damaged separately. The expected discrepancy is zero.</para>
/// </remarks>
public sealed class LedgerReconciliationTests : IAsyncLifetime
{
    /// <summary>The document date every operation in this file is written under.</summary>
    private static readonly DateOnly BusinessDate = new(2026, 1, 1);

    /// <summary>How many products the run spreads itself over.</summary>
    private const int ProductCount = 4;

    /// <summary>How many operations the run performs at least.</summary>
    private const int MinimumOperations = 1_000;

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

        _session.UserId = await CreateUserAsync("clerk", "Shop clerk");
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_thousand_mixed_operations_leave_the_ledger_and_the_balances_in_step()
    {
        List<int> productIds = new();

        for (int index = 0; index < ProductCount; index++)
        {
            productIds.Add(await CreateProductAsync($"LEDGER-{index:D3}", 100m));
        }

        int operations = 0;

        // The run walks the four kinds of movement the shop makes, over and over, so the ledger ends
        // up holding receipts, sales, resellable returns, damaged returns and adjustments.
        while (operations < MinimumOperations)
        {
            foreach (int productId in productIds)
            {
                await PostPurchaseAsync(productId, 10m, 50m);
                operations++;

                int invoiceId = await PostSaleAsync(productId, 2m, 100m);
                operations++;

                int lineId = await LineIdOf(invoiceId);

                await PostReturnAsync(invoiceId, lineId, 1m, resellable: true);
                operations++;

                await PostReturnAsync(invoiceId, lineId, 1m, resellable: false);
                operations++;

                await PostAdjustmentAsync(productId, -1m);
                operations++;
            }
        }

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        List<StockBalance> balances = await context.StockBalances.AsNoTracking().ToListAsync();

        // The run has to have been big enough to be worth calling a reconciliation.
        (await context.StockMovements.CountAsync()).ShouldBeGreaterThanOrEqualTo(MinimumOperations);
        balances.Count.ShouldBe(ProductCount);

        List<MovementSum> movementSums = await context.StockMovements
            .AsNoTracking()
            .GroupBy(movement => new { movement.ProductId, movement.Disposition })
            .Select(group => new MovementSum
            {
                ProductId = group.Key.ProductId,
                Disposition = group.Key.Disposition,
                Quantity = group.Sum(movement => movement.QuantityDeltaScaled),
            })
            .ToListAsync();

        foreach (StockBalance balance in balances)
        {
            long sellable = movementSums
                .Where(sum => sum.ProductId == balance.ProductId && sum.Disposition == StockDisposition.Sellable)
                .Sum(sum => sum.Quantity);

            long damaged = movementSums
                .Where(sum => sum.ProductId == balance.ProductId && sum.Disposition == StockDisposition.Damaged)
                .Sum(sum => sum.Quantity);

            // The cached balance and the ledger must say the same thing, to the unit.
            balance.QuantityOnHandScaled.ShouldBe(sellable);
            balance.DamagedQuantityScaled.ShouldBe(damaged);
        }

        // And the last movement of every product carries the balance it left behind, so the ledger
        // can be read on its own.
        List<StockMovement> lastMovements = await context.StockMovements
            .AsNoTracking()
            .Where(movement => movement.Disposition == StockDisposition.Sellable)
            .GroupBy(movement => movement.ProductId)
            .Select(group => group.OrderByDescending(movement => movement.Id).First())
            .ToListAsync();

        foreach (StockMovement movement in lastMovements)
        {
            StockBalance balance = balances.Single(candidate => candidate.ProductId == movement.ProductId);

            movement.RunningBalanceScaled.ShouldBe(balance.QuantityOnHandScaled);
        }
    }

    private async Task PostReturnAsync(int invoiceId, int lineId, decimal quantity, bool resellable)
    {
        Result<RefundReceipt> posted = await new PostReturnCommandHandler(
            _fixture.Factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _session)
            .HandleAsync(
                new PostReturnCommand(
                    invoiceId,
                    BusinessDate,
                    [new ReturnLineInput(lineId, quantity, resellable)],
                    PaymentMethod.Cash,
                    "Reconciliation run"),
                CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);
    }

    private async Task PostAdjustmentAsync(int productId, decimal quantityDelta)
    {
        Result<StockAdjustmentOutcome> posted = await new PostStockAdjustmentCommandHandler(
            _fixture.Factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _session)
            .HandleAsync(
                new PostStockAdjustmentCommand(
                    productId,
                    quantityDelta,
                    StockDisposition.Sellable,
                    new Money(50m),
                    "Reconciliation run"),
                CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);
    }

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
                    [new PaymentInput(
                        PaymentMethod.Cash,
                        new Money(quantity * unitPrice),
                        new Money(quantity * unitPrice),
                        null)]),
                CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        return posted.Value.InvoiceId;
    }

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

    private async Task<int> LineIdOf(int invoiceId)
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

    /// <summary>The sum of the movements of one product and one bucket.</summary>
    private sealed class MovementSum
    {
        public int ProductId { get; set; }

        public StockDisposition Disposition { get; set; }

        public long Quantity { get; set; }
    }

    /// <summary>Authorises everything, so these tests exercise the ledger and not the roles.</summary>
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
