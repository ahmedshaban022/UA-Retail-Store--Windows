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
/// Reads the sales register: the documents it lists, the detail of one invoice, and the cashiers it
/// can be filtered by.
/// </summary>
/// <remarks>
/// The register is a history, not a report. It shows every document that touched the money of a
/// sale — the sale, the returns against it, and the reversal that undid it — so the reader can see
/// what happened rather than only what is left.
/// </remarks>
public sealed class ListSalesRegisterQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListSalesRegisterQuery, Result<IReadOnlyList<SalesRegisterRow>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SalesRegisterRow>>> HandleAsync(
        ListSalesRegisterQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<SalesRegisterRow>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        string search = (query.Filter.Search ?? string.Empty).Trim();

        List<InvoiceRow> invoices = await context.SalesInvoices
            .AsNoTracking()
            .Where(invoice => query.Filter.From == null || invoice.BusinessDate >= query.Filter.From)
            .Where(invoice => query.Filter.To == null || invoice.BusinessDate <= query.Filter.To)
            .Where(invoice => query.Filter.CashierId == null || invoice.UserId == query.Filter.CashierId)
            .Where(invoice => search.Length == 0
                || invoice.Number.Contains(search)
                || invoice.User!.DisplayName.Contains(search))
            .Select(invoice => new InvoiceRow
            {
                Id = invoice.Id,
                Number = invoice.Number,
                Status = invoice.Status,
                BusinessDate = invoice.BusinessDate,
                PostedUtc = invoice.PostedUtc,
                Total = invoice.Total,
                CashierName = invoice.User!.DisplayName,
                LineCount = invoice.Lines.Count,
                ReversesDocumentId = invoice.ReversesDocumentId,
                ReversesNumber = invoice.ReversesDocument!.Number,
            })
            .ToListAsync(cancellationToken);

        List<ReturnRow> returns = await context.SalesReturns
            .AsNoTracking()
            .Where(salesReturn => query.Filter.From == null || salesReturn.BusinessDate >= query.Filter.From)
            .Where(salesReturn => query.Filter.To == null || salesReturn.BusinessDate <= query.Filter.To)
            .Where(salesReturn => query.Filter.CashierId == null || salesReturn.UserId == query.Filter.CashierId)
            .Where(salesReturn => search.Length == 0
                || salesReturn.Number.Contains(search)
                || salesReturn.User!.DisplayName.Contains(search)
                || salesReturn.OriginalInvoice!.Number.Contains(search))
            .Select(salesReturn => new ReturnRow
            {
                Id = salesReturn.Id,
                Number = salesReturn.Number,
                BusinessDate = salesReturn.BusinessDate,
                PostedUtc = salesReturn.PostedUtc,
                RefundTotal = salesReturn.RefundTotal,
                CashierName = salesReturn.User!.DisplayName,
                LineCount = salesReturn.Lines.Count,
                OriginalInvoiceId = salesReturn.OriginalInvoiceId,
                OriginalInvoiceNumber = salesReturn.OriginalInvoice!.Number,
            })
            .ToListAsync(cancellationToken);

        // How much of each invoice has already come back, so the reader can see at a glance that a
        // sale has been partly given back.
        int[] invoiceIds = invoices.Select(invoice => invoice.Id).ToArray();

        Dictionary<int, Money> refunded = [];

        if (invoiceIds.Length > 0)
        {
            List<RefundedRow> refundedRows = await context.SalesReturns
                .AsNoTracking()
                .Where(salesReturn => invoiceIds.Contains(salesReturn.OriginalInvoiceId))
                .Select(salesReturn => new RefundedRow
                {
                    OriginalInvoiceId = salesReturn.OriginalInvoiceId,
                    RefundTotal = salesReturn.RefundTotal,
                })
                .ToListAsync(cancellationToken);

            refunded = refundedRows
                .GroupBy(row => row.OriginalInvoiceId)
                .ToDictionary(
                    group => group.Key,
                    group => new Money(group.Sum(row => row.RefundTotal.Amount)));
        }

        List<SalesRegisterRow> rows = [];

        foreach (InvoiceRow invoice in invoices)
        {
            SalesDocumentKind kind = invoice.ReversesDocumentId.HasValue
                ? SalesDocumentKind.Reversal
                : SalesDocumentKind.Invoice;

            rows.Add(new SalesRegisterRow(
                invoice.Id,
                invoice.Number,
                kind,
                invoice.Status,
                invoice.BusinessDate,
                invoice.PostedUtc,
                invoice.CashierName,
                SalesRules.SignedAmount(kind, invoice.Total),
                kind == SalesDocumentKind.Invoice
                    && refunded.TryGetValue(invoice.Id, out Money amount)
                        ? amount
                        : Money.Zero,
                invoice.LineCount,
                invoice.ReversesDocumentId ?? 0,
                invoice.ReversesNumber));
        }

        foreach (ReturnRow salesReturn in returns)
        {
            rows.Add(new SalesRegisterRow(
                salesReturn.Id,
                salesReturn.Number,
                SalesDocumentKind.Return,
                DocumentStatus.Posted,
                salesReturn.BusinessDate,
                salesReturn.PostedUtc,
                salesReturn.CashierName,
                SalesRules.SignedAmount(SalesDocumentKind.Return, salesReturn.RefundTotal),
                Money.Zero,
                salesReturn.LineCount,
                salesReturn.OriginalInvoiceId,
                salesReturn.OriginalInvoiceNumber));
        }

        List<SalesRegisterRow> ordered = rows
            .OrderByDescending(row => row.BusinessDate)
            .ThenByDescending(row => row.PostedUtc)
            .ToList();

        return Result<IReadOnlyList<SalesRegisterRow>>.Success(ordered);
    }

    /// <summary>The invoice row as it is read from the database.</summary>
    private sealed class InvoiceRow
    {
        public int Id { get; set; }

        public string Number { get; set; } = string.Empty;

        public DocumentStatus Status { get; set; }

        public DateOnly BusinessDate { get; set; }

        public DateTimeOffset PostedUtc { get; set; }

        public Money Total { get; set; }

        public string CashierName { get; set; } = string.Empty;

        public int LineCount { get; set; }

        public int? ReversesDocumentId { get; set; }

        public string? ReversesNumber { get; set; }
    }

    /// <summary>The return row as it is read from the database.</summary>
    private sealed class ReturnRow
    {
        public int Id { get; set; }

        public string Number { get; set; } = string.Empty;

        public DateOnly BusinessDate { get; set; }

        public DateTimeOffset PostedUtc { get; set; }

        public Money RefundTotal { get; set; }

        public string CashierName { get; set; } = string.Empty;

        public int LineCount { get; set; }

        public int OriginalInvoiceId { get; set; }

        public string OriginalInvoiceNumber { get; set; } = string.Empty;
    }

    /// <summary>The refunded amount of one invoice, as it is read from the database.</summary>
    private sealed class RefundedRow
    {
        public int OriginalInvoiceId { get; set; }

        public Money RefundTotal { get; set; }
    }
}
