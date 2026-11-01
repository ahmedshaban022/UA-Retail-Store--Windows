using StoreFlow.Application.Reporting;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Reporting;

/// <summary>
/// Reads the dashboard figures straight from the posted ledger. The queries are deliberately simple
/// and index-backed, because the dashboard is the first screen the owner sees every morning.
/// </summary>
/// <remarks>
/// The figure is the day's takings net of what came back, so a return or a reversal takes its
/// money off the day it happened rather than leaving the dashboard to brag about a sale that was
/// given back.
/// </remarks>
public sealed class EfDashboardReader(IDbContextFactory<StoreFlowDbContext> contextFactory) : IDashboardReader
{
    /// <inheritdoc />
    public async Task<DashboardSnapshot> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);

        // The sales of the day. A reversal is a document of its own, so it is not one of them.
        List<SalesInvoice> sales = await context.SalesInvoices
            .AsNoTracking()
            .Where(invoice => invoice.BusinessDate == today && invoice.ReversesDocumentId == null)
            .Select(invoice => new SalesInvoice
            {
                Id = invoice.Id,
                Subtotal = invoice.Subtotal,
                Discount = invoice.Discount,
                Tax = invoice.Tax,
                Total = invoice.Total,
            })
            .ToListAsync(cancellationToken);

        long salesMinor = sales.Sum(invoice => invoice.Subtotal.ToMinorUnits() -
                                              invoice.Discount.ToMinorUnits());

        // A reversal takes its money off the day it happened, but only when it undoes a sale of
        // that same day; a reversal of an older sale belongs to the day the money went back.
        List<ReversalRow> reversals = await context.SalesInvoices
            .AsNoTracking()
            .Where(invoice => invoice.ReversesDocumentId != null &&
                              invoice.BusinessDate == today &&
                              invoice.ReversesDocument!.BusinessDate == today)
            .Select(invoice => new ReversalRow
            {
                Subtotal = invoice.Subtotal,
                Discount = invoice.Discount,
            })
            .ToListAsync(cancellationToken);

        long reversedMinor = reversals.Sum(reversal => reversal.Subtotal.ToMinorUnits() -
                                                       reversal.Discount.ToMinorUnits());

        // A return takes its money off in the same way.
        List<ReturnRow> returns = await context.SalesReturns
            .AsNoTracking()
            .Where(salesReturn => salesReturn.BusinessDate == today &&
                                  salesReturn.OriginalInvoice!.BusinessDate == today)
            .Select(salesReturn => new ReturnRow
            {
                RefundTotal = salesReturn.RefundTotal,
                RefundTax = salesReturn.RefundTax,
            })
            .ToListAsync(cancellationToken);

        long returnedMinor = returns.Sum(salesReturn => salesReturn.RefundTotal.ToMinorUnits() -
                                                         salesReturn.RefundTax.ToMinorUnits());

        long netSalesMinor = salesMinor - reversedMinor - returnedMinor;

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
            sales.Count,
            lowStockCount,
            productCount);
    }

    /// <summary>A reversal as it is read from the database.</summary>
    private sealed class ReversalRow
    {
        public Domain.Money.Money Subtotal { get; set; }

        public Domain.Money.Money Discount { get; set; }
    }

    /// <summary>A return as it is read from the database.</summary>
    private sealed class ReturnRow
    {
        public Domain.Money.Money RefundTotal { get; set; }

        public Domain.Money.Money RefundTax { get; set; }
    }
}
