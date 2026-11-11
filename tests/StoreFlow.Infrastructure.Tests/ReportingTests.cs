using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Pos;
using StoreFlow.Application.Reporting;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Pos;
using StoreFlow.Infrastructure.Reporting;
using StoreFlow.Infrastructure.Sales;
using StoreFlow.Infrastructure.Services;
using StoreFlow.Infrastructure.Settings;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The reports against a real SQLite database: the real documents, the real stock ledger and the
/// real money.
/// </summary>
/// <remarks>
/// <para>Every figure in this file is worked out by hand and written down next to the assertion, so
/// that a change in the reports shows up as a change in a number a person can check. The shop in
/// this file is the one in the product's worked example: ten units at fifty, three sold at a
/// hundred, one of them coming back.</para>
/// <para>Every test works on its own day and its own product, so that the tests in this file never
/// read each other's figures.</para>
/// </remarks>
public sealed class ReportingTests : IAsyncLifetime
{
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

        _session.UserId = await CreateUserAsync("reporter", "Report cashier");
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task The_daily_sales_summary_reports_the_day_as_it_was_worked_out_by_hand()
    {
        // The day: three units of a product bought at fifty and sold at a hundred.
        //
        //   goods at the till   3 x 100.00 = 300.00
        //   discount                    0.00
        //   tax                          0.00
        //   what the day took          300.00
        DateOnly day = new(2026, 3, 1);

        int productId = await CreateProductAsync("REP-001", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostSaleAsync(productId, 3m, 100m, day);

        IReadOnlyList<DailySalesRow> rows = await Reader().ReadDailySalesAsync(
            new ReportFilter { From = day, To = day }, CancellationToken.None);

        DailySalesRow row = rows.Single(candidate => candidate.Date == day);

        row.InvoiceCount.ShouldBe(1);
        row.GrossSales.ShouldBe(new Money(300m));
        row.Discounts.ShouldBe(new Money(0m));
        row.Tax.ShouldBe(new Money(0m));
        row.Returns.ShouldBe(new Money(0m));
        row.NetSales.ShouldBe(new Money(300m));
    }

    [Fact]
    public async Task A_return_takes_the_money_off_the_day_it_happened()
    {
        // Three units sold on the first of March for three hundred. One of them comes back on the
        // second of March and is refunded a hundred.
        //
        //   the first of March   sales 300.00   returns   0.00   net 300.00
        //   the second of March  sales   0.00   returns 100.00   net -100.00
        DateOnly sold = new(2026, 3, 2);
        DateOnly returned = new(2026, 3, 3);

        int productId = await CreateProductAsync("REP-002", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, sold);

        int invoiceId = await PostSaleAsync(productId, 3m, 100m, sold);

        Result<RefundReceipt> refund = await PostReturnAsync(
            invoiceId,
            [new ReturnLineInput(await LineId(invoiceId), 1m, true)],
            returned);

        refund.IsFailure.ShouldBeFalse(refund.Error.Message);
        refund.Value.RefundTotal.ShouldBe(new Money(100m));

        IReadOnlyList<DailySalesRow> rows = await Reader().ReadDailySalesAsync(
            new ReportFilter { From = sold, To = returned });

        DailySalesRow dayOfSale = rows.Single(candidate => candidate.Date == sold);
        dayOfSale.NetSales.ShouldBe(new Money(300m));
        dayOfSale.Returns.ShouldBe(new Money(0m));

        DailySalesRow dayOfReturn = rows.Single(candidate => candidate.Date == returned);
        dayOfReturn.InvoiceCount.ShouldBe(0);
        dayOfReturn.Returns.ShouldBe(new Money(100m));
        dayOfReturn.NetSales.ShouldBe(new Money(-100m));
    }

    [Fact]
    public async Task The_sales_by_product_report_carries_the_cost_of_the_goods()
    {
        // Three units bought at fifty and sold at a hundred.
        //
        //   revenue     3 x 100.00 = 300.00
        //   cost        3 x  50.00 = 150.00
        //   profit                   150.00
        DateOnly day = new(2026, 3, 4);

        int productId = await CreateProductAsync("REP-003", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostSaleAsync(productId, 3m, 100m, day);

        IReadOnlyList<SalesByProductRow> rows = await Reader().ReadSalesByProductAsync(
            new ReportFilter { From = day, To = day }, CancellationToken.None);

        SalesByProductRow row = rows.Single(candidate => candidate.ProductId == productId);

        row.Quantity.ShouldBe(3m);
        row.NetRevenue.ShouldBe(new Money(300m));
        row.CostOfGoodsSold.ShouldBe(new Money(150m));
        row.GrossProfit.ShouldBe(new Money(150m));
    }

    [Fact]
    public async Task A_return_takes_the_profit_down_by_the_refund_less_the_cost()
    {
        // Three units sold at a hundred, one of them coming back.
        //
        //   the refund                100.00
        //   the unit it gave back      50.00
        //   the profit falls by         50.00
        //
        // so what is left is
        //
        //   quantity   2
        //   revenue  200.00
        //   cost     100.00
        //   profit   100.00
        DateOnly day = new(2026, 3, 5);

        int productId = await CreateProductAsync("REP-004", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);

        int invoiceId = await PostSaleAsync(productId, 3m, 100m, day);

        await PostReturnAsync(
            invoiceId,
            [new ReturnLineInput(await LineId(invoiceId), 1m, true)],
            day);

        IReadOnlyList<SalesByProductRow> rows = await Reader().ReadSalesByProductAsync(
            new ReportFilter { From = day, To = day }, CancellationToken.None);

        SalesByProductRow row = rows.Single(candidate => candidate.ProductId == productId);

        row.Quantity.ShouldBe(2m);
        row.NetRevenue.ShouldBe(new Money(200m));
        row.CostOfGoodsSold.ShouldBe(new Money(100m));
        row.GrossProfit.ShouldBe(new Money(100m));
    }

    [Fact]
    public async Task The_stock_on_hand_report_is_worth_the_moving_average()
    {
        // Ten units at fifty, then ten more at seventy.
        //
        //   after the first receipt   10 units   average 50.00   worth  500.00
        //   after the second receipt  20 units   average 60.00   worth 1200.00
        //
        // The average is (10 x 50 + 10 x 70) / 20 = 60.00, and the report dated before the second
        // receipt has to leave that receipt out.
        DateOnly firstReceipt = new(2026, 3, 6);
        DateOnly secondReceipt = new(2026, 3, 7);

        int productId = await CreateProductAsync("REP-005", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, firstReceipt);
        await PostPurchaseAsync(productId, 10m, 70m, secondReceipt);

        IReadOnlyList<StockOnHandRow> before = await Reader().ReadStockOnHandAsync(
            new ReportFilter { AsOfDate = firstReceipt, ProductId = productId });

        StockOnHandRow first = before.Single(candidate => candidate.ProductId == productId);

        first.Quantity.ShouldBe(10m);
        first.AverageUnitCost.ShouldBe(new Money(50m));
        first.Value.ShouldBe(new Money(500m));

        IReadOnlyList<StockOnHandRow> after = await Reader().ReadStockOnHandAsync(
            new ReportFilter { AsOfDate = secondReceipt, ProductId = productId });

        StockOnHandRow second = after.Single(candidate => candidate.ProductId == productId);

        second.Quantity.ShouldBe(20m);
        second.AverageUnitCost.ShouldBe(new Money(60m));
        second.Value.ShouldBe(new Money(1200m));
    }

    [Fact]
    public async Task The_valuation_report_worths_the_shelf_the_same_way()
    {
        // Ten units at fifty and ten at seventy: the shelf is worth twelve hundred.
        DateOnly day = new(2026, 3, 8);

        int productId = await CreateProductAsync("REP-006", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostPurchaseAsync(productId, 10m, 70m, day);

        IReadOnlyList<StockValuationRow> rows = await Reader().ReadStockValuationAsync(
            new ReportFilter { AsOfDate = day, ProductId = productId });

        StockValuationRow row = rows.Single(candidate => candidate.ProductId == productId);

        row.Quantity.ShouldBe(20m);
        row.AverageUnitCost.ShouldBe(new Money(60m));
        row.Value.ShouldBe(new Money(1200m));
    }

    [Fact]
    public async Task The_stock_movement_report_shows_what_came_in()
    {
        // One receipt of ten units at fifty.
        DateOnly day = new(2026, 3, 9);

        int productId = await CreateProductAsync("REP-007", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);

        StockMovementsReport report = await Reader().ReadStockMovementsAsync(
            new ReportFilter { From = day, To = day, ProductId = productId });

        StockMovementRow row = report.Rows.Single(candidate => candidate.Sku == "REP-007");

        row.Date.ShouldBe(day);
        row.Quantity.ShouldBe(10m);
        row.UnitCost.ShouldBe(new Money(50m));
        row.Balance.ShouldBe(10m);
    }

    [Fact]
    public async Task A_report_narrowed_to_one_category_leaves_the_other_categories_out()
    {
        // An invoice can hold lines from more than one category. A report about one category
        // must not carry the money of another one, and the returns are narrowed the same way,
        // so that the two halves of the report agree with each other.
        DateOnly day = new(2026, 3, 21);

        int drinks = await CreateCategoryAsync("REP Drinks");
        int news = await CreateCategoryAsync("REP News");

        int cola = await CreateProductAsync("REP-COLA", 100m, drinks);
        int paper = await CreateProductAsync("REP-PAPER", 100m, news);

        await PostPurchaseAsync(cola, 10m, 50m, day);
        await PostPurchaseAsync(paper, 10m, 50m, day);

        // One invoice, two lines, two categories.
        await PostSaleAsync(
            [new SaleLineInput(cola, 2m, new Money(100m), DiscountSpec.None),
             new SaleLineInput(paper, 1m, new Money(100m), DiscountSpec.None)],
            day);

        IReadOnlyList<SalesByProductRow> rows = await Reader().ReadSalesByProductAsync(
            new ReportFilter { From = day, To = day, CategoryId = drinks }, CancellationToken.None);

        SalesByProductRow row = rows.Single();

        row.ProductId.ShouldBe(cola);
        row.NetRevenue.ShouldBe(new Money(200m));
    }

    [Fact]
    public async Task The_stock_movement_report_says_how_many_movements_there_were()
    {
        // The ledger is the one report that can have more rows than a screen can show. A shop
        // that is shown a part of it has to be told how big the whole of it is.
        DateOnly day = new(2026, 3, 22);

        int productId = await CreateProductAsync("REP-022", 100m);

        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostPurchaseAsync(productId, 5m, 50m, day);
        await PostPurchaseAsync(productId, 5m, 50m, day);

        StockMovementsReport report = await Reader().ReadStockMovementsAsync(
            new ReportFilter { From = day, To = day, ProductId = productId, RowLimit = 2 });

        report.Rows.Count.ShouldBe(2);
        report.TotalCount.ShouldBe(3);
        report.IsTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task The_stock_movement_report_leaves_nothing_out_when_no_limit_is_asked_for()
    {
        DateOnly day = new(2026, 3, 23);

        int productId = await CreateProductAsync("REP-023", 100m);

        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostPurchaseAsync(productId, 5m, 50m, day);

        StockMovementsReport report = await Reader().ReadStockMovementsAsync(
            new ReportFilter { From = day, To = day, ProductId = productId });

        report.Rows.Count.ShouldBe(2);
        report.TotalCount.ShouldBe(2);
        report.IsTruncated.ShouldBeFalse();
    }

    [Fact]
    public async Task The_purchase_report_groups_what_was_bought_by_supplier()
    {
        // One receipt of ten units at fifty from one supplier.
        //
        //   subtotal  10 x 50.00 = 500.00
        //   discount             0.00
        //   tax                  0.00
        //   total              500.00
        DateOnly day = new(2026, 3, 10);

        int supplierId = await CreateSupplierAsync("REP Supplier");
        int productId = await CreateProductAsync("REP-008", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day, supplierId);

        IReadOnlyList<PurchaseBySupplierRow> rows = await Reader().ReadPurchasesBySupplierAsync(
            new ReportFilter { From = day, To = day, SupplierId = supplierId });

        PurchaseBySupplierRow row = rows.Single(candidate => candidate.SupplierId == supplierId);

        row.DocumentCount.ShouldBe(1);
        row.Subtotal.ShouldBe(new Money(500m));
        row.Discount.ShouldBe(new Money(0m));
        row.Tax.ShouldBe(new Money(0m));
        row.Total.ShouldBe(new Money(500m));
    }

    [Fact]
    public async Task The_profit_and_loss_report_adds_up()
    {
        // Three units bought at fifty and sold at a hundred.
        //
        //   net sales             300.00
        //   cost of goods sold    150.00
        //   gross profit          150.00
        DateOnly day = new(2026, 3, 11);

        int productId = await CreateProductAsync("REP-009", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostSaleAsync(productId, 3m, 100m, day);

        ProfitAndLossReport report = await Reader().ReadProfitAndLossAsync(
            new ReportFilter { From = day, To = day }, CancellationToken.None);

        report.AmountOf("net_sales").ShouldBe(new Money(300m));
        report.AmountOf("cost_of_goods_sold").ShouldBe(new Money(150m));
        report.AmountOf("gross_profit").ShouldBe(new Money(150m));
        report.AmountOf("tax").ShouldBe(new Money(0m));
        report.AmountOf("discounts").ShouldBe(new Money(0m));
    }

    [Fact]
    public async Task The_cashier_report_says_what_each_cashier_took()
    {
        // One sale of three units at a hundred.
        //
        //   invoices        1
        //   net sales   300.00
        //   average    300.00
        DateOnly day = new(2026, 3, 12);

        int productId = await CreateProductAsync("REP-010", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostSaleAsync(productId, 3m, 100m, day);

        IReadOnlyList<CashierPerformanceRow> rows = await Reader().ReadCashierPerformanceAsync(
            new ReportFilter { From = day, To = day }, CancellationToken.None);

        CashierPerformanceRow row = rows.Single(candidate => candidate.UserId == _session.UserId);

        row.InvoiceCount.ShouldBe(1);
        row.NetSales.ShouldBe(new Money(300m));
        row.AverageInvoice.ShouldBe(new Money(300m));
    }

    [Fact]
    public async Task The_best_sellers_report_ranks_by_units()
    {
        // Ten units of one product and two of another: the first one is the better seller.
        DateOnly day = new(2026, 3, 13);

        int popular = await CreateProductAsync("REP-011", 100m);
        int quiet = await CreateProductAsync("REP-012", 100m);

        await PostPurchaseAsync(popular, 20m, 50m, day);
        await PostPurchaseAsync(quiet, 20m, 50m, day);

        await PostSaleAsync(popular, 10m, 100m, day);
        await PostSaleAsync(quiet, 2m, 100m, day);

        IReadOnlyList<BestSellerRow> rows = await Reader().ReadBestSellersAsync(
            new ReportFilter { From = day, To = day, TopCount = 5 });

        BestSellerRow first = rows[0];

        first.Rank.ShouldBe(1);
        first.ProductId.ShouldBe(popular);
        first.Quantity.ShouldBe(10m);
        first.NetRevenue.ShouldBe(new Money(1000m));

        rows.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task The_dashboard_and_the_daily_sales_report_say_the_same_thing()
    {
        // Two screens that disagree about the day's takings are worse than one screen, so the
        // dashboard reads the same figures the report reads.
        DateOnly day = new(2026, 3, 14);

        int productId = await CreateProductAsync("REP-013", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostSaleAsync(productId, 3m, 100m, day);

        IReadOnlyList<DailySalesRow> rows = await Reader().ReadDailySalesAsync(
            new ReportFilter { From = day, To = day }, CancellationToken.None);

        DailySalesRow row = rows.Single(candidate => candidate.Date == day);

        DashboardSnapshot snapshot = await new EfDashboardReader(_fixture.Factory, Reader())
            .GetAsync(day);

        snapshot.NetSales.ShouldBe(row.NetSales);
        snapshot.InvoiceCount.ShouldBe(row.InvoiceCount);
        snapshot.Returns.ShouldBe(row.Returns);
        snapshot.GrossProfit.ShouldBe(new Money(150m));
    }

    [Fact]
    public async Task The_returns_report_says_what_came_back()
    {
        // Three units sold at a hundred and one of them comes back: one return, one unit, a
        // hundred refunded.
        DateOnly day = new(2026, 3, 15);

        int productId = await CreateProductAsync("REP-014", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);

        int invoiceId = await PostSaleAsync(productId, 3m, 100m, day);

        await PostReturnAsync(
            invoiceId,
            [new ReturnLineInput(await LineId(invoiceId), 1m, true)],
            day);

        IReadOnlyList<ReturnsRow> rows = await Reader().ReadReturnsAsync(
            new ReportFilter { From = day, To = day }, CancellationToken.None);

        ReturnsRow row = rows.Single(candidate => candidate.ProductId == productId);

        row.Count.ShouldBe(1);
        row.Quantity.ShouldBe(1m);
        row.Amount.ShouldBe(new Money(100m));
        row.Reasons.ShouldContain("Changed mind");
    }

    [Fact]
    public async Task The_cashier_reconciliation_says_what_should_be_in_the_drawer()
    {
        // Three units sold at a hundred, paid in cash: the drawer took three hundred. The drawer
        // was counted to hold fifty when the day began, and to hold three hundred and fifty at the
        // end of it, so the day adds up and the variance is nothing.
        DateOnly day = new(2026, 3, 16);

        int productId = await CreateProductAsync("REP-015", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostSaleAsync(productId, 3m, 100m, day);

        await new RecordDrawerCashCommandHandler(
            _fixture.Factory,
            _authorization,
            new TestClock(),
            _auditWriter,
            _session)
            .HandleAsync(
                new RecordDrawerCashCommand(day, _session.UserId, new Money(50m), new Money(350m)),
                CancellationToken.None);

        IReadOnlyList<CashierShiftRow> rows = await Reader().ReadCashierShiftsAsync(
            new ReportFilter { From = day, To = day }, CancellationToken.None);

        CashierShiftRow row = rows.Single(candidate => candidate.Date == day);

        row.CashOpening.ShouldBe(new Money(50m));
        row.CashTaken.ShouldBe(new Money(300m));
        row.CashRefunded.ShouldBe(new Money(0m));
        row.CashClosing.ShouldBe(new Money(350m));
        row.CountedCash.ShouldBe(new Money(350m));
        row.Variance.ShouldBe(new Money(0m));
    }

    /// <summary>The report reader under test.</summary>
    private EfReportReader Reader() => new(_fixture.Factory, new TestClock());
    /// <summary>Creates a user, so that the audit trail and the cashier report name an actor.</summary>
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

    /// <summary>Creates a supplier.</summary>
    private async Task<int> CreateSupplierAsync(string name)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        var supplier = new Supplier
        {
            Name = name,
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        context.Suppliers.Add(supplier);

        await context.SaveChangesAsync();

        return supplier.Id;
    }

    [Fact]
    public async Task A_price_that_changes_after_the_sale_does_not_reach_the_report()
    {
        // Three units bought at fifty and sold at a hundred, and then the shop changes the price.
        //
        //   the sale as it was made   revenue 300.00   cost 150.00   profit 150.00
        //   the price becomes 500.00
        //   the report still reads    revenue 300.00   cost 150.00   profit 150.00
        //
        // The report reads what the invoice was written down at, not what the catalogue says
        // today. A price that changes tomorrow must not rewrite yesterday's margin (PRD §6.1).
        DateOnly day = new(2026, 3, 20);

        int productId = await CreateProductAsync("REP-016", 100m);
        await PostPurchaseAsync(productId, 10m, 50m, day);
        await PostSaleAsync(productId, 3m, 100m, day);

        await ChangeThePriceAsync(productId, 500m);

        IReadOnlyList<SalesByProductRow> rows = await Reader().ReadSalesByProductAsync(
            new ReportFilter { From = day, To = day }, CancellationToken.None);

        SalesByProductRow row = rows.Single(candidate => candidate.ProductId == productId);

        row.Quantity.ShouldBe(3m);
        row.NetRevenue.ShouldBe(new Money(300m));
        row.CostOfGoodsSold.ShouldBe(new Money(150m));
        row.GrossProfit.ShouldBe(new Money(150m));
    }

    /// <summary>Changes the price of a product in the catalogue, the way the shop would.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="salePrice">The price it is sold at from now on.</param>
    private async Task ChangeThePriceAsync(int productId, decimal salePrice)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Product product = await context.Products.SingleAsync(candidate => candidate.Id == productId);

        product.SalePrice = new Money(salePrice);
        product.UpdatedUtc = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        await context.SaveChangesAsync();
    }

    /// <summary>Creates a product that sells for a hundred and cost fifty.</summary>
    private async Task<int> CreateProductAsync(string sku, decimal salePrice, int? categoryId = null)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Unit unit = await context.Units.FirstAsync(candidate => candidate.Precision == 0);
        TaxCategory taxCategory = await context.TaxCategories.FirstAsync();

        var product = new Product
        {
            Sku = sku,
            Name = sku,
            UnitId = unit.Id,
            CategoryId = categoryId,
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

    /// <summary>Creates a category for the products to be put in.</summary>
    private async Task<int> CreateCategoryAsync(string name)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        var category = new Category
        {
            Name = name,
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        context.Categories.Add(category);

        await context.SaveChangesAsync();

        return category.Id;
    }

    /// <summary>Puts stock on the shelf the way the shop does it: a posted purchase.</summary>
    private async Task PostPurchaseAsync(
        int productId,
        decimal quantity,
        decimal unitCost,
        DateOnly businessDate,
        int? supplierId = null)
    {
        Result<int> saved = await new SavePurchaseCommandHandler(
            _fixture.Factory, _authorization, _auditWriter, new TestClock(), _session)
            .HandleAsync(
                new SavePurchaseCommand(
                    null,
                    supplierId,
                    businessDate,
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

    /// <summary>Sells something at the till.</summary>
    private async Task<int> PostSaleAsync(int productId, decimal quantity, decimal unitPrice, DateOnly businessDate) =>
        await PostSaleAsync(
            [new SaleLineInput(productId, quantity, new Money(unitPrice), DiscountSpec.None)],
            businessDate);

    /// <summary>Sells things at the till, on one invoice.</summary>
    private async Task<int> PostSaleAsync(IReadOnlyList<SaleLineInput> lines, DateOnly businessDate)
    {
        decimal total = lines.Sum(line => line.Quantity * line.UnitPrice.Amount);

        Result<SaleReceipt> posted = await new PostSaleCommandHandler(
            _fixture.Factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _session,
            new StoreSettingsReader(_fixture.Factory),
            new SaleActivityMonitor())
            .HandleAsync(
                new PostSaleCommand(
                    Guid.NewGuid(),
                    businessDate,
                    lines,
                    DiscountSpec.None,
                    [new PaymentInput(PaymentMethod.Cash, new Money(total), new Money(total), null)]),
                CancellationToken.None);

        posted.IsFailure.ShouldBeFalse(posted.Error.Message);

        return posted.Value.InvoiceId;
    }

    /// <summary>Gives something back.</summary>
    private async Task<Result<RefundReceipt>> PostReturnAsync(
        int invoiceId,
        IReadOnlyList<ReturnLineInput> lines,
        DateOnly businessDate) =>
        await new PostReturnCommandHandler(
            _fixture.Factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _session)
            .HandleAsync(
                new PostReturnCommand(invoiceId, businessDate, lines, PaymentMethod.Cash, "Changed mind"),
                CancellationToken.None);

    /// <summary>Finds the line of an invoice, so that it can be given back.</summary>
    private async Task<int> LineId(int invoiceId)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        SalesInvoiceLine line = await context.SalesInvoiceLines
            .SingleAsync(candidate => candidate.SalesInvoiceId == invoiceId);

        return line.Id;
    }

    /// <summary>Authorises everything, so these tests exercise the reports and not the roles.</summary>
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
