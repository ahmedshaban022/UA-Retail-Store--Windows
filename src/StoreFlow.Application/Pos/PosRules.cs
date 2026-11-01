using StoreFlow.Domain.Money;
using StoreFlow.Domain.Quantity;

namespace StoreFlow.Application.Pos;

/// <summary>
/// The numbers the policies of the till are made of, and the rules that are pure functions of them.
/// </summary>
/// <remarks>
/// Everything here is a pure function or a constant. The database is not consulted, so every rule
/// can be tested on its own and the interface and the invoice cannot disagree about what a number
/// means.
/// </remarks>
public static class PosRules
{
    /// <summary>The family the invoice numbers belong to.</summary>
    public const string InvoiceNumberPrefix = "INV";

    /// <summary>The family the suspended carts are numbered in.</summary>
    public const string HeldCartNumberPrefix = "HLD";

    /// <summary>The longest name a suspended cart may be given.</summary>
    public const int MaximumHeldCartLabelLength = 40;

    /// <summary>
    /// Builds a sequential invoice number, for example <c>INV-2026-0001</c>.
    /// </summary>
    /// <param name="prefix">The document family, for example <c>INV</c>.</param>
    /// <param name="businessDate">The document date, which decides the year segment.</param>
    /// <param name="sequence">The sequence number within that year, starting at one.</param>
    /// <returns>The document number.</returns>
    public static string BuildNumber(string prefix, DateOnly businessDate, int sequence) =>
        Common.DocumentNumber.Build(prefix, businessDate, sequence);

    /// <summary>
    /// The tax on a line, given the amount the customer pays for it.
    /// </summary>
    /// <remarks>
    /// A price that already carries the tax has the tax taken out of it; a price that does not has
    /// the tax added on top. The two policies are the ones the product supports (PRD §6.2), and the
    /// difference is snapshotted on the invoice line so a later change of the store's tax settings
    /// cannot alter a posted invoice.
    /// </remarks>
    /// <param name="amount">The amount the customer pays for the line, after the discounts.</param>
    /// <param name="rateBasisPoints">The tax rate in basis points.</param>
    /// <param name="priceIncludesTax">True when the selling price already carries the tax.</param>
    /// <returns>The tax.</returns>
    public static Money TaxOn(Money amount, int rateBasisPoints, bool priceIncludesTax)
    {
        decimal tax = priceIncludesTax
            ? Domain.Tax.TaxCalculator.OnInclusiveAmount(amount.Amount, rateBasisPoints)
            : Domain.Tax.TaxCalculator.OnExclusiveAmount(amount.Amount, rateBasisPoints);

        return new Money(tax);
    }

    /// <summary>
    /// The amount the tax is computed on, given the amount the customer pays for the line.
    /// </summary>
    /// <param name="amount">The amount the customer pays for the line, after the discounts.</param>
    /// <param name="rateBasisPoints">The tax rate in basis points.</param>
    /// <param name="priceIncludesTax">True when the selling price already carries the tax.</param>
    /// <returns>The taxable base.</returns>
    public static Money TaxableBaseOn(Money amount, int rateBasisPoints, bool priceIncludesTax)
    {
        decimal baseAmount = Domain.Tax.TaxCalculator.TaxableBase(
            amount.Amount, rateBasisPoints, priceIncludesTax);

        return new Money(baseAmount);
    }

    /// <summary>
    /// True when the quantity can be written exactly in the product's selling unit.
    /// </summary>
    /// <remarks>
    /// A quantity the unit cannot express is refused rather than quietly rounded. A request for
    /// 2.6 kilograms of a product sold by the piece is a mistake, and the cashier is told about it
    /// instead of being handed three.
    /// </remarks>
    /// <param name="quantity">The quantity the cashier asked for.</param>
    /// <param name="scale">The precision of the product's selling unit.</param>
    /// <returns>True when the quantity fits the unit exactly.</returns>
    public static bool FitsScale(decimal quantity, int scale) =>
        Domain.Quantity.QuantityScale.Fits(quantity, scale);
}
