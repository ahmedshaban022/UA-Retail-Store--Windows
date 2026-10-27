using System.Globalization;
using System.Windows;
using StoreFlow.Desktop.Controls;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

public sealed class ConverterTests
{
    [Fact]
    public void A_string_with_content_is_visible_and_an_empty_one_is_not()
    {
        var converter = new StringToVisibilityConverter();

        converter.Convert("text", typeof(Visibility), null, CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Visible);
        converter.Convert(string.Empty, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Collapsed);
        converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Collapsed);
    }

    [Fact]
    public void A_boolean_visibility_can_be_inverted()
    {
        var converter = new BooleanToVisibilityConverter();

        converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Visible);
        converter.Convert(true, typeof(Visibility), "Invert", CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Collapsed);
        converter.Convert(false, typeof(Visibility), "Invert", CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Visible);
    }

    [Fact]
    public void An_integer_visibility_matches_the_expected_step()
    {
        var converter = new IntEqualsVisibilityConverter();

        converter.Convert(1, typeof(Visibility), "1", CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Visible);
        converter.Convert(1, typeof(Visibility), "2", CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Collapsed);
    }
}
