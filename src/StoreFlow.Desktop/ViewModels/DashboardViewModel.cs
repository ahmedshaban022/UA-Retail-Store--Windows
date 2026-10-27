using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Reporting;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The dashboard. It reads the posted ledger, so every figure on it is one the owner can reconcile
/// against the invoice history.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardReader _dashboardReader;
    private readonly IStrings _strings;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _todaySales = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private int _todayInvoiceCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private int _lowStockCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private int _productCount;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Creates the dashboard.</summary>
    public DashboardViewModel(
        IDashboardReader dashboardReader,
        IStrings strings,
        IThemeService themeService)
    {
        _dashboardReader = dashboardReader;
        _strings = strings;
        _themeService = themeService;
    }

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.DashboardTitle];

    /// <summary>The greeting shown above the figures.</summary>
    public string WelcomeText => _strings[StringKeys.DashboardWelcome];

    /// <summary>The label for today's sales.</summary>
    public string TodaySalesLabel => _strings[StringKeys.DashboardTodaySales];

    /// <summary>The label for the invoice count.</summary>
    public string InvoicesLabel => _strings[StringKeys.DashboardInvoices];

    /// <summary>The label for the low stock count.</summary>
    public string LowStockLabel => _strings[StringKeys.DashboardLowStock];

    /// <summary>The empty state shown when nothing has been sold yet.</summary>
    public string EmptyStateText => _strings[StringKeys.DashboardNoSalesYet];

    /// <summary>True when nothing has been sold today.</summary>
    public bool HasNoSales => TodayInvoiceCount == 0;

    /// <summary>Formats a monetary amount in the shop's currency.</summary>
    public string FormatMoney(Domain.Money.Money amount) => _themeService.FormatMoney(amount);

    /// <summary>Reloads the figures.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;

        try
        {
            DashboardSnapshot snapshot = await _dashboardReader.GetTodayAsync(cancellationToken);

            TodaySales = FormatMoney(snapshot.TodaySales);
            TodayInvoiceCount = snapshot.TodayInvoiceCount;
            LowStockCount = snapshot.LowStockCount;
            ProductCount = snapshot.ProductCount;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoSales));
        }
    }
}
