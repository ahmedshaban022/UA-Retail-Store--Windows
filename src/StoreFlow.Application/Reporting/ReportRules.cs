namespace StoreFlow.Application.Reporting;

using StoreFlow.Domain.Money;

/// <summary>The money on a posted sale, as the reports read it.</summary>
/// <param name="Total">What the customer owed, tax included when the tax is charged on top.</param>
/// <param name="Discount">Every discount given, line and whole-sale together.</param>
/// <param name="Tax">The tax charged.</param>
public sealed record SaleAmounts(Money Total, Money Discount, Money Tax);

/// <summary>The money on one line of a posted sale.</summary>
/// <param name="Total">What the line came to, tax included when the tax is charged on top.</param>
/// <param name="Discount">The discount on the line itself.</param>
/// <param name="InvoiceDiscountShare">The part of the whole-sale discount that was spread onto the line.</param>
/// <param name="Tax">The tax charged on the line.</param>
/// <param name="UnitCost">The cost of one unit, captured when the sale was posted.</param>
/// <param name="QuantityScaled">How many units, scaled by the product's precision.</param>
/// <param name="QuantityScale">How many decimal places the quantity has.</param>
public sealed record LineAmounts(
    Money Total,
    Money Discount,
    Money InvoiceDiscountShare,
    Money Tax,
    Money UnitCost,
    long QuantityScaled,
    int QuantityScale);

/// <summary>The money on a posted return.</summary>
/// <param name="RefundTotal">What went back to the customer, tax included.</param>
/// <param name="RefundTax">The tax that came back with it.</param>
public sealed record ReturnAmounts(Money RefundTotal, Money RefundTax);

/// <summary>The money on one line of a posted return.</summary>
/// <param name="RefundAmount">What went back to the customer for this line.</param>
/// <param name="OriginalUnitCost">The cost of one unit when it was sold.</param>
/// <param name="QuantityScaled">How many units came back, scaled by the product's precision.</param>
/// <param name="QuantityScale">How many decimal places the quantity has.</param>
public sealed record ReturnLineAmounts(
    Money RefundAmount,
    Money OriginalUnitCost,
    long QuantityScaled,
    int QuantityScale);

/// <summary>
/// The money the reports are built from. Every rule lives here, apart from the reading of the
/// database, so that each figure can be checked by hand.
/// </summary>
/// <remarks>
/// <para><b>The house rule.</b> Every figure the reports quote is stated <b>without the output tax</b>,
/// because the tax is the government's money and never belongs to the shop's takings. The tax is
/// still reported, on its own line, so the owner can see it.</para>
/// <para>The tax can be charged on top of the price or already be inside it. Both are handled by the
/// same rule: the tax is subtracted from the total, because in both cases the total carries it.</para>
/// </remarks>
public static class ReportRules
{
    /// <summary>
    /// What a sale took, without the tax. This is the figure the day's takings are measured by.
    /// </summary>
    public static Money NetSales(SaleAmounts amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        return amounts.Total - amounts.Tax;
    }

    /// <summary>
    /// What the goods on a sale were worth before any discount, without the tax.
    /// </summary>
    public static Money GrossSales(SaleAmounts amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        return NetSales(amounts) + amounts.Discount;
    }

    /// <summary>What a line of a sale took, without the tax.</summary>
    public static Money LineNetRevenue(LineAmounts line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.Total - line.Tax;
    }

    /// <summary>What the goods on a line were worth before any discount, without the tax.</summary>
    public static Money LineGoodsValue(LineAmounts line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return LineNetRevenue(line) + line.Discount + line.InvoiceDiscountShare;
    }

    /// <summary>Every discount given on a line, the line's own and its share of the whole-sale one.</summary>
    public static Money LineDiscount(LineAmounts line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.Discount + line.InvoiceDiscountShare;
    }

    /// <summary>What the units on a line cost the shop.</summary>
    public static Money LineCostOfGoodsSold(LineAmounts line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.UnitCost * Quantity(line.QuantityScaled, line.QuantityScale);
    }

    /// <summary>
    /// What was left on a line after the cost of the goods. A return takes its share of this away
    /// again, by exactly what it refunded and what those units cost.
    /// </summary>
    public static Money LineGrossProfit(LineAmounts line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return LineNetRevenue(line) - LineCostOfGoodsSold(line);
    }

    /// <summary>What a return gave back, without the tax.</summary>
    public static Money NetRefund(ReturnAmounts amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        return amounts.RefundTotal - amounts.RefundTax;
    }

    /// <summary>
    /// What the returned units cost the shop. They go back on the shelf at the cost they were sold
    /// at, so the profit falls by exactly what was refunded less this.
    /// </summary>
    public static Money ReturnCostOfGoodsSold(ReturnLineAmounts line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.OriginalUnitCost * Quantity(line.QuantityScaled, line.QuantityScale);
    }

    /// <summary>What a returned line took off the profit.</summary>
    public static Money ReturnProfitImpact(ReturnLineAmounts line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.RefundAmount - ReturnCostOfGoodsSold(line);
    }

    /// <summary>Turns a scaled quantity into the number the shop thinks in.</summary>
    public static decimal Quantity(long scaled, int scale) =>
        scaled / (decimal)Math.Pow(10, scale);

    /// <summary>Adds up a set of amounts.</summary>
    public static Money Sum(IEnumerable<Money> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        long total = 0;

        foreach (Money amount in amounts)
        {
            total += amount.ToMinorUnits();
        }

        return Money.FromMinorUnits(total);
    }

    /// <summary>
    /// Shares an amount out across parts in proportion to their size, so that the parts add up to
    /// the amount exactly. The last part carries the remainder, so nothing is lost to rounding.
    /// </summary>
    /// <param name="amount">The amount to share out.</param>
    /// <param name="parts">The parts to share it across, each weighed by its own size.</param>
    public static IReadOnlyList<Money> Split(Money amount, IReadOnlyList<Money> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var shares = new List<Money>(parts.Count);

        if (parts.Count == 0)
        {
            return shares;
        }

        long amountMinor = amount.ToMinorUnits();
        long totalMinor = 0;

        foreach (Money part in parts)
        {
            totalMinor += part.ToMinorUnits();
        }

        if (totalMinor == 0)
        {
            foreach (Money _ in parts)
            {
                shares.Add(Money.Zero);
            }

            return shares;
        }

        long handedOut = 0;

        for (int index = 0; index < parts.Count; index++)
        {
            long share = index == parts.Count - 1
                ? amountMinor - handedOut
                : amountMinor * parts[index].ToMinorUnits() / totalMinor;

            shares.Add(Money.FromMinorUnits(share));
            handedOut += share;
        }

        return shares;
    }

    /// <summary>
    /// Divides an amount by a count, rounding to the currency's minor unit in the way money is
    /// always rounded: away from zero.
    /// </summary>
    public static Money Divide(Money amount, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        decimal divided = amount.Amount / count;

        return new Money(MoneyMath.Round(divided));
    }
}
