using StoreFlow.Application.Common;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Quantity;

namespace StoreFlow.Application.Sales;

/// <summary>
/// The numbers the sales register and the returns are made of, and the rules that are pure
/// functions of them.
/// </summary>
/// <remarks>
/// Everything here is a pure function or a constant. The database is not consulted, so every rule
/// can be tested on its own and the register and the invoice cannot disagree about what a number
/// means.
/// </remarks>
public static class SalesRules
{
    /// <summary>The family the return numbers belong to.</summary>
    public const string ReturnNumberPrefix = "RET";

    /// <summary>The family the reversal numbers belong to.</summary>
    public const string ReversalNumberPrefix = "REV";

    /// <summary>The longest reason a return or a reversal may carry.</summary>
    public const int MaximumReasonLength = 500;

    /// <summary>The longest note a return or a reversal may carry.</summary>
    public const int MaximumNotesLength = 1000;

    /// <summary>Builds a document number, for example <c>RET-2026-0001</c>.</summary>
    /// <param name="prefix">The document family.</param>
    /// <param name="businessDate">The document date, which decides the year segment.</param>
    /// <param name="sequence">The sequence number within that year, starting at one.</param>
    /// <returns>The document number.</returns>
    public static string BuildNumber(string prefix, DateOnly businessDate, int sequence) =>
        DocumentNumber.Build(prefix, businessDate, sequence);

    /// <summary>
    /// The money that goes back for a part of a line.
    /// </summary>
    /// <remarks>
    /// The refund is the returned quantity's share of what the customer paid for the line, so the
    /// discount and the tax come back with it. Returning the whole line refunds the whole line
    /// exactly, with no rounding at all; returning part of it rounds once, to the nearest minor unit,
    /// with the product's rounding policy.
    /// </remarks>
    /// <param name="lineTotal">What the customer paid for the whole line.</param>
    /// <param name="quantityScaled">The quantity coming back, in the persisted scaled form.</param>
    /// <param name="lineQuantityScaled">The quantity sold, in the persisted scaled form.</param>
    /// <returns>The refund for that quantity.</returns>
    public static Money RefundForQuantity(Money lineTotal, long quantityScaled, long lineQuantityScaled)
    {
        if (lineQuantityScaled <= 0 || quantityScaled <= 0)
        {
            return Money.Zero;
        }

        if (quantityScaled >= lineQuantityScaled)
        {
            return lineTotal;
        }

        decimal share = lineTotal.Amount * quantityScaled / lineQuantityScaled;

        return new Money(MoneyMath.Round(share));
    }

    /// <summary>How much of a line may still come back.</summary>
    /// <param name="quantityScaled">The quantity sold, in the persisted scaled form.</param>
    /// <param name="returnedQuantityScaled">The quantity that has already come back.</param>
    /// <returns>The quantity that may still come back.</returns>
    public static long RemainingQuantity(long quantityScaled, long returnedQuantityScaled) =>
        Math.Max(0, quantityScaled - returnedQuantityScaled);

    /// <summary>
    /// The amount as the register shows it: a sale is positive, a return and a reversal are
    /// negative, because in both of those money is going back to the customer.
    /// </summary>
    /// <param name="kind">The kind of document.</param>
    /// <param name="amount">The amount of the document, as a magnitude.</param>
    /// <returns>The signed amount.</returns>
    public static Money SignedAmount(SalesDocumentKind kind, Money amount) =>
        kind == SalesDocumentKind.Invoice ? amount : -amount;

    /// <summary>
    /// True when the quantity may be written in the selling unit of the line it belongs to.
    /// </summary>
    /// <param name="quantity">The quantity in the unit's own terms.</param>
    /// <param name="scale">The precision of the selling unit.</param>
    /// <returns>True when the quantity fits the unit exactly.</returns>
    public static bool FitsScale(decimal quantity, int scale) => QuantityScale.Fits(quantity, scale);

    /// <summary>Converts a quantity in the unit's own terms into its persisted scaled form.</summary>
    /// <param name="quantity">The quantity in the unit's own terms.</param>
    /// <param name="scale">The precision of the selling unit.</param>
    /// <returns>The scaled quantity.</returns>
    public static long Scale(decimal quantity, int scale) =>
        new UnitQuantity(quantity, scale).ToScaledLong();

    /// <summary>Converts a persisted scaled quantity into the unit's own terms.</summary>
    /// <param name="quantityScaled">The persisted scaled quantity.</param>
    /// <param name="scale">The precision of the selling unit.</param>
    /// <returns>The quantity in the unit's own terms.</returns>
    public static decimal Unscale(long quantityScaled, int scale) =>
        UnitQuantity.FromScaledLong(quantityScaled, scale).Value;
}
