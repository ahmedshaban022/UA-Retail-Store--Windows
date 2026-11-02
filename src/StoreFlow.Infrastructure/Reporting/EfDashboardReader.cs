using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Reporting;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Reporting;

/// <summary>
/// Reads the dashboard's figures.
/// </summary>
/// <remarks>
/// <para>The money on the dashboard is read through the report reader, so the dashboard and the
/// daily sales report cannot tell the owner two different stories about the same day. Two screens
/// that disagree about the day's takings are worse than one screen.</para>
/// </remarks>
public sealed class EfDashboardReader(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IReportReader reportReader) : IDashboardReader
{
    /// <inheritdoc />
    public async Task<DashboardSnapshot> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        ReportFilter filter = new() { From = date, To = date };

        IReadOnlyList<DailySalesRow> daily = await reportReader.ReadDailySalesAsync(filter, cancellationToken);

        DailySalesRow? day = daily.FirstOrDefault(row => row.Date == date);

        ProfitAndLossReport profitAndLoss = await reportReader.ReadProfitAndLossAsync(filter, cancellationToken);

        IReadOnlyList<StockOnHandRow> stock = await reportReader.ReadStockOnHandAsync(
            new ReportFilter { AsOfDate = date, StockStatus = StockStatusFilter.All },
            cancellationToken);

        int lowStockCount = stock.Count(row => row.IsBelowReorder);

        int productCount = await context.Products
            .AsNoTracking()
            .CountAsync(product => product.IsActive, cancellationToken);

        Money stockValue = ReportRules.Sum(stock.Select(row => row.Value));

        return new DashboardSnapshot(
            date,
            day?.NetSales ?? Money.Zero,
            profitAndLoss.AmountOf("gross_profit"),
            day?.Returns ?? Money.Zero,
            day?.InvoiceCount ?? 0,
            lowStockCount,
            productCount,
            stockValue);
    }
}
