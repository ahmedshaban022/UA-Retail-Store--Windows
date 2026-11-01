using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Sales;

/// <summary>
/// Reads one invoice with everything that has happened to it: its lines, the returns against it,
/// and the reversal that may have undone it.
/// </summary>
public sealed class GetSalesInvoiceQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<GetSalesInvoiceQuery, Result<SalesInvoiceDetails>>
{
    /// <inheritdoc />
    public async Task<Result<SalesInvoiceDetails>> HandleAsync(
        GetSalesInvoiceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<SalesInvoiceDetails>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        SalesInvoice? invoice = await context.SalesInvoices
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .Include(candidate => candidate.User)
            .Include(candidate => candidate.ReversesDocument)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.InvoiceId, cancellationToken);

        if (invoice is null)
        {
            return Result<SalesInvoiceDetails>.Failure(
                SalesErrorCodes.InvoiceNotFound,
                "This invoice no longer exists.");
        }

        string? reversedByNumber = await context.SalesInvoices
            .AsNoTracking()
            .Where(candidate => candidate.ReversesDocumentId == invoice.Id)
            .Select(candidate => candidate.Number)
            .FirstOrDefaultAsync(cancellationToken);

        List<SalesReturn> returns = await context.SalesReturns
            .AsNoTracking()
            .Include(candidate => candidate.User)
            .Include(candidate => candidate.Lines)
            .Where(candidate => candidate.OriginalInvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);

        // The order is put in here rather than in the database, because SQLite cannot sort a moment
        // in time.
        List<SalesReturn> ordered = returns
            .OrderBy(candidate => candidate.PostedUtc)
            .ThenBy(candidate => candidate.Id)
            .ToList();

        IReadOnlyList<SalesReturnSummary> summaries = Summarise(invoice, ordered);

        Money returned = new(summaries.Sum(summary => summary.RefundTotal.Amount));

        var lines = invoice.Lines
            .OrderBy(line => line.Id)
            .Select(line => new SalesLineDetails(
                line.Id,
                line.ProductId,
                line.NameSnapshot,
                line.SkuSnapshot,
                line.UnitSnapshot,
                SalesRules.Unscale(line.QuantityScaled, line.QuantityScale),
                line.QuantityScaled,
                line.QuantityScale,
                line.ReturnedQuantityScaled,
                line.UnitPrice,
                line.Discount,
                line.Tax,
                line.Total,
                line.UnitCostSnapshot))
            .ToList();

        var details = new SalesInvoiceDetails(
            invoice.Id,
            invoice.Number,
            invoice.Status,
            invoice.ReversesDocumentId.HasValue ? SalesDocumentKind.Reversal : SalesDocumentKind.Invoice,
            invoice.BusinessDate,
            invoice.PostedUtc,
            invoice.User?.DisplayName ?? string.Empty,
            invoice.PaymentMethod,
            invoice.Subtotal,
            invoice.Discount,
            invoice.Tax,
            invoice.Total,
            invoice.Paid,
            invoice.Change,
            returned,
            lines,
            summaries,
            invoice.ReversesDocument?.Number,
            reversedByNumber,
            invoice.Notes);

        return Result<SalesInvoiceDetails>.Success(details);
    }

    /// <summary>
    /// Turns the returns against an invoice into the summaries the detail screen shows, and works
    /// out which of them gave back the whole invoice.
    /// </summary>
    /// <param name="invoice">The invoice being read.</param>
    /// <param name="returns">The returns against it, oldest first.</param>
    /// <returns>The summaries.</returns>
    private static IReadOnlyList<SalesReturnSummary> Summarise(
        SalesInvoice invoice,
        IReadOnlyList<SalesReturn> returns)
    {
        Dictionary<int, long> returned = invoice.Lines.ToDictionary(line => line.Id, _ => 0L);

        List<SalesReturnSummary> summaries = [];

        foreach (SalesReturn salesReturn in returns)
        {
            foreach (SalesReturnLine line in salesReturn.Lines)
            {
                returned[line.SalesInvoiceLineId] = returned.GetValueOrDefault(line.SalesInvoiceLineId) + line.QuantityScaled;
            }

            bool isFullReturn = invoice.Lines.Count > 0
                && invoice.Lines.All(line => returned.GetValueOrDefault(line.Id) >= line.QuantityScaled);

            summaries.Add(new SalesReturnSummary(
                salesReturn.Id,
                salesReturn.Number,
                salesReturn.BusinessDate,
                salesReturn.PostedUtc,
                salesReturn.User?.DisplayName ?? string.Empty,
                salesReturn.RefundTotal,
                salesReturn.RefundMethod,
                salesReturn.Reason,
                isFullReturn));
        }

        return summaries;
    }
}
