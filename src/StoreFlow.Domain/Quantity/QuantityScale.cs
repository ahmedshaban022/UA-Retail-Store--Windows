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
}
