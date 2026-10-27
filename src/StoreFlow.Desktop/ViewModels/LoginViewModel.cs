using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Application.Identity;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The sign-in screen. Authentication happens in the application layer, never in the view, and the
/// password never leaves this object.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly ICommandHandler<AuthenticateCommand, Result<UserSession>> _authenticate;
    private readonly IUserSession _session;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the sign-in screen.</summary>
    public LoginViewModel(
        ICommandHandler<AuthenticateCommand, Result<UserSession>> authenticate,
        IUserSession session,
        IStrings strings,
        IThemeService themeService,
        LanguageService languageService)
    {
        _authenticate = authenticate;
        _session = session;
        _strings = strings;
        _themeService = themeService;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Re-reads the labels after the language changes.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(UsernameLabel));
        OnPropertyChanged(nameof(PasswordLabel));
        OnPropertyChanged(nameof(SignInLabel));
        OnPropertyChanged(nameof(ProductNameFallback));
    }

    /// <summary>The product name, taken from the theme so a rebrand reaches the sign-in screen too.</summary>
    public string ProductNameFallback =>
        _themeService.Current.ProductName.Resolve(_languageService.CurrentLanguage);

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.LoginTitle];

    /// <summary>The label for the user name field.</summary>
    public string UsernameLabel => _strings[StringKeys.LoginUsername];

    /// <summary>The label for the password field.</summary>
    public string PasswordLabel => _strings[StringKeys.LoginPassword];

    /// <summary>The label on the sign-in command.</summary>
    public string SignInLabel => _strings[StringKeys.LoginSignIn];

    /// <summary>True when the form can be submitted.</summary>
    public bool CanSubmit => !IsBusy && !string.IsNullOrWhiteSpace(Username);

    /// <summary>True once the user has been signed in.</summary>
    public bool IsAuthenticated => _session.IsAuthenticated;

    /// <summary>Signs in, or reports why it could not.</summary>
    [RelayCommand]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = _strings[StringKeys.LoginInvalid];
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            Result<UserSession> result = await _authenticate.HandleAsync(
                new AuthenticateCommand(Username, Password),
                cancellationToken);

            if (result.IsSuccess)
            {
                _session.SignIn(result.Value);
                Password = string.Empty;
                OnPropertyChanged(nameof(IsAuthenticated));
                return;
            }

            ErrorMessage = result.Error.Code switch
            {
                "auth.account_disabled" => _strings[StringKeys.LoginDisabled],
                _ => _strings[StringKeys.LoginInvalid],
            };
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanSubmit));
        }
    }

    partial void OnUsernameChanged(string value) => OnPropertyChanged(nameof(CanSubmit));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));
}
