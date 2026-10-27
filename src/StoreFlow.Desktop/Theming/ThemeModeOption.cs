using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.Theming;

/// <summary>
/// An appearance mode paired with the label the owner sees in the picker.
/// </summary>
/// <remarks>
/// The mode itself is an enum, and an enum renders as its identifier. Wrapping it in a record with
/// a display label lets the picker say "High contrast" instead of "HighContrast", and lets the
/// closed state of the select show the same word as the open list.
/// </remarks>
/// <param name="Value">The appearance mode this option selects.</param>
/// <param name="Display">The label shown to the owner.</param>
public sealed record ThemeModeOption(ThemeMode Value, string Display)
{
    /// <inheritdoc />
    public override string ToString() => Display;

    /// <summary>The available modes, with their labels.</summary>
    public static IReadOnlyList<ThemeModeOption> For(IStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return
        [
            new ThemeModeOption(ThemeMode.Light, strings[StringKeys.FirstRunThemeLight]),
            new ThemeModeOption(ThemeMode.Dark, strings[StringKeys.FirstRunThemeDark]),
            new ThemeModeOption(ThemeMode.HighContrast, strings[StringKeys.FirstRunThemeHighContrast]),
        ];
    }
}
