using StoreFlow.Domain.Money;

namespace StoreFlow.Domain.Pricing;

/// <summary>The kind of discount a line or invoice carries.</summary>
public enum DiscountKind
{
    /// <summary>A fixed amount subtracted from the base amount.</summary>
    Fixed = 0,

    /// <summary>A percentage of the base amount subtracted from it.</summary>
    Percent = 1,
}

/// <summary>
/// A discount descriptor. Either a fixed amount or a percentage — never both — so the resulting
/// numbers stay unambiguous and reconcilable.
/// </summary>
public readonly record struct DiscountSpec(DiscountKind Kind, decimal Value)
{
    public static DiscountSpec None => new(DiscountKind.Fixed, 0m);

    public static DiscountSpec FixedAmount(decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        return new DiscountSpec(DiscountKind.Fixed, amount);
    }

    public static DiscountSpec Percentage(decimal percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);

        if (percent > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "A percentage discount cannot exceed 100 %.");
        }

        return new DiscountSpec(DiscountKind.Percent, percent);
    }

    public bool IsNone => Kind == DiscountKind.Fixed && Value == 0m;
}

/// <summary>
/// Resolves a discount into a concrete monetary amount. The discounted amount can never become
/// negative, and the discount can never exceed the base amount (PRD POS-03).
/// </summary>
public static class DiscountCalculator
{
    /// <summary>The discount amount for <paramref name="baseAmount"/> under <paramref name="discount"/>.</summary>
    public static decimal DiscountAmount(decimal baseAmount, DiscountSpec discount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseAmount);

        if (discount.IsNone)
        {
            return 0m;
        }

        var raw = discount.Kind == DiscountKind.Percent
            ? MoneyMath.Percentage(baseAmount, discount.Value)
            : discount.Value;

        return MoneyMath.ClampDiscount(baseAmount, MoneyMath.Round(raw));
    }

    /// <summary>The amount after the discount is applied.</summary>
    public static decimal Apply(decimal baseAmount, DiscountSpec discount) =>
        MoneyMath.Round(baseAmount - DiscountAmount(baseAmount, discount));

    /// <summary>
    /// The effective percentage a fixed discount represents, used by the authorisation rules that gate
    /// discounts above a configured limit.
    /// </summary>
    public static decimal EffectivePercent(decimal baseAmount, DiscountSpec discount)
    {
        if (baseAmount <= 0m || discount.IsNone)
        {
            return 0m;
        }

        var amount = DiscountAmount(baseAmount, discount);

        return MoneyMath.Round(amount * 100m / baseAmount);
    }
}
