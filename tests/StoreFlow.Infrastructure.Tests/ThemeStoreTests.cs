using StoreFlow.Application.Theming;
using StoreFlow.Infrastructure.Theming;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

public sealed class ThemeStoreTests : IAsyncLifetime
{
    private string _directory = string.Empty;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "storeflow-theme-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_saved_theme_is_read_back_unchanged()
    {
        var store = new FileThemeStore(_directory);

        var theme = new ThemeDefinition
        {
            ProductName = new LocalizedText { En = "Al-Market", Ar = "السوق" },
            Mode = ThemeMode.Dark,
        };

        await store.SaveAsync(theme);

        ThemeDefinition loaded = await store.LoadAsync();

        loaded.ProductName.En.ShouldBe("Al-Market");
        loaded.ProductName.Ar.ShouldBe("السوق");
        loaded.Mode.ShouldBe(ThemeMode.Dark);
    }

    [Fact]
    public async Task Loading_a_theme_that_was_never_saved_creates_the_default()
    {
        var store = new FileThemeStore(_directory);

        ThemeDefinition loaded = await store.LoadAsync();

        loaded.ProductName.En.ShouldBe("StoreFlow");
        File.Exists(Path.Combine(_directory, FileThemeStore.ActiveThemeFileName)).ShouldBeTrue();
    }

    [Fact]
    public void An_exported_theme_can_be_imported_again()
    {
        var store = new FileThemeStore(_directory);

        string json = store.Export(BuiltInThemes.HighContrast);

        ThemeDefinition imported = store.Import(json);

        imported.Mode.ShouldBe(ThemeMode.HighContrast);
        imported.Colors.AccentDefault.ShouldBe(BuiltInThemes.HighContrast.Colors.AccentDefault);
    }

    [Fact]
    public void A_file_with_an_unknown_schema_is_refused()
    {
        var store = new FileThemeStore(_directory);

        Should.Throw<FormatException>(() => store.Import("""{ "schema": "sftheme/99" }"""));
    }

    [Fact]
    public void The_built_in_presets_all_satisfy_the_contrast_rules()
    {
        foreach (ThemeDefinition theme in BuiltInThemes.All)
        {
            IReadOnlyList<ContrastIssue> issues = ThemeContrastValidator.Validate(theme);

            issues.ShouldNotContain(issue => issue.Severity == ContrastSeverity.Error, theme.Mode.ToString());
        }
    }
}
