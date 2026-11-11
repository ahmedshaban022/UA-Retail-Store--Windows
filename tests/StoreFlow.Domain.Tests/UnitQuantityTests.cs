using System.Globalization;
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
    public void A_value_on_the_half_is_rounded_away_from_zero()
    {
        // 2.565 is exactly half a piastre. Rounded to the even side it would come back as 2.56,
        // and the shelf would be short of what the cashier asked for.
        new UnitQuantity(2.565m, 2).Value.ShouldBe(2.57m);
        new UnitQuantity(-2.565m, 2).Value.ShouldBe(-2.57m);
    }

    [Theory]
    [InlineData(4, "1.23445", "1.2345")]
    [InlineData(5, "1.234565", "1.23457")]
    [InlineData(6, "1.2345665", "1.234567")]
    public void A_value_is_rounded_at_the_finest_precisions(int scale, string value, string expected)
    {
        // The units the shop sells by are not only kilograms: a metre of cloth and a metre of
        // trim are counted to three and six decimals, and the half goes away from zero there too.
        var quantity = new UnitQuantity(decimal.Parse(value, CultureInfo.InvariantCulture), scale);

        quantity.Value.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_quantity_at_the_finest_precision_survives_the_scaled_integer()
    {
        var quantity = new UnitQuantity(1.234567m, 6);

        quantity.ToScaledLong().ShouldBe(1_234_567);
        UnitQuantity.FromScaledLong(1_234_567, 6).Value.ShouldBe(1.234567m);
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
