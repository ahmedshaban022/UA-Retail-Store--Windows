using System.IO;
using System.Windows;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Theming;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

public sealed class ThemeEngineTests
{
    [Fact]
    public void The_generated_dictionary_exposes_every_semantic_role()
    {
        ResourceDictionary dictionary = ThemeEngine.Build(new ThemeDefinition());

        string[] expected =
        [
            ThemeEngine.SurfaceCanvasKey, ThemeEngine.SurfaceRaisedKey, ThemeEngine.SurfaceSunkenKey,
            ThemeEngine.SurfaceOverlayKey, ThemeEngine.BorderSubtleKey, ThemeEngine.BorderStrongKey,
            ThemeEngine.TextPrimaryKey, ThemeEngine.TextSecondaryKey, ThemeEngine.TextMutedKey,
            ThemeEngine.TextInverseKey, ThemeEngine.AccentDefaultKey, ThemeEngine.AccentHoverKey,
            ThemeEngine.AccentPressedKey, ThemeEngine.AccentSubtleKey, ThemeEngine.AccentOnKey,
            ThemeEngine.StateCriticalKey, ThemeEngine.StateWarningKey, ThemeEngine.StateSuccessKey,
            ThemeEngine.StateInfoKey, ThemeEngine.StateCriticalSubtleKey, ThemeEngine.StateWarningSubtleKey,
            ThemeEngine.StateSuccessSubtleKey, ThemeEngine.StateInfoSubtleKey, ThemeEngine.FocusRingKey,
            ThemeEngine.SelectionBackgroundKey, ThemeEngine.SelectionForegroundKey,
            ThemeEngine.ChartSeries1Key, ThemeEngine.ChartSeries2Key, ThemeEngine.ChartSeries3Key,
            ThemeEngine.ChartSeries4Key, ThemeEngine.ChartSeries5Key, ThemeEngine.ChartSeries6Key,
            "Font.Family.Ui", "Font.Size.Body", "Size.RowHeight", "Size.CornerRadius",
        ];

        foreach (string key in expected)
        {
            dictionary.Contains(key).ShouldBeTrue(key);
        }
    }

    [Fact]
    public void A_colour_role_becomes_a_frozen_brush_with_the_configured_value()
    {
        var theme = new ThemeDefinition();
        theme.Colors.AccentDefault = "#0F5E63";

        ResourceDictionary dictionary = ThemeEngine.Build(theme);

        var brush = dictionary[ThemeEngine.AccentDefaultKey].ShouldBeOfType<System.Windows.Media.SolidColorBrush>();
        brush.Color.ShouldBe(System.Windows.Media.Color.FromRgb(0x0F, 0x5E, 0x63));
        brush.IsFrozen.ShouldBeTrue();
    }

    [Fact]
    public void Shape_tokens_publish_the_types_the_control_templates_assign()
    {
        // Border.CornerRadius takes a CornerRadius and Border.BorderThickness takes a Thickness.
        // Publishing plain doubles makes the window throw an invalid cast at layout time, so the
        // theme hands out the real types.
        ResourceDictionary dictionary = ThemeEngine.Build(new ThemeDefinition());

        var radius = dictionary["Size.CornerRadius"].ShouldBeOfType<CornerRadius>();
        radius.TopLeft.ShouldBe(6d);
        radius.BottomRight.ShouldBe(6d);

        var width = dictionary["Size.BorderWidth"].ShouldBeOfType<Thickness>();
        width.Left.ShouldBe(1d);
        width.Top.ShouldBe(1d);
    }

    [Fact]
    public void The_reading_direction_follows_the_language()
    {
        ThemeEngine.FlowDirectionFor("ar").ShouldBe(FlowDirection.RightToLeft);
        ThemeEngine.FlowDirectionFor("en").ShouldBe(FlowDirection.LeftToRight);
    }

    [Fact]
    public void The_default_theme_names_the_embedded_Arabic_face()
    {
        new ThemeDefinition().Typography.ArabicFont.ShouldBe("Tajawal");
    }

    [Fact]
    public void Applying_a_theme_replaces_the_live_dictionary()
    {
        var live = new ThemeResourceDictionary();
        var target = new ResourceDictionary();
        target.MergedDictionaries.Add(live);

        var theme = new ThemeDefinition();
        theme.Colors.SurfaceCanvas = "#010203";

        ThemeEngine.Apply(target, theme);

        var brush = live[ThemeEngine.SurfaceCanvasKey].ShouldBeOfType<System.Windows.Media.SolidColorBrush>();
        brush.Color.ShouldBe(System.Windows.Media.Color.FromRgb(0x01, 0x02, 0x03));
    }
}
