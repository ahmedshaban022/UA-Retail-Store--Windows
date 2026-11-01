using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Pos;

/// <summary>
/// Builds the next sequential invoice number for a year.
/// </summary>
/// <remarks>
/// The number is unique inside the database by a unique index, so a clash is refused rather than
/// silently reused, and the sequence is read from what has already been issued rather than kept in
/// memory — a counter in memory would hand out the same number twice after a restart.
/// </remarks>
internal static class InvoiceNumber
{
    /// <summary>Produces the next free number, for example <c>INV-2026-0007</c>.</summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="businessDate">The document date, which decides the year segment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document number.</returns>
    public static async Task<string> NextAsync(
        StoreFlowDbContext context,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string prefix = $"{PosRules.InvoiceNumberPrefix}-{businessDate.Year:D4}-";

        List<string> existing = await context.SalesInvoices
            .AsNoTracking()
            .Where(invoice => invoice.Number.StartsWith(prefix))
            .Select(invoice => invoice.Number)
            .ToListAsync(cancellationToken);

        int highest = 0;

        foreach (string number in existing)
        {
            if (int.TryParse(number.AsSpan(prefix.Length), out int sequence) && sequence > highest)
            {
                highest = sequence;
            }
        }

        return PosRules.BuildNumber(PosRules.InvoiceNumberPrefix, businessDate, highest + 1);
    }
}
