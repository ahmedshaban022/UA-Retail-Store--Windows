using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StoreFlow.Application.Theming;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Pins the rules that keep a table readable: a cell keeps a margin, text that does not fit is
/// shortened instead of being cut, a heading may take a second line, and a box has room for the
/// line it holds.
/// </summary>
public sealed class TableLayoutTests
{
    /// <summary>
    /// The height of one line of the body text in the Arabic face, measured against the face that
    /// ships with the application. A box that cannot hold this much clips the bottom off the
    /// digits in it.
    /// </summary>
    private const double LineHeightOfTheBodyText = 20d;

    [Fact]
    public void A_cell_keeps_a_margin_on_both_sides()
    {
        Style cell = StyleFor(typeof(DataGridCell));

        Thickness padding = PaddingOf(cell);

        padding.Left.ShouldBeGreaterThan(0d);
        padding.Right.ShouldBeGreaterThan(0d);
        padding.Left.ShouldBe(padding.Right);
    }

    [Fact]
    public void A_heading_stands_over_the_values_it_names()
    {
        Style cell = StyleFor(typeof(DataGridCell));
        Style heading = StyleFor(typeof(DataGridColumnHeader));

        // The heading and the value under it have to start at the same place, or the heading
        // appears to name the column next door.
        PaddingOf(heading).Left.ShouldBe(PaddingOf(cell).Left);
    }

    [Fact]
    public void Text_that_does_not_fit_is_shortened_rather_than_cut()
    {
        bool shortens = SetterValue(NamedStyle("Type.Body"), TextBlock.TextTrimmingProperty)
            is TextTrimming trimming && trimming == TextTrimming.CharacterEllipsis;

        shortens.ShouldBeTrue("The body text has to be shortened with an ellipsis, not cut.");

        // A bare label - one with no style of its own - has to behave the same way.
        Style? bare = Themes()[typeof(TextBlock)] as Style;

        (bare is not null && SetterValue(bare, TextBlock.TextTrimmingProperty) is not null)
            .ShouldBeTrue("A bare label has to be shortened with an ellipsis, not cut.");
    }

    [Fact]
    public void A_heading_may_wrap_onto_a_second_line()
    {
        // The Arabic words for a heading are wider than the English ones. A heading that wraps
        // keeps its words; a heading that is cut keeps only the first of them.
        bool wraps = SetterValue(NamedStyle("Type.Caption"), TextBlock.TextWrappingProperty)
            is TextWrapping wrapping && wrapping == TextWrapping.Wrap;

        wraps.ShouldBeTrue("A heading has to be able to take a second line.");
    }

    [Fact]
    public void A_box_has_room_for_the_line_it_holds()
    {
        foreach (Type kind in new[] { typeof(TextBox), typeof(PasswordBox), typeof(ComboBox) })
        {
            Style style = StyleFor(kind);

            Thickness padding = PaddingOf(style);
            Thickness border = BorderOf(style);

            double room = new ThemeDefinition().Density.ControlHeight
                - (padding.Top + padding.Bottom)
                - (border.Top + border.Bottom);

            room.ShouldBeGreaterThanOrEqualTo(
                LineHeightOfTheBodyText,
                $"{kind.Name} leaves {room} for the text, and a line of body text is {LineHeightOfTheBodyText}.");
        }
    }

    private static Style StyleFor(Type kind)
    {
        ResourceDictionary themes = Themes();

        return themes[kind] as Style
            ?? throw new InvalidOperationException($"No style for {kind.Name} in the theme.");
    }

    private static Style NamedStyle(string key)
    {
        ResourceDictionary themes = Themes();

        return themes[key] as Style
            ?? throw new InvalidOperationException($"No style named {key} in the theme.");
    }

    private static ResourceDictionary Themes()
    {
        var themes = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/StoreFlow.Desktop;component/Themes/Base.xaml"),
        };

        themes.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/StoreFlow.Desktop;component/Themes/Controls.xaml"),
        });

        return themes;
    }

    private static object? SetterValue(Style style, DependencyProperty property)
    {
        Setter? setter = style.Setters.OfType<Setter>()
            .FirstOrDefault(candidate => candidate.Property == property);

        return setter?.Value;
    }

    private static Thickness PaddingOf(Style style)
    {
        return SetterValue(style, Control.PaddingProperty) is Thickness thickness ? thickness : default;
    }

    private static Thickness BorderOf(Style style)
    {
        return SetterValue(style, Control.BorderThicknessProperty) is Thickness thickness ? thickness : default;
    }
}
