using StoreFlow.Domain.Money;

namespace StoreFlow.Application.Reporting;

/// <summary>
/// The headline figures on the dashboard. It reads the posted ledger only, so it can never show a
/// number that a return or a reversal has already taken back.
/// </summary>
/// <param name="Date">The day the figures belong to.</param>
/// <param name="NetSales">What the day took, without the tax.</param>
/// <param name="GrossProfit">What was left after the cost of the goods.</param>
/// <param name="Returns">What came back that day, without the tax.</param>
/// <param name="InvoiceCount">How many invoices were posted that day.</param>
/// <param name="LowStockCount">How many active products are at or below their reorder level.</param>
/// <param name="ProductCount">How many active products exist.</param>
/// <param name="StockValue">What the shelf is worth.</param>
public sealed record DashboardSnapshot(
    DateOnly Date,
    Money NetSales,
    Money GrossProfit,
    Money Returns,
    int InvoiceCount,
    int LowStockCount,
    int ProductCount,
    Money StockValue);

/// <summary>Reads the dashboard figures.</summary>
public interface IDashboardReader
{
    /// <summary>Reads the figures for a day.</summary>
    /// <param name="date">The day to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<DashboardSnapshot> GetAsync(DateOnly date, CancellationToken cancellationToken = default);
}
