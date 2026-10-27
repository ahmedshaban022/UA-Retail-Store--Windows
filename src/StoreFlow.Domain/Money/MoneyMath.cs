namespace StoreFlow.Domain.Money;

/// <summary>
/// The single place where money is rounded. Every monetary calculation in the product funnels through
/// here so that one documented rounding policy governs the whole application.
/// </summary>
/// <remarks>
/// Policy: <see cref="MidpointRounding.AwayFromZero"/> at two decimal places. Rounding is applied once
/// per line, and document totals are the sum of already-rounded lines, so an invoice total always equals
/// the sum of its line totals exactly and never drifts.
/// </remarks>
public static class MoneyMath
{
    /// <summary>Rounds an amount to the currency's minor unit using the product rounding policy.</summary>
    public static decimal Round(decimal amount) =>
        Math.Round(amount, Money.MinorUnitScale, MidpointRounding.AwayFromZero);

    /// <summary>Rounds an amount to a whole count of minor units (the persisted representation).</summary>
    public static long RoundToMinorUnits(decimal amount) =>
        (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rounds an amount that is <b>already</b> expressed in minor units to a whole number of minor
    /// units. Use this when the value is a per-unit cost or a quantity, never for a major amount.
    /// </summary>
    public static long RoundToWholeMinorUnits(decimal amount) =>
        (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Multiplies a unit price by a quantity and rounds once, at the end.</summary>
    public static decimal Multiply(decimal unitPrice, decimal quantity) =>
        Round(unitPrice * quantity);

    /// <summary>Applies a rate expressed in basis points (1 % = 100 bp) and rounds once.</summary>
    public static decimal ApplyRate(decimal amount, int rateBasisPoints) =>
        Round(amount * rateBasisPoints / 10_000m);

    /// <summary>
    /// Computes a discount amount from a percentage of <paramref name="baseAmount"/>.
    /// </summary>
    public static decimal Percentage(decimal baseAmount, decimal percent) =>
        Round(baseAmount * percent / 100m);

    /// <summary>
    /// Clamps a discount so a discounted amount can never fall below zero.
    /// </summary>
    public static decimal ClampDiscount(decimal baseAmount, decimal discountAmount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(discountAmount);

        return Math.Min(discountAmount, baseAmount);
    }
}
