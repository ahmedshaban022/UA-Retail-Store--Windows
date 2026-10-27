using StoreFlow.Domain.Quantity;
using Shouldly;

namespace StoreFlow.Domain.Tests;

public sealed class UnitQuantityTests
{
    [Fact]
    public void Quantities_round_trip_through_the_scaled_integer()
    {
        var quantity = new UnitQuantity(2.5m, 1);

        quantity.ToScaledLong().ShouldBe(25);
        UnitQuantity.FromScaledLong(25, 1).Value.ShouldBe(2.5m);
    }

    [Fact]
    public void A_value_is_rounded_to_the_unit_precision()
    {
        var quantity = new UnitQuantity(2.567m, 2);

        quantity.Value.ShouldBe(2.57m);
    }

    [Fact]
    public void Quantities_with_different_precisions_cannot_be_combined()
    {
        var pieces = new UnitQuantity(2m, 0);
        var kilograms = new UnitQuantity(2m, 3);

        Should.Throw<ArgumentException>(() => pieces + kilograms);
    }

    [Fact]
    public void An_unsupported_precision_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new UnitQuantity(1m, 7));
    }
}
