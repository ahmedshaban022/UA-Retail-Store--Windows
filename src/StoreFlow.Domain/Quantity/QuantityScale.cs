namespace StoreFlow.Domain.Quantity;

/// <summary>
/// Scale factors for unit precision. Shared by every quantity calculation so the scaling rule is
/// defined exactly once.
/// </summary>
public static class QuantityScale
{
    /// <summary>Maximum supported unit precision.</summary>
    public const int MaxScale = 6;

    /// <summary>Throws when the requested precision is outside the supported range.</summary>
    public static void EnsureValid(int scale)
    {
        if (scale is < 0 or > MaxScale)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unit precision must be between 0 and 6.");
        }
    }

    /// <summary>10^<paramref name="scale"/> as an exact decimal factor.</summary>
    public static decimal PowerOfTen(int scale) => scale switch
    {
        0 => 1m,
        1 => 10m,
        2 => 100m,
        3 => 1_000m,
        4 => 10_000m,
        5 => 100_000m,
        6 => 1_000_000m,
        _ => throw new ArgumentOutOfRangeException(nameof(scale)),
    };

    /// <summary>
    /// True when the quantity can be written exactly in the unit of the given precision.
    /// </summary>
    /// <remarks>
    /// A quantity the unit cannot express is refused rather than quietly rounded. A request for
    /// 2.6 kilograms of a product sold by the piece is a mistake, and the operator is told about it
    /// instead of being handed three.
    /// </remarks>
    /// <param name="quantity">The quantity in the unit's own terms.</param>
    /// <param name="scale">The precision of the unit.</param>
    /// <returns>True when the quantity fits the unit exactly.</returns>
    public static bool Fits(decimal quantity, int scale)
    {
        if (scale is < 0 or > MaxScale)
        {
            return false;
        }

        decimal scaled = quantity * PowerOfTen(scale);

        if (decimal.Abs(scaled) > long.MaxValue)
        {
            return false;
        }

        return decimal.Truncate(scaled) == scaled;
    }
}
