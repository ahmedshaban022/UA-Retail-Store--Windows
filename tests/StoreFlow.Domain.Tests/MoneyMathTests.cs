using StoreFlow.Domain.Money;
using StoreFlow.Domain.Tax;
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

    [Theory]
    [InlineData("1.005", "1", "1.01")]
    [InlineData("-1.005", "1", "-1.01")]
    [InlineData("19.99", "3.005", "60.07")]
    public void Multiply_rounds_the_product_to_the_minor_unit(string price, string quantity, string expected)
    {
        // The product of a price and a quantity carries more decimals than the currency has.
        // Left unrounded it comes back with them, and the third decimal place travels onto the
        // invoice. The half goes away from zero, so a price that is exactly half a piastre over
        // is not quietly shortened to the even side.
        MoneyMath.Multiply(Parse(price), Parse(quantity)).ShouldBe(Parse(expected));
    }

    [Fact]
    public void Apply_rate_uses_basis_points()
    {
        MoneyMath.ApplyRate(100m, 1450).ShouldBe(14.50m);
    }

    [Theory]
    [InlineData("19.99", 1450, "2.90")]
    [InlineData("33.33", 1450, "4.83")]
    [InlineData("1.00", 1250, "0.13")]
    public void Apply_rate_rounds_the_result_to_the_minor_unit(string amount, int rateBasisPoints, string expected)
    {
        // 19.99 at 14.5 % is 2.89855 before it is rounded. A rate is applied to a price that has
        // already been worked out, and the figure that goes on the invoice has two decimals.
        MoneyMath.ApplyRate(Parse(amount), rateBasisPoints).ShouldBe(Parse(expected));
    }

    [Theory]
    [InlineData("19.99", 1450, "2.90")]
    [InlineData("33.33", 1450, "4.83")]
    public void Tax_on_a_fractional_exclusive_amount_is_rounded_to_the_minor_unit(
        string amount, int rateBasisPoints, string expected)
    {
        // The tax of a line whose price excludes the tax is the rate of the net amount, and it is
        // rounded like every other figure the shop writes down. Without the rounding the tax of
        // a price of 19.99 would be 2.89855 and the invoice would carry a third decimal place.
        TaxCalculator.OnExclusiveAmount(Parse(amount), rateBasisPoints).ShouldBe(Parse(expected));
    }

    [Fact]
    public void Clamp_discount_never_exceeds_the_base_amount()
    {
        MoneyMath.ClampDiscount(50m, 80m).ShouldBe(50m);
    }

    private static decimal Parse(string value) =>
        decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
