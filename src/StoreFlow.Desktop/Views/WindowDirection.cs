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

        Apply(window, CurrentLanguage());
    }

    /// <summary>Applies the direction that matches the given language.</summary>
    public static void Apply(Window window, string language)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.FlowDirection = ThemeEngine.FlowDirectionFor(language);
    }

    /// <summary>Applies the direction now and whenever the language changes.</summary>
    public static void Watch(Window window) => Watch(window, Resolve());

    /// <summary>Applies the direction now and whenever the given service's language changes.</summary>
    /// <remarks>
    /// The window takes the handler back when it is closed. A closed window must not stay in the
    /// service's list of listeners: it would keep the whole window — and everything it holds —
    /// alive for the rest of the session, and every change of language would go on redrawing a
    /// window that nobody can see.
    /// </remarks>
    public static void Watch(Window window, LanguageService? language)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (language is null)
        {
            Apply(window);
            return;
        }

        Apply(window, language.CurrentLanguage);

        EventHandler handler = (_, _) => Apply(window, language.CurrentLanguage);

        // The window holds the handler itself. The service holds its listeners weakly, so
        // nothing else would keep this one alive, and a window that had quietly stopped
        // mirroring would be a window that had quietly stopped working in Arabic.
        window.Closed += (_, _) => language.LanguageChanged -= handler;

        language.LanguageChanged += handler;
    }

    /// <summary>The language code the application is currently using.</summary>
    public static string CurrentLanguage() =>
        Resolve()?.CurrentLanguage ?? "ar";

    private static LanguageService? Resolve() =>
        App.Host?.Services.GetService(typeof(LanguageService)) as LanguageService;
}
