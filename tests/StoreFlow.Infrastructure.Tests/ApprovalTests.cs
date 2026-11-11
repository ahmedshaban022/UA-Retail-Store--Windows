using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Approvals;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Infrastructure.Approvals;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Pos;
using StoreFlow.Infrastructure.Services;
using StoreFlow.Infrastructure.Settings;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The approval of a discount that is over the shop's limit: the sale is held, nothing is written
/// to the ledger, and the sale happens only once a manager has said yes (PRD §3, UAT-13).
/// </summary>
/// <remarks>
/// The cashier may sell but may not approve a discount over the limit; the manager may approve but
/// may not approve their own request. The two are two separate sessions, so the test exercises the
/// real separation and not a flag in one object.
/// </remarks>
public sealed class ApprovalTests : IAsyncLifetime
{
    /// <summary>The day every document in this file is written under.</summary>
    private static readonly DateOnly BusinessDate = new(2026, 4, 1);

    private readonly DatabaseFixture _fixture = new();

    private CashierAuthorization _cashierAuthorization = null!;
    private AllowAllAuthorization _managerAuthorization = null!;
    private EfAuditWriter _auditWriter = null!;
    private TestSession _cashierSession = null!;
    private TestSession _managerSession = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        _cashierAuthorization = new CashierAuthorization();
        _managerAuthorization = new AllowAllAuthorization();
        _cashierSession = new TestSession();
        _managerSession = new TestSession();
        _auditWriter = new EfAuditWriter(_fixture.Factory, new TestClock(), _managerSession);

        _cashierSession.UserId = await CreateUserAsync("cashier", "Shop cashier");
        _managerSession.UserId = await CreateUserAsync("manager", "Shop manager");
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_discount_over_the_limit_is_held_for_a_manager()
    {
        // The shop's limit is ten per cent. Twenty-five per cent is over it, so the sale is held.
        int productId = await CreateProductAsync("APP-001", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        Result<SaleReceipt> posted = await PostSaleAsync(productId, 3m, 100m, DiscountSpec.Percentage(25m));

        posted.IsFailure.ShouldBeTrue();
        posted.Error.Code.ShouldBe(PosErrorCodes.DiscountApprovalRequired);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        // Nothing was written to the ledger: no invoice, and the only movement is the receipt that
        // put the stock on the shelf in the first place.
        int invoices = await context.SalesInvoices.CountAsync();
        int movements = await context.StockMovements.CountAsync();

        invoices.ShouldBe(0);
        movements.ShouldBe(1);
    }

    [Fact]
    public async Task The_request_holds_the_discount_and_the_manager_gives_it()
    {
        // The cashier asks, the manager gives it, and the sale then goes through with the approved
        // discount and nothing more.
        int productId = await CreateProductAsync("APP-002", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        IReadOnlyList<SaleLineInput> lines = [new SaleLineInput(productId, 3m, new Money(100m), DiscountSpec.None)];

        Result<ApprovalRequestSummary> asked = await RequestApprovalAsync(lines, DiscountSpec.Percentage(25m));

        asked.IsFailure.ShouldBeFalse(asked.Error.Message);

        ApprovalRequestSummary request = asked.Value;

        request.Kind.ShouldBe(ApprovalKind.DiscountOverLimit);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.Percent.ShouldBe(25m);
        request.Amount.ShouldBe(new Money(75m));
        request.CartTotal.ShouldBe(new Money(225m));

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        int invoicesWhileWaiting = await context.SalesInvoices.CountAsync();
        invoicesWhileWaiting.ShouldBe(0);

        Result<ApprovalRequestSummary> decided = await DecideAsync(request.Id, approve: true);

        decided.IsFailure.ShouldBeFalse(decided.Error.Message);
        decided.Value.Status.ShouldBe(ApprovalStatus.Approved);
        decided.Value.ApprovedByUserId.ShouldBe(_managerSession.UserId);

        Result<SaleReceipt> posted = await PostSaleAsync(
            productId, 3m, 100m, DiscountSpec.Percentage(25m), request.Id);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        await using StoreFlowDbContext after = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoice invoice = await after.SalesInvoices.SingleAsync();

        invoice.Total.ShouldBe(new Money(225m));
        invoice.Discount.ShouldBe(new Money(75m));
        invoice.Status.ShouldBe(DocumentStatus.Posted);
    }

    [Fact]
    public async Task A_large_stock_adjustment_reaches_the_ledger_when_the_manager_approves_it()
    {
        // A large adjustment is not posted straight away: it is held for a second pair of eyes, and
        // nothing moves until the manager says yes.
        int productId = await CreateProductAsync("RICE-001", 100m);
        await PostPurchaseAsync(productId, 20m, 50m);

        Result<StockAdjustmentOutcome> asked = await new PostStockAdjustmentCommandHandler(
            _fixture.Factory,
            _managerAuthorization,
            _auditWriter,
            new TestClock(),
            _cashierSession)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, 150m, StockDisposition.Sellable, new Money(50m), "Found in the back store"),
                CancellationToken.None);

        asked.IsFailure.ShouldBeFalse(asked.Error.Message);
        asked.Value.Posted.ShouldBeFalse();
        asked.Value.ApprovalRequestId.ShouldNotBeNull();

        await using (StoreFlowDbContext whileWaiting = await _fixture.Factory.CreateDbContextAsync())
        {
            StockBalance balance = await whileWaiting.StockBalances.SingleAsync();

            balance.QuantityOnHandScaled.ShouldBe(20);

            (await whileWaiting.StockMovements.CountAsync()).ShouldBe(1);
        }

        Result<ApprovalRequestSummary> decided = await DecideAsync(asked.Value.ApprovalRequestId!.Value, approve: true);

        decided.IsFailure.ShouldBeFalse(decided.Error.Message);
        decided.Value.Status.ShouldBe(ApprovalStatus.Approved);

        // The manager has to be able to see what they are approving.
        decided.Value.Subject.ShouldContain("RICE-001");
        decided.Value.Subject.ShouldContain("150");

        await using StoreFlowDbContext after = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balanceAfter = await after.StockBalances.SingleAsync();

        // The clerk found a hundred and fifty more on the shelf, and the books now say so.
        // Approving the request without moving the stock would leave the shop selling goods it does
        // not know it has.
        balanceAfter.QuantityOnHandScaled.ShouldBe(170);

        StockMovement movement = await after.StockMovements.SingleAsync(
            candidate => candidate.MovementType == StockMovementType.Adjustment);

        movement.QuantityDeltaScaled.ShouldBe(150);
        movement.Reason.ShouldBe("Found in the back store");
    }

    [Fact]
    public async Task A_rejected_stock_adjustment_leaves_the_stock_where_it_is()
    {
        int productId = await CreateProductAsync("RICE-002", 100m);
        await PostPurchaseAsync(productId, 20m, 50m);

        Result<StockAdjustmentOutcome> asked = await new PostStockAdjustmentCommandHandler(
            _fixture.Factory,
            _managerAuthorization,
            _auditWriter,
            new TestClock(),
            _cashierSession)
            .HandleAsync(
                new PostStockAdjustmentCommand(productId, 150m, StockDisposition.Sellable, new Money(50m), "Found in the back store"),
                CancellationToken.None);

        asked.IsFailure.ShouldBeFalse(asked.Error.Message);

        Result<ApprovalRequestSummary> decided = await DecideAsync(asked.Value.ApprovalRequestId!.Value, approve: false);

        decided.IsFailure.ShouldBeFalse(decided.Error.Message);
        decided.Value.Status.ShouldBe(ApprovalStatus.Rejected);

        await using StoreFlowDbContext after = await _fixture.Factory.CreateDbContextAsync();

        StockBalance balance = await after.StockBalances.SingleAsync();

        balance.QuantityOnHandScaled.ShouldBe(20);

        (await after.StockMovements.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_manager_may_not_approve_their_own_request()
    {
        // The second pair of eyes has to be a second pair of eyes.
        int productId = await CreateProductAsync("APP-003", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        IReadOnlyList<SaleLineInput> lines = [new SaleLineInput(productId, 3m, new Money(100m), DiscountSpec.None)];

        // The same person asks and decides: the manager asks for their own discount and then tries
        // to approve it.
        Result<ApprovalRequestSummary> asked = await RequestApprovalAsync(
            lines, DiscountSpec.Percentage(25m), _managerSession);

        asked.IsFailure.ShouldBeFalse(asked.Error.Message);

        Result<ApprovalRequestSummary> decided = await new DecideApprovalRequestCommandHandler(
            _fixture.Factory,
            _managerAuthorization,
            _auditWriter,
            new TestClock(),
            _managerSession)
            .HandleAsync(
                new DecideApprovalRequestCommand(asked.Value.Id, true),
                CancellationToken.None);

        decided.IsFailure.ShouldBeTrue();
        decided.Error.Code.ShouldBe(ApprovalErrorCodes.SelfApproval);
    }

    [Fact]
    public async Task An_approval_of_another_cart_does_not_apply()
    {
        // The approval belongs to the cart it was asked for. A cart that has changed since has to be
        // asked for again.
        int productId = await CreateProductAsync("APP-004", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        IReadOnlyList<SaleLineInput> lines = [new SaleLineInput(productId, 3m, new Money(100m), DiscountSpec.None)];

        Result<ApprovalRequestSummary> asked = await RequestApprovalAsync(lines, DiscountSpec.Percentage(25m));

        asked.IsFailure.ShouldBeFalse(asked.Error.Message);

        await DecideAsync(asked.Value.Id, approve: true);

        // The cashier changes the cart: two units instead of three.
        Result<SaleReceipt> posted = await PostSaleAsync(
            productId, 2m, 100m, DiscountSpec.Percentage(25m), asked.Value.Id);

        posted.IsFailure.ShouldBeTrue();
        posted.Error.Code.ShouldBe(ApprovalErrorCodes.Mismatch);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        int invoices = await context.SalesInvoices.CountAsync();
        invoices.ShouldBe(0);
    }

    [Fact]
    public async Task A_request_that_is_not_approved_yet_lets_no_sale_through()
    {
        int productId = await CreateProductAsync("APP-005", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        IReadOnlyList<SaleLineInput> lines = [new SaleLineInput(productId, 3m, new Money(100m), DiscountSpec.None)];

        Result<ApprovalRequestSummary> asked = await RequestApprovalAsync(lines, DiscountSpec.Percentage(25m));

        asked.IsFailure.ShouldBeFalse(asked.Error.Message);

        Result<SaleReceipt> posted = await PostSaleAsync(
            productId, 3m, 100m, DiscountSpec.Percentage(25m), asked.Value.Id);

        posted.IsFailure.ShouldBeTrue();
        posted.Error.Code.ShouldBe(ApprovalErrorCodes.NotApproved);
    }

    [Fact]
    public async Task Asking_twice_for_the_same_cart_does_not_ask_the_manager_twice()
    {
        int productId = await CreateProductAsync("APP-006", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        IReadOnlyList<SaleLineInput> lines = [new SaleLineInput(productId, 3m, new Money(100m), DiscountSpec.None)];

        Result<ApprovalRequestSummary> first = await RequestApprovalAsync(lines, DiscountSpec.Percentage(25m));
        Result<ApprovalRequestSummary> second = await RequestApprovalAsync(lines, DiscountSpec.Percentage(25m));

        first.IsFailure.ShouldBeFalse(first.Error.Message);
        second.IsFailure.ShouldBeFalse(second.Error.Message);
        second.Value.Id.ShouldBe(first.Value.Id);
    }

    [Fact]
    public async Task The_manager_sees_the_requests_that_are_waiting_for_a_decision()
    {
        // The manager opens the approvals screen and it must not fall over. Listing the requests is
        // a query the database has to answer, and a query the database cannot answer takes the
        // application down with it — so the list is read against a real database here, not mocked.
        int productId = await CreateProductAsync("APP-007", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        IReadOnlyList<SaleLineInput> lines = [new SaleLineInput(productId, 3m, new Money(100m), DiscountSpec.None)];

        Result<ApprovalRequestSummary> asked = await RequestApprovalAsync(lines, DiscountSpec.Percentage(25m));

        asked.IsFailure.ShouldBeFalse(asked.Error.Message);

        Result<IReadOnlyList<ApprovalRequestSummary>> listed = await ListAsync(ApprovalStatus.Pending);

        listed.IsFailure.ShouldBeFalse(listed.Error.Message);
        listed.Value.Count.ShouldBe(1);
        listed.Value[0].Id.ShouldBe(asked.Value.Id);
        listed.Value[0].RequestedByUserId.ShouldBe(_cashierSession.UserId);

        // The list of the decided requests holds nothing, because the request is still waiting.
        Result<IReadOnlyList<ApprovalRequestSummary>> decided = await ListAsync(ApprovalStatus.Approved);

        decided.IsFailure.ShouldBeFalse(decided.Error.Message);
        decided.Value.Count.ShouldBe(0);
    }

    [Fact]
    public async Task The_requests_are_listed_newest_first()
    {
        // Two requests, and the one that was asked for last is the one that is read first.
        int productId = await CreateProductAsync("APP-008", 100m);
        await PostPurchaseAsync(productId, 10m, 50m);

        IReadOnlyList<SaleLineInput> lines = [new SaleLineInput(productId, 3m, new Money(100m), DiscountSpec.None)];

        Result<ApprovalRequestSummary> first = await RequestApprovalAsync(lines, DiscountSpec.Percentage(25m));

        // A different cart, so that the second request is a second request and not the same one.
        Result<ApprovalRequestSummary> second = await RequestApprovalAsync(
            [new SaleLineInput(productId, 2m, new Money(100m), DiscountSpec.None)], DiscountSpec.Percentage(30m));

        first.IsFailure.ShouldBeFalse(first.Error.Message);
        second.IsFailure.ShouldBeFalse(second.Error.Message);

        Result<IReadOnlyList<ApprovalRequestSummary>> listed = await ListAsync(ApprovalStatus.Pending);

        listed.IsFailure.ShouldBeFalse(listed.Error.Message);
        listed.Value.Count.ShouldBe(2);
        listed.Value[0].Id.ShouldBe(second.Value.Id);
        listed.Value[1].Id.ShouldBe(first.Value.Id);
    }

    /// <summary>Lists the requests, as the manager.</summary>
    /// <param name="status">The status to narrow the list to.</param>
    private async Task<Result<IReadOnlyList<ApprovalRequestSummary>>> ListAsync(ApprovalStatus? status) =>
        await new ListApprovalRequestsQueryHandler(
            _fixture.Factory,
            _managerAuthorization)
            .HandleAsync(new ListApprovalRequestsQuery(status), CancellationToken.None);

    /// <summary>Asks for the discount to be approved.</summary>
    /// <param name="lines">The lines of the cart.</param>
    /// <param name="invoiceDiscount">The discount on the whole sale.</param>
    /// <param name="session">Who is asking.</param>
    private async Task<Result<ApprovalRequestSummary>> RequestApprovalAsync(
        IReadOnlyList<SaleLineInput> lines,
        DiscountSpec invoiceDiscount,
        TestSession? session = null) =>
        await new RequestDiscountApprovalCommandHandler(
            _fixture.Factory,
            _cashierAuthorization,
            _auditWriter,
            new TestClock(),
            session ?? _cashierSession)
            .HandleAsync(
                new RequestDiscountApprovalCommand(lines, invoiceDiscount),
                CancellationToken.None);

    /// <summary>Decides on the request, as the manager.</summary>
    private async Task<Result<ApprovalRequestSummary>> DecideAsync(int requestId, bool approve) =>
        await new DecideApprovalRequestCommandHandler(
            _fixture.Factory,
            _managerAuthorization,
            _auditWriter,
            new TestClock(),
            _managerSession)
            .HandleAsync(
                new DecideApprovalRequestCommand(requestId, approve),
                CancellationToken.None);

    /// <summary>Sells something at the till, as the cashier.</summary>
    private async Task<Result<SaleReceipt>> PostSaleAsync(
        int productId,
        decimal quantity,
        decimal unitPrice,
        DiscountSpec invoiceDiscount,
        int? approvalRequestId = null)
    {
        decimal total = quantity * unitPrice;

        return await new PostSaleCommandHandler(
            _fixture.Factory,
            _cashierAuthorization,
            _auditWriter,
            new TestClock(),
            _cashierSession,
            new StoreSettingsReader(_fixture.Factory),
            new SaleActivityMonitor())
            .HandleAsync(
                new PostSaleCommand(
                    Guid.NewGuid(),
                    BusinessDate,
                    [new SaleLineInput(productId, quantity, new Money(unitPrice), DiscountSpec.None)],
                    invoiceDiscount,
                    [new PaymentInput(PaymentMethod.Cash, new Money(total), new Money(total), null)],
                    null,
                    approvalRequestId),
                CancellationToken.None);
    }

    /// <summary>Creates a user.</summary>
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

    /// <summary>Creates a product.</summary>
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
            _fixture.Factory, _cashierAuthorization, _auditWriter, new TestClock(), _cashierSession)
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
            _fixture.Factory, _cashierAuthorization, _auditWriter, new TestClock(), _cashierSession)
            .HandleAsync(new PostPurchaseCommand(saved.Value), CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);
    }

    /// <summary>
    /// The cashier's rights: everything a cashier may do, and nothing more. The cashier may sell,
    /// but may not approve a discount over the limit.
    /// </summary>
    private sealed class CashierAuthorization : IAuthorizationService
    {
        public bool IsAllowed(Permission permission) => permission != Permission.SalesDiscountOverLimit;

        public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == Permission.SalesDiscountOverLimit
                ? Result.Failure("permission.denied", "You may not approve a discount over the limit.")
                : Result.Success());
    }

    /// <summary>Authorises everything, so the manager's side of the test is about the rules.</summary>
    private sealed class AllowAllAuthorization : IAuthorizationService
    {
        public bool IsAllowed(Permission permission) => true;

        public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>A session with one signed-in user.</summary>
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
