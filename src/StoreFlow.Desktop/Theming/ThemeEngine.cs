using System.Globalization;
using System.Windows;
using System.Windows.Media;
using StoreFlow.Application.Theming;

namespace StoreFlow.Desktop.Theming;

/// <summary>
/// Builds the live theme dictionary from a <see cref="ThemeDefinition"/> and installs it in the
/// application's resource tree.
/// </summary>
/// <remarks>
/// <para>The theme file is the product's rebrand surface. Everything a shop owner can change — the
/// product name, the colours, the typography, the density, the currency — lives in that file, and
/// the engine turns it into WPF resources without recompiling the application.</para>
/// <para>Colours are exposed as <c>DynamicResource</c> keys, so swapping the dictionary repaints
/// every open window immediately.</para>
/// </remarks>
public static class ThemeEngine
{
    /// <summary>Semantic colour resource keys.</summary>
    public const string SurfaceCanvasKey = "Surface.Canvas";
    public const string SurfaceRaisedKey = "Surface.Raised";
    public const string SurfaceSunkenKey = "Surface.Sunken";
    public const string SurfaceOverlayKey = "Surface.Overlay";
    public const string BorderSubtleKey = "Border.Subtle";
    public const string BorderStrongKey = "Border.Strong";
    public const string TextPrimaryKey = "Text.Primary";
    public const string TextSecondaryKey = "Text.Secondary";
    public const string TextMutedKey = "Text.Muted";
    public const string TextInverseKey = "Text.Inverse";
    public const string AccentDefaultKey = "Accent.Default";
    public const string AccentHoverKey = "Accent.Hover";
    public const string AccentPressedKey = "Accent.Pressed";
    public const string AccentSubtleKey = "Accent.Subtle";
    public const string AccentOnKey = "Accent.On";
    public const string StateCriticalKey = "State.Critical";
    public const string StateWarningKey = "State.Warning";
    public const string StateSuccessKey = "State.Success";
    public const string StateInfoKey = "State.Info";
    public const string StateCriticalSubtleKey = "State.Critical.Subtle";
    public const string StateWarningSubtleKey = "State.Warning.Subtle";
    public const string StateSuccessSubtleKey = "State.Success.Subtle";
    public const string StateInfoSubtleKey = "State.Info.Subtle";
    public const string FocusRingKey = "Focus.Ring";
    public const string SelectionBackgroundKey = "Selection.Background";
    public const string SelectionForegroundKey = "Selection.Foreground";

    /// <summary>Chart series colour resource keys.</summary>
    public const string ChartSeries1Key = "Chart.Series1";
    public const string ChartSeries2Key = "Chart.Series2";
    public const string ChartSeries3Key = "Chart.Series3";
    public const string ChartSeries4Key = "Chart.Series4";
    public const string ChartSeries5Key = "Chart.Series5";
    public const string ChartSeries6Key = "Chart.Series6";

    /// <summary>
    /// Builds the theme dictionary. Exposed separately from <see cref="Apply"/> so the theme editor
    /// can preview a candidate theme without installing it.
    /// </summary>
    /// <param name="theme">The theme to build the dictionary from.</param>
    /// <param name="languageCode">
    /// The language the interface is in, which decides the face. Defaults to the theme's own
    /// default language.
    /// </param>
    public static ResourceDictionary Build(ThemeDefinition theme, string? languageCode = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        ThemeColors colors = theme.Colors;

        var dictionary = new ResourceDictionary
        {
            [SurfaceCanvasKey] = Brush(colors.SurfaceCanvas),
            [SurfaceRaisedKey] = Brush(colors.SurfaceRaised),
            [SurfaceSunkenKey] = Brush(colors.SurfaceSunken),
            [SurfaceOverlayKey] = Brush(colors.SurfaceOverlay),
            [BorderSubtleKey] = Brush(colors.BorderSubtle),
            [BorderStrongKey] = Brush(colors.BorderStrong),
            [TextPrimaryKey] = Brush(colors.TextPrimary),
            [TextSecondaryKey] = Brush(colors.TextSecondary),
            [TextMutedKey] = Brush(colors.TextMuted),
            [TextInverseKey] = Brush(colors.TextInverse),
            [AccentDefaultKey] = Brush(colors.AccentDefault),
            [AccentHoverKey] = Brush(colors.AccentHover),
            [AccentPressedKey] = Brush(colors.AccentPressed),
            [AccentSubtleKey] = Brush(colors.AccentSubtle),
            [AccentOnKey] = Brush(colors.AccentOn),
            [StateCriticalKey] = Brush(colors.StateCritical),
            [StateWarningKey] = Brush(colors.StateWarning),
            [StateSuccessKey] = Brush(colors.StateSuccess),
            [StateInfoKey] = Brush(colors.StateInfo),
            [StateCriticalSubtleKey] = Brush(colors.StateCriticalSubtle),
            [StateWarningSubtleKey] = Brush(colors.StateWarningSubtle),
            [StateSuccessSubtleKey] = Brush(colors.StateSuccessSubtle),
            [StateInfoSubtleKey] = Brush(colors.StateInfoSubtle),
            [FocusRingKey] = Brush(colors.FocusRing),
            [SelectionBackgroundKey] = Brush(colors.SelectionBackground),
            [SelectionForegroundKey] = Brush(colors.SelectionForeground),
            [ChartSeries1Key] = Brush(colors.ChartSeries1),
            [ChartSeries2Key] = Brush(colors.ChartSeries2),
            [ChartSeries3Key] = Brush(colors.ChartSeries3),
            [ChartSeries4Key] = Brush(colors.ChartSeries4),
            [ChartSeries5Key] = Brush(colors.ChartSeries5),
            [ChartSeries6Key] = Brush(colors.ChartSeries6),

            ["Font.Family.Ui"] = ResolveUiFontFamily(theme, languageCode),
            ["Font.Size.Body"] = 14d * theme.Typography.Scale,
            ["Font.Size.Small"] = 12d * theme.Typography.Scale,
            ["Font.Size.Title"] = 20d * theme.Typography.Scale,
            ["Font.Size.Display"] = 28d * theme.Typography.Scale,
            ["Font.Size.PosTotal"] = 34d * theme.Typography.Scale,

            // The row and control heights follow the font scale. A theme that asks for larger type
            // gets taller rows and taller boxes, so the Arabic text is not cut off at the bottom of
            // the control it sits in. Without this the text grows while the box it lives in does
            // not, and the clerk loses the last line of what they are reading.
            ["Size.RowHeight"] = theme.Density.RowHeight * theme.Typography.Scale,
            ["Size.ControlHeight"] = theme.Density.ControlHeight * theme.Typography.Scale,
            // A row that holds an editor - a box or a picker the clerk types into - has to be taller
            // than a row that only holds text, otherwise the control is squeezed and the digits in
            // it are cut off at the bottom. The extra room is the control plus a little air.
            ["Size.EditorRowHeight"] = (theme.Density.ControlHeight + 12d) * theme.Typography.Scale,
            ["Size.SpacingUnit"] = theme.Density.SpacingUnit * theme.Typography.Scale,
            ["Size.PosHitTarget"] = theme.Density.PosHitTarget * theme.Typography.Scale,

            // The box that takes a paragraph - a reason, a description. It has to grow with the
            // font, or the Arabic text is cut off at the bottom of the box.
            ["Size.MultilineFieldHeight"] = 72d * theme.Typography.Scale,

            // CornerRadius and Thickness are the types the control templates actually assign to
            // Border.CornerRadius and Border.BorderThickness. Publishing plain doubles here would
            // fail at layout time with an invalid cast, so the theme hands out the real types.
            ["Size.CornerRadius"] = new CornerRadius(
                theme.Density.SpacingUnit > 0 ? theme.Shape.CornerRadius : 0d),
            ["Size.BorderWidth"] = new Thickness(theme.Shape.BorderWidth),
        };

        return dictionary;
    }

    /// <summary>Builds the theme dictionary and installs it in the application's resources.</summary>
    /// <param name="target">The resource dictionary the theme is installed into.</param>
    /// <param name="theme">The theme to install.</param>
    /// <param name="languageCode">
    /// The language the interface is in, which decides the face. Defaults to the theme's own
    /// default language.
    /// </param>
    public static void Apply(ResourceDictionary target, ThemeDefinition theme, string? languageCode = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        ResourceDictionary generated = Build(theme, languageCode);

        // The live dictionary is replaced in place rather than swapped for a new one, so that the
        // keys keep their identity and every DynamicResource in the application follows the change.
        ThemeResourceDictionary? live = null;

        foreach (ResourceDictionary merged in target.MergedDictionaries)
        {
            if (merged is ThemeResourceDictionary candidate)
            {
                live = candidate;
                break;
            }
        }

        if (live is not null)
        {
            live.Replace(generated);
        }
        else
        {
            // A target that has no live dictionary yet gets one, and only one: a second one would
            // shadow the first and the interface would keep showing the older theme.
            var installed = new ThemeResourceDictionary();
            installed.Replace(generated);

            target.MergedDictionaries.Insert(0, installed);
        }
    }

    /// <summary>Parses a hexadecimal colour into a frozen brush.</summary>
    public static SolidColorBrush Brush(string hex)
    {
        Color color = ColorConverter.ConvertFromString(hex) is Color parsed
            ? parsed
            : Colors.Transparent;

        var brush = new SolidColorBrush(color);
        brush.Freeze();

        return brush;
    }

    /// <summary>The flow direction for a language code.</summary>
    public static FlowDirection FlowDirectionFor(string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);

        return new CultureInfo(languageCode).TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    /// <summary>The system face used when the Arabic face has no glyph for a character.</summary>
    private const string SystemArabicFallback = "Sakkal Majalla";

    /// <summary>The Latin face the product ships with.</summary>
    private const string DefaultLatinFont = "Segoe UI";

    /// <summary>The Arabic face the product ships with, embedded in the application.</summary>
    public const string EmbeddedArabicFontName = "Tajawal";

    /// <summary>
    /// The folder in the application's own resource set that holds the embedded faces.
    /// </summary>
    /// <remarks>
    /// A WPF application treats a folder called <c>Fonts</c> in its resources as a private font
    /// collection: the faces in it are available to the application without being installed on the
    /// machine. The URI is relative to the entry assembly, which is the desktop application.
    /// </remarks>
    public const string EmbeddedFontsFolderUri = "pack://application:,,,/Fonts/";

    /// <summary>
    /// The folder that holds the embedded faces, addressed by assembly.
    /// </summary>
    /// <remarks>
    /// The entry assembly is the application itself when the application runs, but it is the test
    /// runner in a test. Naming the assembly keeps the private font collection reachable in both,
    /// which is what lets the embedded face be tested rather than assumed.
    /// </remarks>
    private const string EmbeddedFontsFolderByAssemblyUri =
        "pack://application:,,,/StoreFlow.Desktop;component/Fonts/";

    private static FontFamily? _embeddedArabicFamily;

    /// <summary>
    /// The embedded Arabic face, addressed as a private font family.
    /// </summary>
    /// <remarks>
    /// <para>The family has to come from the font collection that owns it rather than from a URI
    /// written out by hand. WPF hands out a family whose source is relative to the collection, and
    /// a hand-written URI quietly resolves to the platform fallback instead — which looks exactly
    /// like a font that failed to load.</para>
    /// <para>A file in the folder that WPF cannot read breaks the whole collection, so the folder
    /// holds nothing but the faces.</para>
    /// <para>Only a successful lookup is remembered. A failed one is tried again, because the
    /// collection can become reachable later — which is exactly what happens in a test host that
    /// points WPF at the desktop assembly after the first attempt.</para>
    /// </remarks>
    public static FontFamily? EmbeddedArabicFamily
    {
        get
        {
            if (_embeddedArabicFamily is not null)
            {
                return _embeddedArabicFamily;
            }

#pragma warning disable CA1031 // A font that cannot load must not stop the application.
            try
            {
                foreach (string folder in new[] { EmbeddedFontsFolderByAssemblyUri, EmbeddedFontsFolderUri })
                {
                    var uri = new Uri(folder, UriKind.Absolute);

                    foreach (FontFamily family in Fonts.GetFontFamilies(uri))
                    {
                        foreach (string name in family.FamilyNames.Values)
                        {
                            if (string.Equals(name, EmbeddedArabicFontName, StringComparison.OrdinalIgnoreCase))
                            {
                                _embeddedArabicFamily = family;
                                break;
                            }
                        }

                        if (_embeddedArabicFamily is not null)
                        {
                            break;
                        }
                    }

                    if (_embeddedArabicFamily is not null)
                    {
                        break;
                    }
                }
            }
            catch (Exception)
            {
                _embeddedArabicFamily = null;
            }
#pragma warning restore CA1031

            return _embeddedArabicFamily;
        }
    }

    /// <summary>Builds the family the interface is drawn in.</summary>
    /// <remarks>
    /// <para>Each language is set in the face that was designed for it. The Arabic interface is
    /// set in the face that ships with the product, so a shop PC needs no font installation and
    /// every machine looks the same. That face carries a complete Latin as well, so a product
    /// name or a figure inside an Arabic line reads as one voice rather than as two fonts
    /// arguing.</para>
    /// <para>A theme that names other faces is expressed as WPF's native family list, which
    /// resolves against the fonts installed on the machine. That is the live rebrand path.</para>
    /// </remarks>
    public static FontFamily ResolveUiFontFamily(ThemeDefinition theme, string? languageCode = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        string ui = string.IsNullOrWhiteSpace(theme.Typography.UiFont)
            ? DefaultLatinFont
            : theme.Typography.UiFont.Trim();

        string arabic = string.IsNullOrWhiteSpace(theme.Typography.ArabicFont)
            ? EmbeddedArabicFontName
            : theme.Typography.ArabicFont.Trim();

        string language = string.IsNullOrWhiteSpace(languageCode)
            ? theme.Locale.Default
            : languageCode.Trim();

        bool isRightToLeft = FlowDirectionFor(language) == FlowDirection.RightToLeft;

        if (isRightToLeft &&
            string.Equals(arabic, EmbeddedArabicFontName, StringComparison.OrdinalIgnoreCase))
        {
            FontFamily? embedded = EmbeddedArabicFamily;

            if (embedded is not null)
            {
                return embedded;
            }
        }

        if (isRightToLeft)
        {
            return new FontFamily($"{ui}, {arabic}, {SystemArabicFallback}");
        }

        // The Latin interface is set in the Latin face, with a system Arabic face behind it so that
        // a stray Arabic character still has a home.
        return new FontFamily($"{ui}, {SystemArabicFallback}");
    }

    /// <summary>
    /// A description of how the faces resolved, for the start-up log.
    /// </summary>
    /// <remarks>
    /// A shop PC has no debugger, so the only way to answer "which face is the interface actually
    /// using?" after the fact is to have written it down.
    /// </remarks>
    public static string DescribeFontResolution(ThemeDefinition theme, string? languageCode = null)
    {
        var lines = new List<string>();

        void Describe(string label, FontFamily family)
        {
            lines.Add($"{label}: source={family.Source}");
            lines.Add($"  names={string.Join(", ", family.FamilyNames.Select(n => n.Value))}");

            try
            {
                foreach (Typeface typeface in family.GetTypefaces())
                {
                    lines.Add($"  typeface={typeface.FontFamily.Source} weight={typeface.Weight} style={typeface.Style}");
                }
            }
#pragma warning disable CA1031 // Diagnostics must never take the application down.
            catch (Exception ex)
            {
                lines.Add($"  typefaces unavailable: {ex.Message}");
            }
#pragma warning restore CA1031
        }

        FontFamily ui = ResolveUiFontFamily(theme, languageCode);
        Describe("interface", ui);

        FontFamily? arabic = EmbeddedArabicFamily;
        lines.Add(arabic is null
            ? "embedded arabic face: not available"
            : $"embedded arabic face: {string.Join(", ", arabic.FamilyNames.Select(n => n.Value))}");

        return string.Join(Environment.NewLine, lines);
    }
}