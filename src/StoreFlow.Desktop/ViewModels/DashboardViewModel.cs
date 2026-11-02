using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Reporting;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The dashboard. It reads the posted ledger, so every figure on it is one the owner can reconcile
/// against the invoice history. The money comes from the same reader as the daily sales report,
/// so the two screens cannot disagree about the same day.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardReader _dashboardReader;
    private readonly IStrings _strings;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _netSales = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _grossProfit = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _returns = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _stockValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private int _invoiceCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private int _lowStockCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private int _productCount;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Creates the dashboard.</summary>
    /// <param name="dashboardReader">Reads the figures.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="themeService">The shop's theme, for the money.</param>
    public DashboardViewModel(
        IDashboardReader dashboardReader,
        IStrings strings,
        IThemeService themeService)
    {
        _dashboardReader = dashboardReader;
        _strings = strings;
        _themeService = themeService;

        DateText = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.DashboardTitle];

    /// <summary>The greeting shown above the figures.</summary>
    public string WelcomeText => _strings[StringKeys.DashboardWelcome];

    /// <summary>The label for the day's takings.</summary>
    public string NetSalesLabel => _strings[StringKeys.DashboardNetSales];

    /// <summary>The label for the gross profit.</summary>
    public string GrossProfitLabel => _strings[StringKeys.DashboardGrossProfit];

    /// <summary>The label for the returns.</summary>
    public string ReturnsLabel => _strings[StringKeys.DashboardReturns];

    /// <summary>The label for the stock value.</summary>
    public string StockValueLabel => _strings[StringKeys.DashboardStockValue];

    /// <summary>The label for the invoice count.</summary>
    public string InvoicesLabel => _strings[StringKeys.DashboardInvoices];

    /// <summary>The label for the low stock count.</summary>
    public string LowStockLabel => _strings[StringKeys.DashboardLowStock];

    /// <summary>The label for the day the figures belong to.</summary>
    public string DateLabel => _strings[StringKeys.DashboardToday];

    /// <summary>The label of the button that reads the figures again.</summary>
    public string RefreshLabel => _strings[StringKeys.DashboardRefresh];

    /// <summary>The empty state shown when nothing has been sold yet.</summary>
    public string EmptyStateText => _strings[StringKeys.DashboardNoSalesYet];

    /// <summary>True when nothing has been sold on the chosen day.</summary>
    public bool HasNoSales => InvoiceCount == 0;

    /// <summary>Formats a monetary amount in the shop's currency.</summary>
    public string FormatMoney(Domain.Money.Money amount) => _themeService.FormatMoney(amount);

    /// <summary>Reloads the figures.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(DateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            date = DateOnly.FromDateTime(DateTime.Now);
        }

        IsLoading = true;

        try
        {
            DashboardSnapshot snapshot = await _dashboardReader.GetAsync(date, cancellationToken);

            NetSales = FormatMoney(snapshot.NetSales);
            GrossProfit = FormatMoney(snapshot.GrossProfit);
            Returns = FormatMoney(snapshot.Returns);
            StockValue = FormatMoney(snapshot.StockValue);
            InvoiceCount = snapshot.InvoiceCount;
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
