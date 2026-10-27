using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Localization;
using StoreFlow.Domain.Money;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The theme and currency services the interface depends on. It lives in the desktop layer because
/// applying a theme and formatting money are presentation concerns.
/// </summary>
public interface IThemeService
{
    /// <summary>The theme currently in use.</summary>
    ThemeDefinition Current { get; }

    /// <summary>Applies a theme immediately, without restarting the application.</summary>
    void Apply(ThemeDefinition theme);

    /// <summary>Formats a monetary amount using the shop's currency settings.</summary>
    string FormatMoney(Money amount);
}

/// <summary>
/// Applies themes to the running WPF application and formats money for display. Formatting follows
/// the currency's minor units and keeps Western Arabic numerals by default, because they are faster
/// to scan at the till; the setting can switch to Eastern Arabic-Indic numerals.
/// </summary>
public sealed class WpfThemeService : IThemeService
{
    private static readonly char[] EasternArabicDigits =
        ['٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩'];

    private readonly LanguageService _language;

    /// <summary>Creates the service.</summary>
    public WpfThemeService(
        System.Windows.ResourceDictionary target,
        ThemeDefinition theme,
        LanguageService language)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(language);

        Target = target;
        Current = theme;
        _language = language;

        language.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The resource dictionary the theme is installed into.</summary>
    public System.Windows.ResourceDictionary Target { get; }

    /// <inheritdoc />
    public ThemeDefinition Current { get; private set; }

    /// <inheritdoc />
    public void Apply(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Current = theme;
        Theming.ThemeEngine.Apply(Target, theme, _language.CurrentLanguage);
    }

    /// <summary>
    /// Re-applies the theme when the language changes, because the face is part of the theme.
    /// </summary>
    /// <remarks>
    /// The Arabic interface is set in the Arabic face and the Latin interface in the Latin face,
    /// so a language change is a change of face as well as of words.
    /// </remarks>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Theming.ThemeEngine.Apply(Target, Current, _language.CurrentLanguage);
    }

    /// <inheritdoc />
    public string FormatMoney(Money amount)
    {
        ThemeCurrencySettings currency = Current.Currency;

        string number = amount.Amount.ToString(
            "N" + Math.Max(0, currency.MinorUnits),
            System.Globalization.CultureInfo.InvariantCulture);

        if (Current.Locale.EasternArabicNumerals)
        {
            number = ToEasternArabicNumerals(number);
        }

        bool arabic = Current.Locale.Default.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        string symbol = arabic ? currency.SymbolAr : currency.SymbolEn;

        return string.Equals(currency.Position, "before", StringComparison.OrdinalIgnoreCase)
            ? $"{symbol} {number}"
            : $"{number} {symbol}";
    }

    private static string ToEasternArabicNumerals(string value)
    {
        char[] characters = value.ToCharArray();

        for (int index = 0; index < characters.Length; index++)
        {
            if (characters[index] is >= '0' and <= '9')
            {
                characters[index] = EasternArabicDigits[characters[index] - '0'];
            }
        }

        return new string(characters);
    }
}
