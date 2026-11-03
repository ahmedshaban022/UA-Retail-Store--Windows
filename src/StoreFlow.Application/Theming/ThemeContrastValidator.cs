using System.Globalization;

namespace StoreFlow.Application.Theming;

/// <summary>The severity of a contrast finding.</summary>
public enum ContrastSeverity
{
    /// <summary>Below WCAG AA: the theme must not be used as configured.</summary>
    Error = 0,

    /// <summary>Meets AA but not AAA: acceptable, and worth flagging.</summary>
    Warning = 1,
}

/// <summary>One contrast finding between two semantic colour roles.</summary>
/// <param name="Description">Which pair failed.</param>
/// <param name="Ratio">The measured contrast ratio.</param>
/// <param name="Required">The ratio required for AA.</param>
/// <param name="Severity">How serious the finding is.</param>
public sealed record ContrastIssue(
    string Description,
    double Ratio,
    double Required,
    ContrastSeverity Severity);

/// <summary>
/// Checks a theme's colour roles against the WCAG 2.1 contrast requirements, so the theme editor
/// can warn the owner before a combination becomes unreadable.
/// </summary>
/// <remarks>
/// Colours are handled as hex strings rather than a UI colour type, so the check runs in the
/// application layer and is unit-testable without a window.
/// </remarks>
public static class ThemeContrastValidator
{
    /// <summary>The WCAG 2.1 AA ratio for normal text.</summary>
    public const double NormalTextAa = 4.5;

    /// <summary>The WCAG 2.1 AA ratio for large text and non-text indicators.</summary>
    public const double LargeTextAa = 3.0;

    /// <summary>The WCAG 2.1 AAA ratio for normal text.</summary>
    public const double NormalTextAaa = 7.0;

    /// <summary>Validates every role pair that carries meaning.</summary>
    public static IReadOnlyList<ContrastIssue> Validate(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var issues = new List<ContrastIssue>();
        ThemeColors colors = theme.Colors;

        CheckText(issues, "text.primary", colors.TextPrimary, "surface.canvas", colors.SurfaceCanvas);
        CheckText(issues, "text.primary", colors.TextPrimary, "surface.raised", colors.SurfaceRaised);
        CheckText(issues, "text.primary", colors.TextPrimary, "surface.sunken", colors.SurfaceSunken);
        CheckText(issues, "text.primary", colors.TextPrimary, "surface.overlay", colors.SurfaceOverlay);
        CheckText(issues, "text.secondary", colors.TextSecondary, "surface.canvas", colors.SurfaceCanvas);
        CheckText(issues, "text.secondary", colors.TextSecondary, "surface.raised", colors.SurfaceRaised);
        CheckText(issues, "text.muted", colors.TextMuted, "surface.canvas", colors.SurfaceCanvas);
        CheckText(issues, "text.muted", colors.TextMuted, "surface.raised", colors.SurfaceRaised);
        CheckText(issues, "text.inverse", colors.TextInverse, "accent.default", colors.AccentDefault);
        CheckText(issues, "accent.on", colors.AccentOn, "accent.default", colors.AccentDefault);
        CheckText(issues, "accent.on", colors.AccentOn, "state.critical", colors.StateCritical);
        CheckText(issues, "accent.on", colors.AccentOn, "state.warning", colors.StateWarning);
        CheckText(issues, "accent.on", colors.AccentOn, "state.success", colors.StateSuccess);
        CheckText(issues, "accent.on", colors.AccentOn, "state.info", colors.StateInfo);
        CheckText(issues, "state.critical", colors.StateCritical, "surface.canvas", colors.SurfaceCanvas);
        CheckText(issues, "state.warning", colors.StateWarning, "surface.canvas", colors.SurfaceCanvas);
        CheckText(issues, "state.success", colors.StateSuccess, "surface.canvas", colors.SurfaceCanvas);
        CheckText(issues, "state.info", colors.StateInfo, "surface.canvas", colors.SurfaceCanvas);
        CheckText(issues, "state.critical", colors.StateCritical, "surface.raised", colors.SurfaceRaised);
        CheckText(issues, "state.warning", colors.StateWarning, "surface.raised", colors.SurfaceRaised);
        CheckText(issues, "state.success", colors.StateSuccess, "surface.raised", colors.SurfaceRaised);
        CheckText(issues, "state.info", colors.StateInfo, "surface.raised", colors.SurfaceRaised);
        CheckText(issues, "state.critical", colors.StateCritical, "surface.sunken", colors.SurfaceSunken);
        CheckText(issues, "state.warning", colors.StateWarning, "surface.sunken", colors.SurfaceSunken);
        CheckText(issues, "state.success", colors.StateSuccess, "surface.sunken", colors.SurfaceSunken);
        CheckText(issues, "state.info", colors.StateInfo, "surface.sunken", colors.SurfaceSunken);
        CheckText(issues, "state.critical", colors.StateCritical, "state.critical.subtle", colors.StateCriticalSubtle);
        CheckText(issues, "state.warning", colors.StateWarning, "state.warning.subtle", colors.StateWarningSubtle);
        CheckText(issues, "state.success", colors.StateSuccess, "state.success.subtle", colors.StateSuccessSubtle);
        CheckText(issues, "state.info", colors.StateInfo, "state.info.subtle", colors.StateInfoSubtle);
        CheckText(issues, "selection.foreground", colors.SelectionForeground, "selection.background", colors.SelectionBackground);

        CheckNonText(issues, "focus.ring", colors.FocusRing, "surface.canvas", colors.SurfaceCanvas);
        CheckNonText(issues, "focus.ring", colors.FocusRing, "surface.raised", colors.SurfaceRaised);
        CheckNonText(issues, "border.strong", colors.BorderStrong, "surface.canvas", colors.SurfaceCanvas);
        CheckNonText(issues, "border.strong", colors.BorderStrong, "surface.raised", colors.SurfaceRaised);
        CheckNonText(issues, "accent.default", colors.AccentDefault, "surface.canvas", colors.SurfaceCanvas);

        return issues;
    }

    /// <summary>
    /// The WCAG 2.1 contrast ratio between two hex colours, from 1 (identical) to 21 (black on white).
    /// </summary>
    public static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        (double Red, double Green, double Blue) foreground = ParseHex(foregroundHex);
        (double Red, double Green, double Blue) background = ParseHex(backgroundHex);

        double foregroundLuminance = RelativeLuminance(foreground);
        double backgroundLuminance = RelativeLuminance(background);

        double lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        double darker = Math.Min(foregroundLuminance, backgroundLuminance);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Parses a <c>#RGB</c> or <c>#RRGGBB</c> colour into normalised components.</summary>
    public static (double Red, double Green, double Blue) ParseHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);

        string value = hex.Trim().TrimStart('#');

        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        if (value.Length != 6 ||
            !int.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int red) ||
            !int.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int green) ||
            !int.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int blue))
        {
            throw new FormatException($"'{hex}' is not a valid hexadecimal colour.");
        }

        return (red / 255d, green / 255d, blue / 255d);
    }

    private static double RelativeLuminance((double Red, double Green, double Blue) colour) =>
        (0.2126 * Linearise(colour.Red)) +
        (0.7152 * Linearise(colour.Green)) +
        (0.0722 * Linearise(colour.Blue));

    private static double Linearise(double channel) =>
        channel <= 0.03928d ? channel / 12.92d : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);

    private static void CheckText(
        ICollection<ContrastIssue> issues,
        string foregroundRole,
        string foregroundHex,
        string backgroundRole,
        string backgroundHex)
    {
        double ratio = ContrastRatio(foregroundHex, backgroundHex);

        if (ratio < NormalTextAa)
        {
            issues.Add(new ContrastIssue(
                $"{foregroundRole} on {backgroundRole}",
                ratio,
                NormalTextAa,
                ContrastSeverity.Error));
        }
        else if (ratio < NormalTextAaa)
        {
            issues.Add(new ContrastIssue(
                $"{foregroundRole} on {backgroundRole}",
                ratio,
                NormalTextAaa,
                ContrastSeverity.Warning));
        }
    }

    private static void CheckNonText(
        ICollection<ContrastIssue> issues,
        string foregroundRole,
        string foregroundHex,
        string backgroundRole,
        string backgroundHex)
    {
        double ratio = ContrastRatio(foregroundHex, backgroundHex);

        if (ratio < LargeTextAa)
        {
            issues.Add(new ContrastIssue(
                $"{foregroundRole} on {backgroundRole}",
                ratio,
                LargeTextAa,
                ContrastSeverity.Error));
        }
    }
}
