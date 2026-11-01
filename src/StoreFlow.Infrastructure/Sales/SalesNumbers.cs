using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Sales;

/// <summary>
/// Builds the next sequential number for the two documents that correct a sale: the return and the
/// reversal.
/// </summary>
/// <remarks>
/// The numbers are read from what has already been issued rather than kept in memory, so a restart
/// can never hand out the same number twice. Each family keeps its own sequence, because a return
/// and a reversal are different documents and a shopkeeper reads their numbers apart.
/// </remarks>
internal static class SalesNumbers
{
    /// <summary>Produces the next free return number, for example <c>RET-2026-0007</c>.</summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="businessDate">The document date, which decides the year segment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The return number.</returns>
    public static async Task<string> NextReturnNumberAsync(
        StoreFlowDbContext context,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string prefix = $"{SalesRules.ReturnNumberPrefix}-{businessDate.Year:D4}-";

        List<string> existing = await context.SalesReturns
            .AsNoTracking()
            .Where(salesReturn => salesReturn.Number.StartsWith(prefix))
            .Select(salesReturn => salesReturn.Number)
            .ToListAsync(cancellationToken);

        return SalesRules.BuildNumber(
            SalesRules.ReturnNumberPrefix,
            businessDate,
            NextSequence(existing, prefix));
    }

    /// <summary>Produces the next free reversal number, for example <c>REV-2026-0002</c>.</summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="businessDate">The document date, which decides the year segment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reversal number.</returns>
    public static async Task<string> NextReversalNumberAsync(
        StoreFlowDbContext context,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string prefix = $"{SalesRules.ReversalNumberPrefix}-{businessDate.Year:D4}-";

        List<string> existing = await context.SalesInvoices
            .AsNoTracking()
            .Where(invoice => invoice.Number.StartsWith(prefix))
            .Select(invoice => invoice.Number)
            .ToListAsync(cancellationToken);

        return SalesRules.BuildNumber(
            SalesRules.ReversalNumberPrefix,
            businessDate,
            NextSequence(existing, prefix));
    }

    /// <summary>
    /// The highest sequence already issued in a family, plus one.
    /// </summary>
    /// <param name="existing">The numbers already issued in that family and year.</param>
    /// <param name="prefix">The family and year prefix those numbers carry.</param>
    /// <returns>The next sequence number.</returns>
    private static int NextSequence(IReadOnlyList<string> existing, string prefix)
    {
        int highest = 0;

        foreach (string number in existing)
        {
            if (int.TryParse(number.AsSpan(prefix.Length), out int sequence) && sequence > highest)
            {
                highest = sequence;
            }
        }

        return highest + 1;
    }
}
