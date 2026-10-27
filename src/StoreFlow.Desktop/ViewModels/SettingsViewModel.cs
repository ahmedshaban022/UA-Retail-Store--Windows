using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.Theming;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The settings screen: the product's identity and appearance. Everything here is written to the
/// theme file, so the shop can be rebranded without touching the code.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeStore _themeStore;
    private readonly IThemeService _themeService;
    private readonly LanguageService _languageService;
    private readonly IStrings _strings;

    [ObservableProperty]
    private string _productNameEn = string.Empty;

    [ObservableProperty]
    private string _productNameAr = string.Empty;

    [ObservableProperty]
    private ThemeModeOption? _selectedModeOption;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Creates the settings screen.</summary>
    public SettingsViewModel(
        IThemeStore themeStore,
        IThemeService themeService,
        LanguageService languageService,
        IStrings strings)
    {
        _themeStore = themeStore;
        _themeService = themeService;
        _languageService = languageService;
        _strings = strings;

        ThemeDefinition theme = _themeService.Current;

        ProductNameEn = theme.ProductName.En;
        ProductNameAr = theme.ProductName.Ar;

        Modes = ThemeModeOption.For(_strings);

        SelectedModeOption = Modes.FirstOrDefault(option => option.Value == theme.Mode) ?? Modes[0];

        SelectedLanguage = LanguageService.Available
            .FirstOrDefault(option => option.Code == _languageService.CurrentLanguage)
            ?? LanguageService.Available[0];
    }

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.NavSettings];

    /// <summary>The label for the language setting.</summary>
    public string LanguageLabel => _strings[StringKeys.SettingsLanguage];

    /// <summary>The label for the appearance setting.</summary>
    public string ThemeLabel => _strings[StringKeys.SettingsTheme];

    /// <summary>The label for the product name setting.</summary>
    public string ProductNameLabel => _strings[StringKeys.SettingsProductName];

    /// <summary>The label for the rebrand section.</summary>
    public string RebrandLabel => _strings[StringKeys.SettingsRebrand];

    /// <summary>The label on the save command.</summary>
    public string SaveLabel => _strings[StringKeys.CommonSave];

    /// <summary>The available appearance modes.</summary>
    public IReadOnlyList<ThemeModeOption> Modes { get; }

    /// <summary>The appearance mode the owner picked.</summary>
    public ThemeMode SelectedMode => SelectedModeOption?.Value ?? ThemeMode.Light;

    /// <summary>The available languages.</summary>
    public static IReadOnlyList<LanguageOption> Languages => LanguageService.Available;

    /// <summary>Applies the language chosen on this screen.</summary>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is not null)
        {
            _languageService.SetLanguage(value.Code);
        }
    }

    /// <summary>Saves the identity and appearance, and applies them straight away.</summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        ThemeDefinition theme = _themeService.Current;

        theme.ProductName = new LocalizedText
        {
            En = ProductNameEn,
            Ar = ProductNameAr,
        };

        theme.Mode = SelectedMode;
        theme.Locale.Default = _languageService.CurrentLanguage;

        // Switching the appearance mode swaps in the matching preset's colours, while keeping the
        // owner's own product name.
        ThemeDefinition preset = PresetFor(SelectedMode);

        theme.Colors = preset.Colors;

        await _themeStore.SaveAsync(theme, cancellationToken);
        _themeService.Apply(theme);

        StatusMessage = _strings[StringKeys.SettingsSaved];
    }

    private static ThemeDefinition PresetFor(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => Infrastructure.Theming.BuiltInThemes.Dark,
        ThemeMode.HighContrast => Infrastructure.Theming.BuiltInThemes.HighContrast,
        _ => Infrastructure.Theming.BuiltInThemes.Light,
    };
}
