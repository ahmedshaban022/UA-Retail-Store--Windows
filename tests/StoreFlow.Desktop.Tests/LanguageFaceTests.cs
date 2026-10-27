using System.Windows;
using System.Windows.Media;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Covers the face the interface follows the language into.
/// </summary>
/// <remarks>
/// The face is part of the theme, so a language change is a change of face as well as of words.
/// Without that, the Arabic interface would keep the Latin face after the shop switched to Arabic.
/// </remarks>
public sealed class LanguageFaceTests
{
    [Fact]
    public void Changing_the_language_changes_the_face()
    {
        var theme = new ThemeDefinition();
        var resources = new ResourceDictionary();
        var language = new LanguageService();
        var service = new WpfThemeService(resources, theme, language);

        service.Apply(theme);

        var arabic = resources["Font.Family.Ui"].ShouldBeOfType<FontFamily>();
        arabic.FamilyNames.Values.ShouldContain(
            name => name.Contains(ThemeEngineNames.Arabic, StringComparison.OrdinalIgnoreCase));

        language.SetLanguage("en");

        var latin = resources["Font.Family.Ui"].ShouldBeOfType<FontFamily>();
        latin.Source.ShouldContain(theme.Typography.UiFont);
        latin.Source.ShouldNotContain(ThemeEngineNames.Arabic);
    }

    [Fact]
    public void The_language_service_raises_its_change_only_when_the_language_really_changes()
    {
        var language = new LanguageService();
        int raised = 0;

        language.LanguageChanged += (_, _) => raised++;

        language.SetLanguage("en");
        language.SetLanguage("en");

        raised.ShouldBe(1);
    }

    /// <summary>The names the theme engine uses, spelled out so the test does not depend on them.</summary>
    private static class ThemeEngineNames
    {
        /// <summary>The embedded Arabic face.</summary>
        internal const string Arabic = "Tajawal";
    }
}
