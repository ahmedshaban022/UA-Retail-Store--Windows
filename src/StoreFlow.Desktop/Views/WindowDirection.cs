using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.Theming;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// Keeps a window's reading direction in step with the interface language.
/// </summary>
/// <remarks>
/// Right-to-left is a layout mode, not a decoration: the navigation rail moves to the other edge,
/// the command bar reverses, and the whole window mirrors. Applying it from one place means every
/// window agrees, and that a language change is reflected everywhere at once.
/// </remarks>
public static class WindowDirection
{
    /// <summary>Applies the direction that matches the current language.</summary>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.FlowDirection = ThemeEngine.FlowDirectionFor(CurrentLanguage());
    }

    /// <summary>Applies the direction now and whenever the language changes.</summary>
    public static void Watch(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        Apply(window);

        LanguageService? language = Resolve();

        if (language is not null)
        {
            language.LanguageChanged += (_, _) => Apply(window);
        }
    }

    /// <summary>The language code the application is currently using.</summary>
    public static string CurrentLanguage() =>
        Resolve()?.CurrentLanguage ?? "ar";

    private static LanguageService? Resolve() =>
        App.Host?.Services.GetService(typeof(LanguageService)) as LanguageService;
}
