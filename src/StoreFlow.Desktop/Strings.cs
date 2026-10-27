using System.Globalization;
using System.Resources;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop;

/// <summary>
/// The default <see cref="IStrings"/>, backed by the application's resource sets.
/// </summary>
/// <remarks>
/// <para>The strings are resolved against <em>the application's language</em>, not against whatever
/// culture happens to be attached to the calling thread. The ambient thread culture is fragile: it
/// depends on where the call came from, it is silently reset by some framework internals, and it
/// can differ between the thread that built a view model and the thread that renders it. That is
/// how a screen ends up showing English while the application is in Arabic. Naming the culture
/// explicitly makes the answer depend only on the application's state.</para>
/// <para>English is the fallback culture and Arabic is the satellite, so a string that has not been
/// translated yet shows English rather than an empty label.</para>
/// </remarks>
public sealed class Strings : IStrings
{
    private static readonly ResourceManager Manager = new(
        "StoreFlow.Desktop.Resources.Strings",
        typeof(Strings).Assembly);

    private readonly LanguageService _languageService;

    /// <summary>Creates the service.</summary>
    public Strings(LanguageService languageService) =>
        _languageService = languageService;

    /// <inheritdoc />
    public string this[string key]
    {
        get
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            return Manager.GetString(key, _languageService.CurrentCulture) ?? key;
        }
    }

    /// <inheritdoc />
    public string Format(string key, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string format = this[key];

        try
        {
            return string.Format(_languageService.CurrentCulture, format, arguments);
        }
        catch (FormatException)
        {
            // A malformed placeholder must never take the screen down.
            return format;
        }
    }
}
