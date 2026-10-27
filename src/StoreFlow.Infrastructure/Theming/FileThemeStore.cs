using System.Text.Json;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Theming;

namespace StoreFlow.Infrastructure.Theming;

/// <summary>
/// Stores themes as JSON files. The theme file is the product's rebrand surface: editing it, or
/// dropping in a new one, changes the running application without recompiling it.
/// </summary>
public sealed class FileThemeStore : IThemeStore
{
    /// <summary>The file name of the theme currently in use.</summary>
    public const string ActiveThemeFileName = "active.sftheme.json";

    /// <summary>The extension every theme file uses.</summary>
    public const string ThemeFileExtension = ".sftheme.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _themesDirectory;

    /// <summary>Creates the store rooted at a themes directory, which is created if missing.</summary>
    public FileThemeStore(string themesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themesDirectory);

        _themesDirectory = themesDirectory;
        Directory.CreateDirectory(_themesDirectory);
    }

    /// <inheritdoc />
    public async Task<ThemeDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        string activePath = Path.Combine(_themesDirectory, ActiveThemeFileName);

        if (!File.Exists(activePath))
        {
            ThemeDefinition preset = BuiltInThemes.Light;
            await SaveAsync(preset, cancellationToken);
            return preset;
        }

        await using FileStream stream = File.OpenRead(activePath);
        return await JsonSerializer.DeserializeAsync<ThemeDefinition>(stream, SerializerOptions, cancellationToken)
               ?? BuiltInThemes.Light;
    }

    /// <inheritdoc />
    public async Task SaveAsync(ThemeDefinition theme, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme);

        string path = Path.Combine(_themesDirectory, ActiveThemeFileName);
        string json = Export(theme);

        // Write to a temporary file first so a crash cannot leave a half-written theme behind.
        string temporaryPath = path + ".tmp";

        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ThemeDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var themes = new List<ThemeDefinition>();

        foreach (string path in Directory.EnumerateFiles(_themesDirectory, $"*{ThemeFileExtension}", SearchOption.TopDirectoryOnly))
        {
            await using FileStream stream = File.OpenRead(path);
            ThemeDefinition? theme = await JsonSerializer.DeserializeAsync<ThemeDefinition>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (theme is not null)
            {
                themes.Add(theme);
            }
        }

        return themes;
    }

    /// <inheritdoc />
    public string Export(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return JsonSerializer.Serialize(theme, SerializerOptions);
    }

    /// <inheritdoc />
    public ThemeDefinition Import(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        ThemeDefinition theme = JsonSerializer.Deserialize<ThemeDefinition>(json, SerializerOptions)
                               ?? throw new FormatException("The file does not contain a valid theme.");

        if (!string.Equals(theme.Schema, ThemeSchema.Current, StringComparison.Ordinal))
        {
            throw new FormatException($"Unsupported theme schema '{theme.Schema}'. Expected '{ThemeSchema.Current}'.");
        }

        return theme;
    }
}
