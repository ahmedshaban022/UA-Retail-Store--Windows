using System.Globalization;

namespace StoreFlow.Desktop.Localization;

/// <summary>A language the application can run in.</summary>
/// <param name="Code">The culture code, for example <c>ar</c> or <c>en</c>.</param>
/// <param name="NativeName">The language's own name, shown in the language picker.</param>
public sealed record LanguageOption(string Code, string NativeName)
{
    /// <summary>
    /// The language's own name.
    /// </summary>
    /// <remarks>
    /// A select shows the selected item's own text. Without this override the closed control would
    /// show the record's generated <c>ToString</c>, which reads as
    /// <c>LanguageOption { Code = ar, NativeName = ... }</c> instead of the language name.
    /// </remarks>
    public override string ToString() => NativeName;
}

/// <summary>
/// Switches the interface language at run time. The resource sets are satellite assemblies, so a
/// change is instant and does not disturb the open windows beyond the text that changes.
/// </summary>
public sealed class LanguageService
{
    private static readonly LanguageOption[] Options =
    [
        new("ar", "العربية"),
        new("en", "English"),
    ];

    private string _currentLanguage = "ar";

    /// <summary>Creates the service and applies the default language's culture.</summary>
    /// <remarks>
    /// The culture has to be in place before any view model reads a string, because the view
    /// models take their labels at construction time. Applying it here means the very first
    /// screen is already in the right language.
    /// </remarks>
    public LanguageService() => ApplyCulture(_currentLanguage);

    /// <summary>Raised after the language changes.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>The languages the application offers.</summary>
    public static IReadOnlyList<LanguageOption> Available => Options;

    /// <summary>The language currently in use.</summary>
    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage == value)
            {
                return;
            }

            _currentLanguage = value;
            ApplyCulture(value);
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The culture that matches the current language.</summary>
    /// <remarks>
    /// Strings are resolved against this culture explicitly rather than against the ambient thread
    /// culture, so a screen cannot end up in a different language from the application.
    /// </remarks>
    public CultureInfo CurrentCulture => new(_currentLanguage);

    /// <summary>Sets the interface language.</summary>
    public void SetLanguage(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalised = code.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";

        // The culture is applied unconditionally: the language may already be the requested one and
        // the culture may still not have been applied to this thread.
        ApplyCulture(normalised);

        CurrentLanguage = normalised;
    }

    /// <summary>Applies a language to the current thread and to every thread created afterwards.</summary>
    public static void ApplyCulture(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        CultureInfo culture = new(code);

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }
}
