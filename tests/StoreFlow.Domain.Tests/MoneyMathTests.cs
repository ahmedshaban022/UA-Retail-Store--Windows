using StoreFlow.Domain.Money;
using Shouldly;

namespace StoreFlow.Domain.Tests;

using Money = StoreFlow.Domain.Money.Money;

public sealed class MoneyMathTests
{
    [Theory]
    [InlineData("12.345", "12.35")]
    [InlineData("12.344", "12.34")]
    [InlineData("-12.345", "-12.35")]
    [InlineData("0.005", "0.01")]
    public void Round_applies_half_away_from_zero(string input, string expected)
    {
        MoneyMath.Round(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Minor_units_round_trip_without_loss()
    {
        const decimal amount = 1234.56m;

        long minor = MoneyMath.RoundToMinorUnits(amount);

        minor.ShouldBe(123456);
        Money.FromMinorUnits(minor).Amount.ShouldBe(amount);
    }

    [Fact]
    public void Multiply_rounds_once_at_the_end()
    {
        MoneyMath.Multiply(19.99m, 3m).ShouldBe(59.97m);
    }

    [Fact]
    public void Apply_rate_uses_basis_points()
    {
        MoneyMath.ApplyRate(100m, 1450).ShouldBe(14.50m);
    }

    [Fact]
    public void Clamp_discount_never_exceeds_the_base_amount()
    {
        MoneyMath.ClampDiscount(50m, 80m).ShouldBe(50m);
    }
}
