using StoreFlow.Application.Reporting;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Reporting;

/// <summary>
/// Reads the dashboard figures straight from the posted ledger. The queries are deliberately simple
/// and index-backed, because the dashboard is the first screen the owner sees every morning.
/// </summary>
public sealed class EfDashboardReader(IDbContextFactory<StoreFlowDbContext> contextFactory) : IDashboardReader
{
    /// <inheritdoc />
    public async Task<DashboardSnapshot> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);

        List<SalesInvoice> invoices = await context.SalesInvoices
            .AsNoTracking()
            .Where(invoice => invoice.Status == Domain.Enumerations.DocumentStatus.Posted &&
                              invoice.BusinessDate == today)
            .Select(invoice => new SalesInvoice
            {
                Id = invoice.Id,
                Subtotal = invoice.Subtotal,
                Discount = invoice.Discount,
                Tax = invoice.Tax,
                Total = invoice.Total,
            })
            .ToListAsync(cancellationToken);

        long netSalesMinor = invoices.Sum(invoice => invoice.Subtotal.ToMinorUnits() -
                                                    invoice.Discount.ToMinorUnits());

        int productCount = await context.Products
            .AsNoTracking()
            .CountAsync(product => product.IsActive, cancellationToken);

        int lowStockCount = await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .Join(
                context.StockBalances.AsNoTracking(),
                product => product.Id,
                balance => balance.ProductId,
                (product, balance) => new { product.ReorderLevelScaled, balance.QuantityOnHandScaled })
            .Where(row => row.QuantityOnHandScaled <= row.ReorderLevelScaled)
            .CountAsync(cancellationToken);

        return new DashboardSnapshot(
            Domain.Money.Money.FromMinorUnits(netSalesMinor),
            invoices.Count,
            lowStockCount,
            productCount);
    }
}
