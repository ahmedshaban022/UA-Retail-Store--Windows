namespace StoreFlow.Domain.Money;

/// <summary>
/// A percentage value. Persisted as a scaled integer (four decimal places) so percentage comparisons
/// and authorisation thresholds never touch floating point.
/// </summary>
public readonly record struct Percent(decimal Value) : IComparable<Percent>
{
    /// <summary>Number of decimal places kept for a percentage.</summary>
    public const int Scale = 4;

    public static readonly Percent Zero = new(0m);

    public bool IsZero => Value == 0m;

    public long ToScaledLong() =>
        (long)Math.Round(Value * 10_000m, 0, MidpointRounding.AwayFromZero);

    public static Percent FromScaledLong(long scaledValue) => new(scaledValue / 10_000m);

    public int CompareTo(Percent other) => Value.CompareTo(other.Value);

    public static Percent operator +(Percent left, Percent right) => new(left.Value + right.Value);

    public static Percent operator -(Percent left, Percent right) => new(left.Value - right.Value);

    public static bool operator >(Percent left, Percent right) => left.Value > right.Value;

    public static bool operator <(Percent left, Percent right) => left.Value < right.Value;

    public static bool operator >=(Percent left, Percent right) => left.Value >= right.Value;

    public static bool operator <=(Percent left, Percent right) => left.Value <= right.Value;
}
