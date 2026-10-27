using System.Globalization;

namespace StoreFlow.Domain.Money;

/// <summary>
/// An exact monetary amount. Money is never a <see cref="float"/> or <see cref="double"/>; the domain
/// works in <see cref="decimal"/> and is persisted as an integer number of minor units (piastres).
/// </summary>
/// <remarks>
/// The scale is fixed at <see cref="MinorUnitScale"/> for every currency supported in v1 (EGP, USD,
/// EUR, SAR, AED all use two minor digits). Keeping one scale keeps arithmetic exact and lets the
/// persistence layer use a single lossless <c>decimal -&gt; long</c> conversion.
/// </remarks>
public readonly record struct Money(decimal Amount) : IComparable<Money>
{
    /// <summary>Number of decimal places in the currency's minor unit.</summary>
    public const int MinorUnitScale = 2;

    /// <summary>The monetary factor of one base unit (e.g. one EGP, one kg of product).</summary>
    public static readonly Money Zero = new(0m);

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public bool IsPositive => Amount > 0m;

    /// <summary>Converts to the persisted representation: an integer count of minor units.</summary>
    public long ToMinorUnits() => MoneyMath.RoundToMinorUnits(Amount);

    /// <summary>Reconstitutes money from the persisted integer representation.</summary>
    public static Money FromMinorUnits(long minorUnits) => new(minorUnits / 100m);

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public override string ToString() =>
        Amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    public static Money operator -(Money value) => new(-value.Amount);

    public static Money operator *(Money value, decimal factor) => new(value.Amount * factor);

    public static Money operator /(Money value, decimal divisor) => new(value.Amount / divisor);

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;
}
