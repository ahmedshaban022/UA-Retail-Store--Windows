using System;
using System.Linq;
using System.Windows.Media;
using StoreFlow.Desktop.Theming;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Covers the private font collection the embedded faces live in.
/// </summary>
/// <remarks>
/// <para>WPF only treats a folder in the application's resources as a private font collection if
/// every file in it can be read. One unreadable file in the folder takes the whole collection
/// down, and the symptom is a face that quietly resolves to the platform fallback — which looks
/// exactly like a font that failed to load. The collection is therefore pinned here.</para>
/// </remarks>
public sealed class PrivateFontCollectionTests
{
    [Fact]
    public void The_private_font_collection_holds_the_embedded_face()
    {
        var folder = new Uri(
            "pack://application:,,,/StoreFlow.Desktop;component/Fonts/", UriKind.Absolute);

        var families = Fonts.GetFontFamilies(folder).ToList();

        families.ShouldNotBeEmpty();
        families.ShouldContain(family => family.FamilyNames.Values.Contains(ThemeEngine.EmbeddedArabicFontName));
    }

    [Fact]
    public void The_private_font_collection_holds_nothing_else()
    {
        // A composite font, a readme or anything else WPF cannot read in this folder would take
        // the whole collection down, so the folder holds faces and nothing else.
        var folder = new Uri(
            "pack://application:,,,/StoreFlow.Desktop;component/Fonts/", UriKind.Absolute);

        var families = Fonts.GetFontFamilies(folder).ToList();

        families.Count.ShouldBe(1);
    }
}
