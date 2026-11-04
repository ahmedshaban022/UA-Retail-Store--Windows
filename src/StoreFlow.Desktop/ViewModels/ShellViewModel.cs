using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Updates;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>One entry in the navigation rail.</summary>
/// <param name="Key">The page identifier.</param>
/// <param name="Icon">A short glyph shown beside the label.</param>
/// <param name="Title">The localized title.</param>
/// <param name="IsAvailable">False for a page that this build does not yet provide.</param>
public sealed record ShellNavItem(string Key, string Icon, string Title, bool IsAvailable = true);

/// <summary>
/// The application shell: the navigation rail, the command bar, and the page currently in view.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;
    private readonly IUserSession _session;
    private readonly IThemeService _themeService;
    private readonly IUpdateService _updateService;

    private string _currentKey = "Dashboard";

    [ObservableProperty]
    private object? _currentContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTitle))]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _currentUserName = string.Empty;

    [ObservableProperty]
    private string _currentRoleName = string.Empty;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    /// <summary>True when a newer release is waiting to be installed.</summary>
    [ObservableProperty]
    private bool _isUpdateVisible;

    /// <summary>True while the update is being downloaded and started.</summary>
    [ObservableProperty]
    private bool _isUpdateBusy;

    /// <summary>What the shopkeeper is told about the update.</summary>
    [ObservableProperty]
    private string _updateMessage = string.Empty;

    /// <summary>How much of the update has been downloaded, as a percentage.</summary>
    [ObservableProperty]
    private int _updateProgress;

    /// <summary>Creates the shell.</summary>
    public ShellViewModel(
        IServiceProvider services,
        IStrings strings,
        LanguageService languageService,
        IUserSession session,
        IThemeService themeService,
        IUpdateService updateService)
    {
        _services = services;
        _strings = strings;
        _languageService = languageService;
        _session = session;
        _themeService = themeService;
        _updateService = updateService;

        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(languageService);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(updateService);

        _languageService.LanguageChanged += OnLanguageChanged;

        BuildNavigationItems();

        SelectedLanguage = LanguageService.Available
            .FirstOrDefault(option => option.Code == _languageService.CurrentLanguage)
            ?? LanguageService.Available[0];

        ApplySession();
    }

    /// <summary>
    /// Builds the navigation rail. The catalogue's screens are placed together, because they are
    /// used together; the screens that arrive with later phases stay in the rail, greyed out, so the
    /// shape of the application is visible from the start.
    /// </summary>
    private void BuildNavigationItems()
    {
        ShellNavItem[] items =
        [
            new("Dashboard", "▤", _strings[StringKeys.NavDashboard]),
            new("Products", "▦", _strings[StringKeys.NavProducts]),
            new("Categories", "▧", _strings[StringKeys.NavCategories]),
            new("Units", "◔", _strings[StringKeys.NavUnits]),
            new("Suppliers", "⇄", _strings[StringKeys.NavSuppliers]),
            new("Purchases", "▥", _strings[StringKeys.NavPurchases]),
            new("Inventory", "▣", _strings[StringKeys.NavInventory]),
            new("Pos", "▶", _strings[StringKeys.NavPos]),
            new("Sales", "≡", _strings[StringKeys.NavSales]),
            new("Reports", "◫", _strings[StringKeys.NavReports]),
            new("Approvals", "✓", _strings[StringKeys.NavApprovals]),
            new("Users", "◉", _strings[StringKeys.NavUsers]),
            new("Settings", "⚙", _strings[StringKeys.NavSettings]),
            new("Backup", "⛁", _strings[StringKeys.NavBackup]),
            new("Audit", "✎", _strings[StringKeys.NavAudit]),
        ];

        for (int index = 0; index < items.Length; index++)
        {
            if (index < NavigationItems.Count)
            {
                NavigationItems[index] = items[index];
            }
            else
            {
                NavigationItems.Add(items[index]);
            }
        }
    }

    /// <summary>The entries in the navigation rail.</summary>
    public ObservableCollection<ShellNavItem> NavigationItems { get; } = [];

    /// <summary>The title of the current page.</summary>
    public string CurrentTitle { get => PageTitle; private set => PageTitle = value; }

    /// <summary>The label on the sign-out command.</summary>
    public string SignOutText => _strings[StringKeys.SessionSignOut];

    /// <summary>The product name, taken from the theme so a rebrand reaches the title bar too.</summary>
    public string ProductName =>
        _themeService.Current.ProductName.Resolve(_languageService.CurrentLanguage);

    /// <summary>The label on the lock command.</summary>
    public string LockText => _strings[StringKeys.SessionLock];

    /// <summary>The available languages.</summary>
    public static IReadOnlyList<LanguageOption> Languages => LanguageService.Available;

    /// <summary>Navigates to a page by its key.</summary>
    [RelayCommand]
    private void Navigate(string key)
    {
        object? content = key switch
        {
            "Dashboard" => _services.GetService(typeof(DashboardViewModel)),
            "Products" => _services.GetService(typeof(ProductsViewModel)),
            "Categories" => _services.GetService(typeof(CategoriesViewModel)),
            "Units" => _services.GetService(typeof(UnitsViewModel)),
            "Suppliers" => _services.GetService(typeof(SuppliersViewModel)),
            "Audit" => _services.GetService(typeof(AuditViewModel)),
            "Purchases" => _services.GetService(typeof(PurchasesViewModel)),
            "Inventory" => _services.GetService(typeof(InventoryViewModel)),
            "Pos" => _services.GetService(typeof(PosViewModel)),
            "Sales" => _services.GetService(typeof(SalesViewModel)),
            "Reports" => _services.GetService(typeof(ReportsViewModel)),
            "Approvals" => _services.GetService(typeof(ApprovalsViewModel)),
            "Users" => _services.GetService(typeof(UsersViewModel)),
            "Settings" => _services.GetService(typeof(SettingsViewModel)),
            "Backup" => _services.GetService(typeof(BackupViewModel)),
            _ => null,
        };

        if (content is null)
        {
            return;
        }

        CurrentContent = content;
        _currentKey = key;
        CurrentTitle = TitleFor(key);

        switch (content)
        {
            case DashboardViewModel dashboard:
                dashboard.RefreshCommand.Execute(null);
                break;
            case ProductsViewModel products:
                products.RefreshCommand.Execute(null);
                break;
            case CategoriesViewModel categories:
                categories.RefreshCommand.Execute(null);
                break;
            case UnitsViewModel units:
                units.RefreshCommand.Execute(null);
                break;
            case SuppliersViewModel suppliers:
                suppliers.RefreshCommand.Execute(null);
                break;
            case AuditViewModel audit:
                audit.RefreshCommand.Execute(null);
                break;
            case PurchasesViewModel purchases:
                purchases.RefreshCommand.Execute(null);
                break;
            case InventoryViewModel inventory:
                inventory.RefreshCommand.Execute(null);
                break;
            case PosViewModel pos:
                pos.RefreshHeldCartsCommand.Execute(null);
                break;
            case SalesViewModel sales:
                sales.RefreshCommand.Execute(null);
                break;
            case ApprovalsViewModel approvals:
                approvals.RefreshCommand.Execute(null);
                break;
            case UsersViewModel users:
                users.RefreshCommand.Execute(null);
                break;
            case BackupViewModel backup:
                backup.RefreshCommand.Execute(null);
                break;
        }
    }

    /// <summary>The title of a page, in the language the application is running in.</summary>
    private string TitleFor(string key) => key switch
    {
        "Dashboard" => _strings[StringKeys.DashboardTitle],
        "Products" => _strings[StringKeys.ProductsTitle],
        "Categories" => _strings[StringKeys.CategoriesTitle],
        "Units" => _strings[StringKeys.UnitsTitle],
        "Suppliers" => _strings[StringKeys.SuppliersTitle],
        "Audit" => _strings[StringKeys.AuditTitle],
        "Purchases" => _strings[StringKeys.PurchasesTitle],
        "Inventory" => _strings[StringKeys.NavInventory],
        "Pos" => _strings[StringKeys.PosTitle],
        "Sales" => _strings[StringKeys.SalesTitle],
        "Reports" => _strings[StringKeys.ReportsTitle],
        "Approvals" => _strings[StringKeys.ApprovalsTitle],
        "Users" => _strings[StringKeys.UsersTitle],
        "Backup" => _strings[StringKeys.BackupTitle],
        _ => _strings[StringKeys.NavSettings],
    };

    /// <summary>Applies the language chosen in the command bar.</summary>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is not null)
        {
            _languageService.SetLanguage(value.Code);
        }
    }

    /// <summary>Refreshes the signed-in user's name and role.</summary>
    public void ApplySession()
    {
        UserSession? current = _session.Current;

        CurrentUserName = current?.DisplayName ?? string.Empty;
        CurrentRoleName = current?.Username ?? string.Empty;
    }

    /// <summary>The label on the update command.</summary>
    public string UpdateNowLabel => _strings[StringKeys.UpdateNow];

    /// <summary>The label on the command that puts the update off.</summary>
    public string UpdateLaterLabel => _strings[StringKeys.UpdateLater];

    /// <summary>
    /// Looks for a newer release. The check runs in the background and the shop's programme never
    /// waits for it: a shop that is offline simply sees no banner.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        try
        {
            UpdateCheckResult result = await _updateService.CheckAsync(CancellationToken.None);

            if (!result.HasUpdate)
            {
                return;
            }

            UpdateMessage = _strings.Format(
                StringKeys.UpdateAvailable,
                result.LatestVersion ?? string.Empty);

            IsUpdateVisible = true;
        }
#pragma warning disable CA1031 // A failed update check must never take the shop's programme down.
        catch (Exception ex)
        {
            // The update is optional. Whatever went wrong, the shop carries on.
            StartupDiagnostics.Write("The update check could not be made.", ex);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Downloads the newer release, checks it, and starts its installer. The programme closes
    /// afterwards, because the installer cannot replace files that are still open.
    /// </summary>
    [RelayCommand]
    private async Task UpdateNowAsync(CancellationToken cancellationToken)
    {
        if (IsUpdateBusy)
        {
            return;
        }

        IsUpdateBusy = true;

        try
        {
            UpdateCheckResult update = await _updateService.CheckAsync(cancellationToken);

            if (!update.HasUpdate)
            {
                UpdateMessage = _strings[StringKeys.UpdateUpToDate];
                IsUpdateVisible = false;

                return;
            }

            UpdateProgress = 0;
            UpdateMessage = _strings.Format(StringKeys.UpdateDownloading, 0);

            var progress = new Progress<int>(percent =>
            {
                UpdateProgress = percent;
                UpdateMessage = _strings.Format(StringKeys.UpdateDownloading, percent);
            });

            UpdateInstallResult result = await _updateService
                .InstallAsync(update, progress, cancellationToken);

            if (!result.Started)
            {
                StartupDiagnostics.Write("The update could not be installed: " + result.Reason);

                UpdateMessage = _strings[StringKeys.UpdateFailed];

                return;
            }

            UpdateMessage = _strings[StringKeys.UpdateInstalling];

            // The installer cannot replace the programme's files while they are open, so the
            // programme closes and the installer takes over.
            System.Windows.Application.Current?.Shutdown();
        }
        catch (OperationCanceledException)
        {
            UpdateMessage = _strings[StringKeys.UpdateFailed];
        }
#pragma warning disable CA1031 // A failed update must never take the shop's programme down.
        catch (Exception ex)
        {
            StartupDiagnostics.Write("The update could not be installed.", ex);

            UpdateMessage = _strings[StringKeys.UpdateFailed];
        }
#pragma warning restore CA1031
        finally
        {
            IsUpdateBusy = false;
        }
    }

    /// <summary>Puts the update off. The shop can ask again from the settings screen.</summary>
    [RelayCommand]
    private void Later()
    {
        IsUpdateVisible = false;
    }

    /// <summary>Signs the user out and closes the shell.</summary>
    [RelayCommand]
    private void SignOut()
    {
        _session.SignOut();
        System.Windows.Application.Current?.Shutdown();
    }

    /// <summary>Locks the session.</summary>
    [RelayCommand]
    private void Lock()
    {
        _session.Lock();
        System.Windows.Application.Current?.Shutdown();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _languageService.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        BuildNavigationItems();

        CurrentTitle = TitleFor(_currentKey);

        OnPropertyChanged(nameof(SignOutText));
        OnPropertyChanged(nameof(LockText));
        OnPropertyChanged(nameof(ProductName));
    }
}
