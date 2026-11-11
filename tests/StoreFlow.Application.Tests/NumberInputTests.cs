using StoreFlow.Application.Common;
using Shouldly;

namespace StoreFlow.Application.Tests;

/// <summary>
/// The one place a number is read.
/// </summary>
/// <remarks>
/// A price typed with a decimal comma used to be read with the invariant culture alone, where the
/// comma is a thousands separator: "12,50" became 1250. These tests pin the rule that every
/// screen and every import now shares.
/// </remarks>
public sealed class NumberInputTests
{
    [Theory]
    [InlineData("12.50", 12.50)]
    [InlineData("12,50", 12.50)]
    [InlineData("0,99", 0.99)]
    [InlineData("450,00", 450.00)]
    [InlineData("12", 12)]
    [InlineData("-3.25", -3.25)]
    [InlineData("0", 0)]
    public void A_number_is_read_the_same_way_wherever_it_came_from(string text, double expected)
    {
        NumberInput.TryReadDecimal(text, out decimal value).ShouldBeTrue();
        value.ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("1,200", 1200)]
    [InlineData("1,200,000", 1200000)]
    public void A_comma_with_three_digits_behind_it_is_a_thousands_separator(string text, double expected)
    {
        NumberInput.TryReadDecimal(text, out decimal value).ShouldBeTrue();
        value.ShouldBe((decimal)expected);
    }

    [Fact]
    public void The_arabic_marks_are_read_as_the_marks_they_are()
    {
        // The Arabic decimal mark and the Arabic thousands mark, written the way the Arabic
        // keyboard writes them.
        NumberInput.TryReadDecimal("12٫50", out decimal value).ShouldBeTrue();
        value.ShouldBe(12.50m);

        NumberInput.TryReadDecimal("1٬200", out decimal thousands).ShouldBeTrue();
        thousands.ShouldBe(1200m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12.50.60")]
    [InlineData("1.234,56")]
    [InlineData("450.00 EGP")]
    public void Something_that_is_not_a_number_is_refused_rather_than_guessed_at(string text)
    {
        NumberInput.TryReadDecimal(text, out decimal value).ShouldBeFalse();
        value.ShouldBe(0m);
    }

    [Fact]
    public void A_negative_amount_is_refused_where_the_amount_may_not_be_negative()
    {
        NumberInput.TryReadMoney("-1.00", out decimal value).ShouldBeFalse();
        value.ShouldBe(0m);

        NumberInput.TryReadMoney("1.00", out value).ShouldBeTrue();
        value.ShouldBe(1.00m);
    }
}
