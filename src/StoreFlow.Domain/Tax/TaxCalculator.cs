using StoreFlow.Domain.Money;

namespace StoreFlow.Domain.Tax;

/// <summary>
/// Deterministic tax calculation for both pricing policies the product supports (PRD §6.2).
/// </summary>
/// <remarks>
/// Tax is always computed <b>per line</b> and snapshotted onto the invoice line together with the rate
/// and the inclusivity flag, so a later change to the store's tax settings can never alter a posted
/// invoice.
/// </remarks>
public static class TaxCalculator
{
    /// <summary>
    /// Tax for a line whose price <b>excludes</b> tax: the tax is added on top of the net amount.
    /// </summary>
    public static decimal OnExclusiveAmount(decimal netAmount, int rateBasisPoints) =>
        MoneyMath.ApplyRate(netAmount, rateBasisPoints);

    /// <summary>
    /// Tax for a line whose price <b>includes</b> tax: the tax is extracted from the gross amount.
    /// </summary>
    public static decimal OnInclusiveAmount(decimal grossAmount, int rateBasisPoints)
    {
        if (rateBasisPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rateBasisPoints));
        }

        if (grossAmount == 0m)
        {
            return 0m;
        }

        var rate = rateBasisPoints / 10_000m;
        var net = grossAmount / (1m + rate);

        return MoneyMath.Round(grossAmount - net);
    }

    /// <summary>
    /// The taxable base for a line, given the price and the inclusivity policy.
    /// </summary>
    public static decimal TaxableBase(decimal lineAmount, int rateBasisPoints, bool priceIncludesTax)
    {
        if (!priceIncludesTax)
        {
            return lineAmount;
        }

        var rate = rateBasisPoints / 10_000m;

        return MoneyMath.Round(lineAmount / (1m + rate));
    }
}
