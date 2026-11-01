using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Sales;

/// <summary>
/// Reads one return: the document, the lines that came back and the money that went back with them.
/// </summary>
public sealed class GetSalesReturnQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<GetSalesReturnQuery, Result<SalesReturnDetails>>
{
    /// <inheritdoc />
    public async Task<Result<SalesReturnDetails>> HandleAsync(
        GetSalesReturnQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<SalesReturnDetails>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        SalesReturn? salesReturn = await context.SalesReturns
            .AsNoTracking()
            .Include(candidate => candidate.User)
            .Include(candidate => candidate.Lines)
            .ThenInclude(line => line.SalesInvoiceLine)
            .Include(candidate => candidate.OriginalInvoice)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.ReturnId, cancellationToken);

        if (salesReturn is null)
        {
            return Result<SalesReturnDetails>.Failure(
                SalesErrorCodes.InvoiceNotFound,
                "This return no longer exists.");
        }

        var lines = salesReturn.Lines
            .OrderBy(line => line.Id)
            .Select(line => new ReturnLineSummary(
                line.SalesInvoiceLineId,
                line.SalesInvoiceLine?.NameSnapshot ?? string.Empty,
                SalesRules.Unscale(line.QuantityScaled, line.QuantityScale),
                line.IsResellable,
                line.RefundAmount))
            .ToList();

        var details = new SalesReturnDetails(
            salesReturn.Id,
            salesReturn.Number,
            salesReturn.BusinessDate,
            salesReturn.PostedUtc,
            salesReturn.User?.DisplayName ?? string.Empty,
            salesReturn.OriginalInvoiceId,
            salesReturn.OriginalInvoice?.Number ?? string.Empty,
            salesReturn.Reason,
            salesReturn.RefundTotal,
            salesReturn.RefundTax,
            salesReturn.RefundMethod,
            lines,
            salesReturn.Notes);

        return Result<SalesReturnDetails>.Success(details);
    }
}
