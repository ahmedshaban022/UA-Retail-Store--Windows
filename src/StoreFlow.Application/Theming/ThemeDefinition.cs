using System.Text.Json.Serialization;

namespace StoreFlow.Application.Theming;

/// <summary>The schema identifier and version of a theme file.</summary>
public static class ThemeSchema
{
    /// <summary>The current schema identifier.</summary>
    public const string Current = "sftheme/1";
}

/// <summary>How the theme paints its surfaces.</summary>
public enum ThemeMode
{
    Light = 0,
    Dark = 1,
    HighContrast = 2,
}

/// <summary>
/// A piece of text that exists in both supported languages. The product is bilingual by
/// construction, so a label never ships with only one of its translations.
/// </summary>
public sealed class LocalizedText
{
    [JsonPropertyName("en")]
    public string En { get; set; } = string.Empty;

    [JsonPropertyName("ar")]
    public string Ar { get; set; } = string.Empty;

    /// <summary>Resolves the text for a locale, falling back to English when Arabic is missing.</summary>
    public string Resolve(string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);

        bool wantsArabic = locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        if (wantsArabic && !string.IsNullOrWhiteSpace(Ar))
        {
            return Ar;
        }

        return En;
    }

    /// <summary>True when both languages are present.</summary>
    [JsonIgnore]
    public bool HasBoth => !string.IsNullOrWhiteSpace(En) && !string.IsNullOrWhiteSpace(Ar);
}

/// <summary>
/// The complete, versioned description of the product's appearance. This file is the rebrand
/// surface: changing the title, the colours, the logo or the receipt changes the product without
/// recompiling it.
/// </summary>
public sealed class ThemeDefinition
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = ThemeSchema.Current;

    [JsonPropertyName("productName")]
    public LocalizedText ProductName { get; set; } = new() { En = "StoreFlow", Ar = "ستور فلو" };

    /// <summary>Path to the logo, relative to the application's brand folder.</summary>
    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; }

    [JsonPropertyName("mode")]
    public ThemeMode Mode { get; set; } = ThemeMode.Light;

    [JsonPropertyName("colors")]
    public ThemeColors Colors { get; set; } = new();

    [JsonPropertyName("typography")]
    public ThemeTypography Typography { get; set; } = new();

    [JsonPropertyName("density")]
    public ThemeDensity Density { get; set; } = new();

    [JsonPropertyName("shape")]
    public ThemeShape Shape { get; set; } = new();

    [JsonPropertyName("locale")]
    public ThemeLocaleSettings Locale { get; set; } = new();

    [JsonPropertyName("currency")]
    public ThemeCurrencySettings Currency { get; set; } = new();

    [JsonPropertyName("receipt")]
    public ThemeReceiptSettings Receipt { get; set; } = new();
}

/// <summary>
/// The semantic colour roles. Only roles are defined here — never raw palette names — so a theme
/// expresses intent and stays coherent when a role changes.
/// </summary>
public sealed class ThemeColors
{
    [JsonPropertyName("surface.canvas")]
    public string SurfaceCanvas { get; set; } = "#F7F5F1";

    [JsonPropertyName("surface.raised")]
    public string SurfaceRaised { get; set; } = "#FFFFFF";

    [JsonPropertyName("surface.sunken")]
    public string SurfaceSunken { get; set; } = "#EFECE6";

    [JsonPropertyName("surface.overlay")]
    public string SurfaceOverlay { get; set; } = "#FFFFFF";

    [JsonPropertyName("border.subtle")]
    public string BorderSubtle { get; set; } = "#E2DED6";

    [JsonPropertyName("border.strong")]
    public string BorderStrong { get; set; } = "#8B8578";

    [JsonPropertyName("text.primary")]
    public string TextPrimary { get; set; } = "#16181D";

    [JsonPropertyName("text.secondary")]
    public string TextSecondary { get; set; } = "#4A4E57";

    [JsonPropertyName("text.muted")]
    public string TextMuted { get; set; } = "#666C73";

    [JsonPropertyName("text.inverse")]
    public string TextInverse { get; set; } = "#FFFFFF";

    [JsonPropertyName("accent.default")]
    public string AccentDefault { get; set; } = "#0F5E63";

    [JsonPropertyName("accent.hover")]
    public string AccentHover { get; set; } = "#0C4C50";

    [JsonPropertyName("accent.pressed")]
    public string AccentPressed { get; set; } = "#093D41";

    [JsonPropertyName("accent.subtle")]
    public string AccentSubtle { get; set; } = "#DCEBEC";

    [JsonPropertyName("accent.on")]
    public string AccentOn { get; set; } = "#FFFFFF";

    [JsonPropertyName("state.critical")]
    public string StateCritical { get; set; } = "#B3261E";

    [JsonPropertyName("state.warning")]
    public string StateWarning { get; set; } = "#8A5200";

    [JsonPropertyName("state.success")]
    public string StateSuccess { get; set; } = "#2E6B3E";

    [JsonPropertyName("state.info")]
    public string StateInfo { get; set; } = "#1F5C99";

    [JsonPropertyName("state.critical.subtle")]
    public string StateCriticalSubtle { get; set; } = "#FBE9E7";

    [JsonPropertyName("state.warning.subtle")]
    public string StateWarningSubtle { get; set; } = "#FBF0DC";

    [JsonPropertyName("state.success.subtle")]
    public string StateSuccessSubtle { get; set; } = "#E4F0E6";

    [JsonPropertyName("state.info.subtle")]
    public string StateInfoSubtle { get; set; } = "#E3EEF8";

    [JsonPropertyName("focus.ring")]
    public string FocusRing { get; set; } = "#0F5E63";

    [JsonPropertyName("selection.background")]
    public string SelectionBackground { get; set; } = "#DCEBEC";

    [JsonPropertyName("selection.foreground")]
    public string SelectionForeground { get; set; } = "#16181D";

    [JsonPropertyName("chart.series1")]
    public string ChartSeries1 { get; set; } = "#0F5E63";

    [JsonPropertyName("chart.series2")]
    public string ChartSeries2 { get; set; } = "#B26B00";

    [JsonPropertyName("chart.series3")]
    public string ChartSeries3 { get; set; } = "#2E6B3E";

    [JsonPropertyName("chart.series4")]
    public string ChartSeries4 { get; set; } = "#B3261E";

    [JsonPropertyName("chart.series5")]
    public string ChartSeries5 { get; set; } = "#1F5C99";

    [JsonPropertyName("chart.series6")]
    public string ChartSeries6 { get; set; } = "#6B4E9B";
}

/// <summary>Typography settings.</summary>
public sealed class ThemeTypography
{
    [JsonPropertyName("uiFont")]
    public string UiFont { get; set; } = "Segoe UI";

    /// <summary>The Arabic face.</summary>
    /// <remarks>
    /// The default is Tajawal, which ships inside the application. A family that is not embedded
    /// is looked up on the system instead, so a rebrand can name any installed font.
    /// </remarks>
    [JsonPropertyName("arabicFont")]
    public string ArabicFont { get; set; } = "Tajawal";

    /// <summary>A multiplier applied to every font size.</summary>
    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1.0;

    /// <summary>True when money and quantity figures use tabular (aligned) digits.</summary>
    [JsonPropertyName("tabularFigures")]
    public bool TabularFigures { get; set; } = true;
}

/// <summary>Layout density.</summary>
public sealed class ThemeDensity
{
    /// <summary>Row height in data tables, in device-independent pixels.</summary>
    [JsonPropertyName("rowHeight")]
    public double RowHeight { get; set; } = 32d;

    /// <summary>Height of standard controls.</summary>
    [JsonPropertyName("controlHeight")]
    public double ControlHeight { get; set; } = 32d;

    /// <summary>Base spacing step.</summary>
    [JsonPropertyName("spacingUnit")]
    public double SpacingUnit { get; set; } = 8d;

    /// <summary>Minimum interactive target size on the point-of-sale screen.</summary>
    [JsonPropertyName("posHitTarget")]
    public double PosHitTarget { get; set; } = 44d;
}

/// <summary>Corner and border geometry.</summary>
public sealed class ThemeShape
{
    [JsonPropertyName("cornerRadius")]
    public double CornerRadius { get; set; } = 6d;

    [JsonPropertyName("borderWidth")]
    public double BorderWidth { get; set; } = 1d;
}

/// <summary>Language settings.</summary>
public sealed class ThemeLocaleSettings
{
    [JsonPropertyName("default")]
    public string Default { get; set; } = "ar";

    [JsonPropertyName("available")]
    public List<string> Available { get; set; } = ["ar", "en"];

    /// <summary>True when numbers are shown in Eastern Arabic-Indic digits.</summary>
    [JsonPropertyName("easternArabicNumerals")]
    public bool EasternArabicNumerals { get; set; }
}

/// <summary>Currency settings.</summary>
public sealed class ThemeCurrencySettings
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "EGP";

    /// <summary>Decimal places in the currency's minor unit.</summary>
    [JsonPropertyName("minorUnits")]
    public int MinorUnits { get; set; } = 2;

    [JsonPropertyName("symbolEn")]
    public string SymbolEn { get; set; } = "EGP";

    [JsonPropertyName("symbolAr")]
    public string SymbolAr { get; set; } = "ج.م";

    /// <summary>Either <c>before</c> or <c>after</c>.</summary>
    [JsonPropertyName("position")]
    public string Position { get; set; } = "after";
}

/// <summary>What appears on a printed receipt.</summary>
public sealed class ThemeReceiptSettings
{
    [JsonPropertyName("headerText")]
    public string HeaderText { get; set; } = string.Empty;

    [JsonPropertyName("footerText")]
    public string FooterText { get; set; } = string.Empty;

    [JsonPropertyName("showTaxTable")]
    public bool ShowTaxTable { get; set; } = true;

    [JsonPropertyName("showBarcode")]
    public bool ShowBarcode { get; set; } = true;
}
