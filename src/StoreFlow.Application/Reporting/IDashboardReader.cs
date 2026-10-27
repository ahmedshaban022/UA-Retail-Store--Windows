using StoreFlow.Domain.Money;

namespace StoreFlow.Application.Reporting;

/// <summary>
/// The headline figures on the dashboard. It reads the posted ledger only, so it can never show a
/// number that a return or a reversal has already taken back.
/// </summary>
/// <param name="TodaySales">Net sales posted today, excluding tax.</param>
/// <param name="TodayInvoiceCount">How many invoices were posted today.</param>
/// <param name="LowStockCount">How many active products are at or below their reorder level.</param>
/// <param name="ProductCount">How many active products exist.</param>
public sealed record DashboardSnapshot(
    Money TodaySales,
    int TodayInvoiceCount,
    int LowStockCount,
    int ProductCount);

/// <summary>Reads the dashboard figures.</summary>
public interface IDashboardReader
{
    /// <summary>Reads the figures for today.</summary>
    Task<DashboardSnapshot> GetTodayAsync(CancellationToken cancellationToken = default);
}
