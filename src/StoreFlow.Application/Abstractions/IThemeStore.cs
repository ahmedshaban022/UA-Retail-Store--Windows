namespace StoreFlow.Application.Abstractions;

using StoreFlow.Application.Theming;

/// <summary>
/// Stores and retrieves the theme definition. The theme is the product's rebrand surface: colours,
/// title, logo, typography, density, currency and locale all live in one versioned JSON file.
/// </summary>
public interface IThemeStore
{
    /// <summary>Loads the active theme.</summary>
    Task<ThemeDefinition> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the active theme.</summary>
    Task SaveAsync(ThemeDefinition theme, CancellationToken cancellationToken = default);

    /// <summary>Lists the available theme presets and saved themes.</summary>
    Task<IReadOnlyList<ThemeDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Exports a theme as pretty-printed JSON.</summary>
    string Export(ThemeDefinition theme);

    /// <summary>Imports a theme from JSON, validating it first.</summary>
    ThemeDefinition Import(string json);
}
