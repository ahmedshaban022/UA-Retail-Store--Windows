namespace StoreFlow.Application.Common;

/// <summary>
/// Builds the sequential numbers the documents of the shop are numbered in.
/// </summary>
/// <remarks>
/// Every family of documents — an invoice, a return, a reversal — numbers itself the same way: a
/// prefix that says what the document is, the year it belongs to, and a sequence that never repeats
/// inside that year. One builder for all of them keeps the shape of a number the same across the
/// whole application.
/// </remarks>
public static class DocumentNumber
{
    /// <summary>Builds a document number, for example <c>INV-2026-0001</c>.</summary>
    /// <param name="prefix">The document family, for example <c>INV</c>.</param>
    /// <param name="businessDate">The document date, which decides the year segment.</param>
    /// <param name="sequence">The sequence number within that year, starting at one.</param>
    /// <returns>The document number.</returns>
    public static string Build(string prefix, DateOnly businessDate, int sequence) =>
        $"{prefix}-{businessDate.Year:D4}-{sequence:D4}";
}
