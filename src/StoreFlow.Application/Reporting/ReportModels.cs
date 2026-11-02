namespace StoreFlow.Application.Reporting;

using StoreFlow.Domain.Money;

/// <summary>The reports the application can produce (PRD §5.8).</summary>
public enum ReportKind
{
    /// <summary>REP-01 — the day's takings, day by day.</summary>
    DailySales = 0,

    /// <summary>REP-02 — what sold, and what it left behind.</summary>
    SalesByProduct = 1,

    /// <summary>REP-03 — the products that moved most.</summary>
    BestSellers = 2,

    /// <summary>REP-04 — what is on the shelf.</summary>
    StockOnHand = 3,

    /// <summary>REP-05 — the products that have reached their reorder level.</summary>
    LowOrOutOfStock = 4,

    /// <summary>What the shelf is worth, on the day the report is dated.</summary>
    StockValuation = 5,

    /// <summary>REP-06 — what came in and what went out.</summary>
    StockMovements = 6,

    /// <summary>REP-07 — what was bought, and from whom.</summary>
    PurchasesBySupplier = 7,

    /// <summary>REP-08 — what the shop earned.</summary>
    ProfitAndLoss = 8,

    /// <summary>REP-09 — what came back.</summary>
    Returns = 9,

    /// <summary>What each cashier took.</summary>
    CashierPerformance = 10,

    /// <summary>REP-11 — what each cashier took, and what should be in the drawer.</summary>
    CashierShift = 11,

    /// <summary>The audit trail, read as a report.</summary>
    AuditTrail = 12,
}

/// <summary>Which products a stock report shows.</summary>
public enum StockStatusFilter
{
    /// <summary>Every product that holds stock.</summary>
    All = 0,

    /// <summary>Only the products at or below their reorder level.</summary>
    BelowReorder = 1,

    /// <summary>Only the products with nothing on the shelf.</summary>
    OutOfStock = 2,
}

/// <summary>How a report is narrowed down.</summary>
public sealed record ReportFilter
{
    /// <summary>The first day of the period.</summary>
    public DateOnly From { get; init; } = DateOnly.MinValue;

    /// <summary>The last day of the period.</summary>
    public DateOnly To { get; init; } = DateOnly.MaxValue;

    /// <summary>The cashier to narrow the report to, when the report has cashiers.</summary>
    public int? CashierUserId { get; init; }

    /// <summary>The category to narrow the report to, when the report has categories.</summary>
    public int? CategoryId { get; init; }

    /// <summary>The product to narrow the report to, when the report has products.</summary>
    public int? ProductId { get; init; }

    /// <summary>The supplier to narrow the report to, when the report has suppliers.</summary>
    public int? SupplierId { get; init; }

    /// <summary>How many rows a ranked report keeps.</summary>
    public int TopCount { get; init; } = 10;

    /// <summary>True when a ranked report ranks by money rather than by units.</summary>
    public bool RankByRevenue { get; init; }

    /// <summary>Which products a stock report shows.</summary>
    public StockStatusFilter StockStatus { get; init; } = StockStatusFilter.All;

    /// <summary>
    /// The day a stock report is dated. The ledger is replayed to that day, so a report dated
    /// before a receipt leaves that receipt out.
    /// </summary>
    public DateOnly? AsOfDate { get; init; }

    /// <summary>Free text to narrow the report by, when the report has names.</summary>
    public string? Search { get; init; }
}

/// <summary>REP-01 — one day of takings.</summary>
/// <param name="Date">The day.</param>
/// <param name="InvoiceCount">How many sales were posted on that day.</param>
/// <param name="GrossSales">What the goods were worth before any discount, without the tax.</param>
/// <param name="Discounts">Every discount given that day.</param>
/// <param name="Tax">The tax collected that day.</param>
/// <param name="Returns">What came back that day, without the tax.</param>
/// <param name="NetSales">What the day actually took, without the tax.</param>
public sealed record DailySalesRow(
    DateOnly Date,
    int InvoiceCount,
    Money GrossSales,
    Money Discounts,
    Money Tax,
    Money Returns,
    Money NetSales);

/// <summary>REP-02 — what one product sold.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">Its stock-keeping unit code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameAr">Its Arabic name, when it has one.</param>
/// <param name="Quantity">How many units went out.</param>
/// <param name="NetRevenue">What the sales brought in, without the tax.</param>
/// <param name="CostOfGoodsSold">What those units cost the shop.</param>
/// <param name="GrossProfit">What was left after the cost of the goods.</param>
public sealed record SalesByProductRow(
    int ProductId,
    string Sku,
    string Name,
    string? NameAr,
    decimal Quantity,
    Money NetRevenue,
    Money CostOfGoodsSold,
    Money GrossProfit);

/// <summary>REP-03 — one product of the ranked list.</summary>
/// <param name="Rank">The place in the list.</param>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">Its stock-keeping unit code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameAr">Its Arabic name, when it has one.</param>
/// <param name="Quantity">How many units went out.</param>
/// <param name="NetRevenue">What the sales brought in, without the tax.</param>
public sealed record BestSellerRow(
    int Rank,
    int ProductId,
    string Sku,
    string Name,
    string? NameAr,
    decimal Quantity,
    Money NetRevenue);

/// <summary>REP-04 — one product on the shelf.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">Its stock-keeping unit code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameAr">Its Arabic name, when it has one.</param>
/// <param name="Category">The category it sits in.</param>
/// <param name="Quantity">How many units are on the shelf.</param>
/// <param name="ReorderLevel">How few units may be left before it is reordered.</param>
/// <param name="AverageUnitCost">The moving average cost of one unit.</param>
/// <param name="Value">What the units on the shelf are worth.</param>
/// <param name="IsBelowReorder">True when the product has reached its reorder level.</param>
public sealed record StockOnHandRow(
    int ProductId,
    string Sku,
    string Name,
    string? NameAr,
    string Category,
    decimal Quantity,
    decimal ReorderLevel,
    Money AverageUnitCost,
    Money Value,
    bool IsBelowReorder);

/// <summary>One product's worth on the shelf, at the average cost of that day.</summary>
/// <remarks>
/// The PRD does not number this report separately: REP-04 asks for the stock value beside the
/// quantity, and this is the same reading with the average cost spelled out.
/// </remarks>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">Its stock-keeping unit code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Quantity">How many units are on the shelf.</param>
/// <param name="AverageUnitCost">The moving average cost of one unit on that day.</param>
/// <param name="Value">What the units on the shelf are worth.</param>
public sealed record StockValuationRow(
    int ProductId,
    string Sku,
    string Name,
    decimal Quantity,
    Money AverageUnitCost,
    Money Value);

/// <summary>REP-06 — one movement of the stock ledger.</summary>
/// <param name="Date">The day of the movement.</param>
/// <param name="PostedUtc">The moment the movement was written.</param>
/// <param name="Sku">The product's stock-keeping unit code.</param>
/// <param name="Name">The product's name.</param>
/// <param name="Movement">What kind of movement it was.</param>
/// <param name="Disposition">Whether it affects the sellable quantity or the damaged bucket.</param>
/// <param name="Quantity">How many units moved; negative when stock went out.</param>
/// <param name="UnitCost">The cost of one unit on that movement.</param>
/// <param name="Balance">What was left on the shelf afterwards.</param>
/// <param name="Reason">Why it happened, when there was a reason.</param>
public sealed record StockMovementRow(
    DateOnly Date,
    DateTimeOffset PostedUtc,
    string Sku,
    string Name,
    string Movement,
    string Disposition,
    decimal Quantity,
    Money UnitCost,
    decimal Balance,
    string? Reason);

/// <summary>REP-07 — what was bought from one supplier.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="Supplier">The supplier's name.</param>
/// <param name="DocumentCount">How many posted documents there were.</param>
/// <param name="Subtotal">What the goods were worth before the discount.</param>
/// <param name="Discount">Every discount the supplier gave.</param>
/// <param name="Tax">The tax paid.</param>
/// <param name="Total">What was paid.</param>
public sealed record PurchaseBySupplierRow(
    int SupplierId,
    string Supplier,
    int DocumentCount,
    Money Subtotal,
    Money Discount,
    Money Tax,
    Money Total);

/// <summary>REP-08 — one line of the profit and loss statement.</summary>
/// <param name="Key">
/// A stable key for the line, so the interface can name it in the shop's own language without the
/// report layer knowing anything about languages.
/// </param>
/// <param name="Amount">The figure on the line.</param>
/// <param name="IsEmphasis">True for the lines that carry the meaning, such as the gross profit.</param>
public sealed record ProfitAndLossLine(string Key, Money Amount, bool IsEmphasis = false);

/// <summary>REP-08 — the profit and loss statement for a period.</summary>
/// <param name="From">The first day of the period.</param>
/// <param name="To">The last day of the period.</param>
/// <param name="Lines">The lines of the statement, in the order they are shown.</param>
public sealed record ProfitAndLossReport(DateOnly From, DateOnly To, IReadOnlyList<ProfitAndLossLine> Lines)
{
    /// <summary>Finds the amount on a line by its key.</summary>
    public Money AmountOf(string key)
    {
        foreach (ProfitAndLossLine line in Lines)
        {
            if (line.Key == key)
            {
                return line.Amount;
            }
        }

        return Money.Zero;
    }
}

/// <summary>What one cashier took over a period.</summary>
/// <remarks>
/// The PRD does not number this report; it is the per-cashier reading of REP-01, and the cashier
/// filter of REP-01 is what narrows it to one person.
/// </remarks>
/// <param name="UserId">The cashier.</param>
/// <param name="DisplayName">The cashier's name.</param>
/// <param name="InvoiceCount">How many sales they posted.</param>
/// <param name="NetSales">What they took, without the tax.</param>
/// <param name="AverageInvoice">What their sales came to on average, without the tax.</param>
public sealed record CashierPerformanceRow(
    int UserId,
    string DisplayName,
    int InvoiceCount,
    Money NetSales,
    Money AverageInvoice);

/// <summary>REP-09 — what came back, product by product.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">Its stock-keeping unit code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Count">How many times the product came back.</param>
/// <param name="Quantity">How many units came back.</param>
/// <param name="Amount">What was refunded, without the tax.</param>
/// <param name="Reasons">The reasons that were written down.</param>
public sealed record ReturnsRow(
    int ProductId,
    string Sku,
    string Name,
    int Count,
    decimal Quantity,
    Money Amount,
    string Reasons);

/// <summary>REP-11 — one cashier's day of cash.</summary>
/// <param name="Date">The day.</param>
/// <param name="UserId">The cashier.</param>
/// <param name="Cashier">The cashier's name.</param>
/// <param name="CashOpening">What the drawer was counted to hold when the day began.</param>
/// <param name="CashTaken">The cash the sales brought in.</param>
/// <param name="CashRefunded">The cash the returns gave back.</param>
/// <param name="CashClosing">What should be in the drawer at the end of the day.</param>
/// <param name="CountedCash">What the drawer was counted to hold at the end of the day, when it has been counted.</param>
/// <param name="Variance">The difference between the two, when the drawer has been counted.</param>
public sealed record CashierShiftRow(
    DateOnly Date,
    int UserId,
    string Cashier,
    Money CashOpening,
    Money CashTaken,
    Money CashRefunded,
    Money CashClosing,
    Money? CountedCash,
    Money? Variance);

/// <summary>Reads the reports.</summary>
public interface IReportReader
{
    /// <summary>REP-01 — the takings, day by day.</summary>
    Task<IReadOnlyList<DailySalesRow>> ReadDailySalesAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>REP-02 — what sold, and what it left behind.</summary>
    Task<IReadOnlyList<SalesByProductRow>> ReadSalesByProductAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>REP-03 — the products that moved most.</summary>
    Task<IReadOnlyList<BestSellerRow>> ReadBestSellersAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>REP-04 — what is on the shelf.</summary>
    Task<IReadOnlyList<StockOnHandRow>> ReadStockOnHandAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>What the shelf is worth, on the day the report is dated.</summary>
    /// <remarks>
    /// The PRD does not number this report separately: REP-04 asks for the stock value beside the
    /// quantity, and this is the same reading with the average cost spelled out.
    /// </remarks>
    Task<IReadOnlyList<StockValuationRow>> ReadStockValuationAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>REP-06 — what came in and what went out.</summary>
    Task<IReadOnlyList<StockMovementRow>> ReadStockMovementsAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>REP-07 — what was bought, and from whom.</summary>
    Task<IReadOnlyList<PurchaseBySupplierRow>> ReadPurchasesBySupplierAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>REP-08 — what the shop earned.</summary>
    Task<ProfitAndLossReport> ReadProfitAndLossAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>REP-09 — what each cashier took.</summary>
    Task<IReadOnlyList<CashierPerformanceRow>> ReadCashierPerformanceAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>REP-09 — what came back, product by product.</summary>
    Task<IReadOnlyList<ReturnsRow>> ReadReturnsAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>REP-11 — what each cashier took, and what should be in the drawer.</summary>
    Task<IReadOnlyList<CashierShiftRow>> ReadCashierShiftsAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default);
}
