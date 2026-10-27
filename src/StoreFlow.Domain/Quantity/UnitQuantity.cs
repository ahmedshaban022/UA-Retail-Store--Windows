using System.Globalization;

namespace StoreFlow.Domain.Quantity;

/// <summary>
/// A quantity of goods expressed with the precision of its selling unit (0 for whole pieces, 2 for
/// kilograms, 3 for metres, ...). Quantities are persisted as scaled integers so no floating point is
/// ever involved in stock arithmetic.
/// </summary>
public readonly struct UnitQuantity : IEquatable<UnitQuantity>, IComparable<UnitQuantity>
{
    /// <summary>Creates a quantity, rounding to the unit's precision and validating that precision.</summary>
    public UnitQuantity(decimal value, int scale)
    {
        QuantityScale.EnsureValid(scale);

        Scale = scale;
        Value = Math.Round(value, scale, MidpointRounding.AwayFromZero);
    }

    /// <summary>The quantity in the selling unit's own terms, for example 2.5 kilograms.</summary>
    public decimal Value { get; }

    /// <summary>The precision of the selling unit this quantity is expressed in.</summary>
    public int Scale { get; }

    public bool IsZero => Value == 0m;

    public bool IsNegative => Value < 0m;

    public bool IsPositive => Value > 0m;

    /// <summary>The number of base units (pieces, kilograms) this quantity represents.</summary>
    public decimal BaseUnits => Value;

    /// <summary>Converts to the persisted representation: an integer scaled by 10^<see cref="Scale"/>.</summary>
    public long ToScaledLong() =>
        (long)Math.Round(Value * QuantityScale.PowerOfTen(Scale), 0, MidpointRounding.AwayFromZero);

    /// <summary>Reconstitutes a quantity from the persisted scaled integer.</summary>
    public static UnitQuantity FromScaledLong(long scaledValue, int scale) =>
        new(scaledValue / QuantityScale.PowerOfTen(scale), scale);

    public int CompareTo(UnitQuantity other) => Value.CompareTo(other.Value);

    public bool Equals(UnitQuantity other) =>
        Scale == other.Scale && Value == other.Value;

    public override bool Equals(object? obj) => obj is UnitQuantity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, Scale);

    public override string ToString() =>
        Value.ToString("0.################", CultureInfo.InvariantCulture);

    public static bool operator ==(UnitQuantity left, UnitQuantity right) => left.Equals(right);

    public static bool operator !=(UnitQuantity left, UnitQuantity right) => !left.Equals(right);

    public static UnitQuantity operator +(UnitQuantity left, UnitQuantity right)
    {
        EnsureSameScale(left, right);
        return new(left.Value + right.Value, left.Scale);
    }

    public static UnitQuantity operator -(UnitQuantity left, UnitQuantity right)
    {
        EnsureSameScale(left, right);
        return new(left.Value - right.Value, left.Scale);
    }

    private static void EnsureSameScale(UnitQuantity left, UnitQuantity right)
    {
        if (left.Scale != right.Scale)
        {
            throw new ArgumentException(
                $"Cannot combine quantities with different unit precision ({left.Scale} and {right.Scale}).");
        }
    }
}
