using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Sales;

/// <summary>
/// Lists the people who have posted a sale, so the register can be filtered by the cashier who was
/// on the till.
/// </summary>
public sealed class ListSalesCashiersQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListSalesCashiersQuery, Result<IReadOnlyList<SalesCashierOption>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SalesCashierOption>>> HandleAsync(
        ListSalesCashiersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<SalesCashierOption>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<CashierRow> rows = await context.SalesInvoices
            .AsNoTracking()
            .Where(invoice => invoice.UserId != 0)
            .Select(invoice => new CashierRow
            {
                Id = invoice.UserId,
                DisplayName = invoice.User!.DisplayName,
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        List<SalesCashierOption> cashiers = rows
            .Select(row => new SalesCashierOption(row.Id, row.DisplayName))
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCulture)
            .ToList();

        return Result<IReadOnlyList<SalesCashierOption>>.Success(cashiers);
    }

    /// <summary>The cashier as it is read from the database.</summary>
    private sealed class CashierRow
    {
        public int Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;
    }
}
