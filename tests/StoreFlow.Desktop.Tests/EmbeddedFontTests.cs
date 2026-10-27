using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Theming;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Covers the face the Arabic interface is drawn in.
/// </summary>
/// <remarks>
/// The Arabic face is embedded in the application rather than installed on the machine, so a shop
/// PC needs no font installation and every machine looks the same. That promise is only worth
/// something if the face really travels with the application and really is the one the interface
/// uses, so both halves are pinned here.
/// </remarks>
public sealed class EmbeddedFontTests
{
    [Fact]
    public void The_Arabic_face_ships_inside_the_application()
    {
        // The faces are resources of the desktop assembly, not something a shop PC has to install.
        var manager = new ResourceManager("StoreFlow.Desktop.g", typeof(ThemeEngine).Assembly);

        using Stream? regular = manager.GetStream("fonts/tajawal-regular.ttf");
        using Stream? bold = manager.GetStream("fonts/tajawal-bold.ttf");

        regular.ShouldNotBeNull();
        bold.ShouldNotBeNull();

        regular!.Length.ShouldBeGreaterThan(0);
        bold!.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_embedded_Arabic_face_resolves_from_the_application_resources()
    {
        FontFamily? family = ThemeEngine.EmbeddedArabicFamily;

        family.ShouldNotBeNull("the private font collection of the desktop assembly must be reachable");

        family!.FamilyNames.Values.ShouldContain(
            name => name.Contains(ThemeEngine.EmbeddedArabicFontName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_embedded_Arabic_face_carries_a_regular_and_a_bold_cut()
    {
        FontFamily? family = ThemeEngine.EmbeddedArabicFamily;

        family.ShouldNotBeNull();

        List<FontWeight> weights = family!.GetTypefaces().Select(t => t.Weight).ToList();

        weights.ShouldContain(weight => weight == FontWeights.Normal);
        weights.ShouldContain(weight => weight == FontWeights.Bold);
    }

    [Fact]
    public void The_Arabic_interface_is_drawn_in_the_embedded_face()
    {
        var theme = new ThemeDefinition();

        FontFamily family = ThemeEngine.ResolveUiFontFamily(theme, "ar");

        family.FamilyNames.Values.ShouldContain(
            name => name.Contains(ThemeEngine.EmbeddedArabicFontName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_Latin_interface_is_drawn_in_the_Latin_face()
    {
        var theme = new ThemeDefinition();

        FontFamily family = ThemeEngine.ResolveUiFontFamily(theme, "en");

        string source = family.Source;

        source.ShouldContain(theme.Typography.UiFont);
        source.ShouldNotContain(ThemeEngine.EmbeddedArabicFontName);
    }

    [Fact]
    public void A_theme_that_names_other_faces_is_honoured()
    {
        // A rebrand names the faces it wants, and the interface follows.
        var theme = new ThemeDefinition();
        theme.Typography.UiFont = "Arial";
        theme.Typography.ArabicFont = "Tahoma";

        FontFamily family = ThemeEngine.ResolveUiFontFamily(theme, "ar");

        family.Source.ShouldContain("Arial");
        family.Source.ShouldContain("Tahoma");
    }
}
