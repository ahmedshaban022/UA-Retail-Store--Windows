using StoreFlow.Application.Theming;

namespace StoreFlow.Infrastructure.Theming;

/// <summary>
/// The theme presets shipped with the product. A preset is a complete theme definition, so the
/// owner can start from one and adjust it rather than inventing colours from scratch.
/// </summary>
public static class BuiltInThemes
{
    /// <summary>The default light theme: warm paper ground, deep petrol accent.</summary>
    public static ThemeDefinition Light { get; } = new()
    {
        Schema = ThemeSchema.Current,
        Mode = ThemeMode.Light,
        ProductName = new LocalizedText { En = "StoreFlow", Ar = "ستور فلو" },
    };

    /// <summary>The dark variant, for a shop that works in dim light or at night.</summary>
    public static ThemeDefinition Dark { get; } = new()
    {
        Schema = ThemeSchema.Current,
        Mode = ThemeMode.Dark,
        ProductName = new LocalizedText { En = "StoreFlow", Ar = "ستور فلو" },
        Colors = new ThemeColors
        {
            SurfaceCanvas = "#14161A",
            SurfaceRaised = "#1C1F24",
            SurfaceSunken = "#101216",
            SurfaceOverlay = "#22262C",
            BorderSubtle = "#2C3138",
            BorderStrong = "#6B737E",
            TextPrimary = "#F2F3F5",
            TextSecondary = "#B9BEC6",
            TextMuted = "#8A9099",
            TextInverse = "#14161A",
            AccentDefault = "#3FA7AC",
            AccentHover = "#59BDC2",
            AccentPressed = "#2E8C91",
            AccentSubtle = "#17383A",
            AccentOn = "#0E1516",
            StateCritical = "#F2726A",
            StateWarning = "#E0A33C",
            StateSuccess = "#63B37C",
            StateInfo = "#64A6E0",
            StateCriticalSubtle = "#3A1F1D",
            StateWarningSubtle = "#372A16",
            StateSuccessSubtle = "#1B2E20",
            StateInfoSubtle = "#1A2836",
            FocusRing = "#3FA7AC",
            SelectionBackground = "#17383A",
            SelectionForeground = "#F2F3F5",
            ChartSeries1 = "#3FA7AC",
            ChartSeries2 = "#E0A33C",
            ChartSeries3 = "#63B37C",
            ChartSeries4 = "#F2726A",
            ChartSeries5 = "#64A6E0",
            ChartSeries6 = "#A98CD8",        },
    };

    /// <summary>A high-contrast variant that meets WCAG AAA for body text.</summary>
    public static ThemeDefinition HighContrast { get; } = new()
    {
        Schema = ThemeSchema.Current,
        Mode = ThemeMode.HighContrast,
        ProductName = new LocalizedText { En = "StoreFlow", Ar = "ستور فلو" },
        Colors = new ThemeColors
        {
            SurfaceCanvas = "#000000",
            SurfaceRaised = "#000000",
            SurfaceSunken = "#000000",
            SurfaceOverlay = "#000000",
            BorderSubtle = "#FFFFFF",
            BorderStrong = "#FFFFFF",
            TextPrimary = "#FFFFFF",
            TextSecondary = "#FFFFFF",
            TextMuted = "#E6E6E6",
            TextInverse = "#000000",
            AccentDefault = "#00E5FF",
            AccentHover = "#66F0FF",
            AccentPressed = "#00B8CC",
            AccentSubtle = "#00363F",
            AccentOn = "#000000",
            StateCritical = "#FF6B6B",
            StateWarning = "#FFD166",
            StateSuccess = "#7CFF9B",
            StateInfo = "#8FC7FF",
            StateCriticalSubtle = "#3A0000",
            StateWarningSubtle = "#3A3000",
            StateSuccessSubtle = "#003A12",
            StateInfoSubtle = "#001F3A",
            FocusRing = "#00E5FF",
            SelectionBackground = "#00363F",
            SelectionForeground = "#FFFFFF",
            ChartSeries1 = "#00E5FF",
            ChartSeries2 = "#FFD166",
            ChartSeries3 = "#7CFF9B",
            ChartSeries4 = "#FF6B6B",
            ChartSeries5 = "#8FC7FF",
            ChartSeries6 = "#D0A9FF",
        },
    };

    /// <summary>Every preset, in the order they are offered in the theme editor.</summary>
    public static IReadOnlyList<ThemeDefinition> All { get; } = [Light, Dark, HighContrast];
}
