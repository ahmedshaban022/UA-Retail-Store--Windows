using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Application.Settings;
using StoreFlow.Application.Setup;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.Theming;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The first-run wizard. It collects the store's details, creates the administrator account, and
/// lets the owner choose the language and appearance before the application is used for the first
/// time. Backups are configured from the settings screen once the shop is running.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ICommandHandler<CompleteSetupCommand, Result> _completeSetup;
    private readonly IThemeStore _themeStore;
    private readonly IThemeService _themeService;
    private readonly LanguageService _languageService;
    private readonly IStrings _strings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    private int _step;

    [ObservableProperty]
    private string _storeName = string.Empty;

    [ObservableProperty]
    private string _storeNameAr = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private ThemeModeOption? _selectedModeOption;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    /// <summary>Creates the wizard.</summary>
    public FirstRunViewModel(
        ICommandHandler<CompleteSetupCommand, Result> completeSetup,
        IThemeStore themeStore,
        IThemeService themeService,
        LanguageService languageService,
        IStrings strings)
    {
        _completeSetup = completeSetup;
        _themeStore = themeStore;
        _themeService = themeService;
        _languageService = languageService;
        _strings = strings;

        Modes = ThemeModeOption.For(_strings);

        SelectedModeOption = Modes.FirstOrDefault(option => option.Value == _themeService.Current.Mode)
                             ?? Modes[0];

        SelectedLanguage = LanguageService.Available
            .FirstOrDefault(option => option.Code == _languageService.CurrentLanguage)
            ?? LanguageService.Available[0];

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Re-reads the labels after the language changes.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StoreNameLabel));
        OnPropertyChanged(nameof(AddressLabel));
        OnPropertyChanged(nameof(PhoneLabel));
        OnPropertyChanged(nameof(DisplayNameLabel));
        OnPropertyChanged(nameof(UsernameLabel));
        OnPropertyChanged(nameof(PasswordLabel));
        OnPropertyChanged(nameof(ConfirmPasswordLabel));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(BackLabel));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(FinishLabel));
    }

    /// <summary>The number of steps in the wizard.</summary>
    public const int StepCount = 3;

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.FirstRunTitle];

    /// <summary>The title of the current step.</summary>
    public string StepTitle => Step switch
    {
        0 => _strings[StringKeys.FirstRunStep1],
        1 => _strings[StringKeys.FirstRunStep2],
        _ => _strings[StringKeys.FirstRunStep3],
    };

    /// <summary>The label for the store name field.</summary>
    public string StoreNameLabel => _strings[StringKeys.FirstRunStoreName];

    /// <summary>The label for the address field.</summary>
    public string AddressLabel => _strings[StringKeys.FirstRunAddress];

    /// <summary>The label for the phone field.</summary>
    public string PhoneLabel => _strings[StringKeys.FirstRunPhone];

    /// <summary>The label for the display name field.</summary>
    public string DisplayNameLabel => _strings[StringKeys.FirstRunDisplayName];

    /// <summary>The label for the user name field.</summary>
    public string UsernameLabel => _strings[StringKeys.LoginUsername];

    /// <summary>The label for the password field.</summary>
    public string PasswordLabel => _strings[StringKeys.FirstRunPassword];

    /// <summary>The label for the confirm password field.</summary>
    public string ConfirmPasswordLabel => _strings[StringKeys.FirstRunConfirmPassword];

    /// <summary>The label for the language picker.</summary>
    public string LanguageLabel => _strings[StringKeys.FirstRunLanguage];

    /// <summary>The label for the appearance picker.</summary>
    public string ThemeLabel => _strings[StringKeys.FirstRunTheme];

    /// <summary>The label on the back command.</summary>
    public string BackLabel => _strings[StringKeys.CommonBack];

    /// <summary>The label on the next command.</summary>
    public string NextLabel => _strings[StringKeys.CommonNext];

    /// <summary>The label on the finish command.</summary>
    public string FinishLabel => _strings[StringKeys.CommonFinish];

    /// <summary>True when the wizard is on its first step.</summary>
    public bool IsFirstStep => Step == 0;

    /// <summary>True when the wizard is on its last step.</summary>
    public bool IsLastStep => Step == StepCount - 1;

    /// <summary>The available appearance modes.</summary>
    public IReadOnlyList<ThemeModeOption> Modes { get; }

    /// <summary>The appearance mode the owner picked.</summary>
    public ThemeMode SelectedMode => SelectedModeOption?.Value ?? ThemeMode.Light;

    /// <summary>True once the setup has been written and the wizard can close.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>The available languages.</summary>
    public static IReadOnlyList<LanguageOption> Languages => LanguageService.Available;

    /// <summary>Applies the language chosen on this step.</summary>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is not null)
        {
            _languageService.SetLanguage(value.Code);
        }
    }

    /// <summary>Moves to the previous step.</summary>
    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
            OnPropertyChanged(nameof(IsFirstStep));
            OnPropertyChanged(nameof(IsLastStep));
        }
    }

    /// <summary>Moves to the next step.</summary>
    [RelayCommand]
    private void Next()
    {
        if (Step < StepCount - 1)
        {
            Step++;
            OnPropertyChanged(nameof(IsFirstStep));
            OnPropertyChanged(nameof(IsLastStep));
        }
    }

    /// <summary>Creates the administrator account and finishes the setup.</summary>
    [RelayCommand]
    private async Task FinishAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = string.Empty;

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = _strings[StringKeys.FirstRunPasswordMismatch];
            return;
        }

        var details = new StoreDetails(
            StoreName.Trim(),
            string.IsNullOrWhiteSpace(StoreNameAr) ? null : StoreNameAr.Trim(),
            string.IsNullOrWhiteSpace(Address) ? null : Address.Trim(),
            string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
            TaxNumber: null,
            LogoPath: null);

        Result result = await _completeSetup.HandleAsync(
            new CompleteSetupCommand(
                Username.Trim(),
                Password,
                DisplayName.Trim(),
                details,
                StoreSettings.Defaults),
            cancellationToken);

        if (result.IsFailure)
        {
            ErrorMessage = result.Error.Message;
            return;
        }

        ThemeDefinition theme = _themeService.Current;
        theme.Mode = SelectedMode;
        theme.Locale.Default = _languageService.CurrentLanguage;
        theme.ProductName = new LocalizedText
        {
            En = string.IsNullOrWhiteSpace(StoreName) ? theme.ProductName.En : StoreName.Trim(),
            Ar = string.IsNullOrWhiteSpace(StoreNameAr) ? theme.ProductName.Ar : StoreNameAr.Trim(),
        };

        await _themeStore.SaveAsync(theme, cancellationToken);
        _themeService.Apply(theme);

        IsComplete = true;
        OnPropertyChanged(nameof(IsComplete));
    }
}
