using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Application.Reporting;
using StoreFlow.Application.Sales;
using StoreFlow.Desktop.Localization;
using StoreFlow.Domain.Money;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>One report the screen can show.</summary>
/// <param name="Kind">The report.</param>
/// <param name="DisplayName">The name to show.</param>
public sealed record ReportKindOption(ReportKind Kind, string DisplayName)
{
    /// <summary>
    /// The name a select shows for this entry. Without this override the closed control would show
    /// the record's generated text, which reads as the report's internals instead of its name.
    /// </summary>
    public override string ToString() => DisplayName;
}

/// <summary>How the value of a column is written.</summary>
public enum ReportColumnKind
{
    /// <summary>Plain text.</summary>
    Text = 0,

    /// <summary>A monetary amount, written in the shop's currency.</summary>
    Money = 1,

    /// <summary>A day.</summary>
    Date = 2,

    /// <summary>A moment in time.</summary>
    Timestamp = 3,
}

/// <summary>One column of the report on screen.</summary>
/// <param name="PropertyName">The property of the row the column reads.</param>
/// <param name="Header">The heading of the column.</param>
/// <param name="Kind">How the value is written.</param>
public sealed record ReportColumnView(string PropertyName, string Header, ReportColumnKind Kind);

/// <summary>
/// The reports screen. It reads the figures the reports work out and shows them; it works out
/// nothing of its own, so the screen and the exported file cannot tell the shop two different
/// stories (PRD §5.8).
/// </summary>
public sealed partial class ReportsViewModel : ObservableObject
{
    /// <summary>
    /// The most rows of the stock ledger the screen shows at once. The ledger is the one report
    /// that can hold more rows than a screen can show, and it is the one report the shop may
    /// want in full, so the screen is bounded and the exported file is not.
    /// </summary>
    private const int ScreenRowLimit = 5000;

    private readonly IReportReader _reader;
    private readonly IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryView>>> _auditQuery;
    private readonly IQueryHandler<ListSalesCashiersQuery, Result<IReadOnlyList<SalesCashierOption>>> _cashiersQuery;
    private readonly ICommandHandler<RecordDrawerCashCommand, Result> _recordDrawerCash;
    private readonly IExportService _exportService;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    private IReadOnlyList<object> _rows = [];
    private ReportFilter? _lastFilter;
    private int _totalRowCount;
    private string _truncatedNote = string.Empty;

    [ObservableProperty]
    private ReportKindOption? _selectedKind;

    [ObservableProperty]
    private SalesCashierOption? _selectedCashier;

    [ObservableProperty]
    private string _fromText = string.Empty;

    [ObservableProperty]
    private string _toText = string.Empty;

    [ObservableProperty]
    private string _drawerOpeningText = string.Empty;

    [ObservableProperty]
    private string _drawerCountedText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the screen.</summary>
    /// <param name="reader">Reads the reports.</param>
    /// <param name="auditQuery">Reads the audit trail, which is one of the reports.</param>
    /// <param name="cashiersQuery">Reads the cashiers, for the filter and for the drawer count.</param>
    /// <param name="recordDrawerCash">Records what was counted in the drawer.</param>
    /// <param name="exportService">Writes the exported file.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public ReportsViewModel(
        IReportReader reader,
        IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryView>>> auditQuery,
        IQueryHandler<ListSalesCashiersQuery, Result<IReadOnlyList<SalesCashierOption>>> cashiersQuery,
        ICommandHandler<RecordDrawerCashCommand, Result> recordDrawerCash,
        IExportService exportService,
        IStrings strings,
        LanguageService languageService)
    {
        _reader = reader;
        _auditQuery = auditQuery;
        _cashiersQuery = cashiersQuery;
        _recordDrawerCash = recordDrawerCash;
        _exportService = exportService;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;

        BuildKinds();

        SelectedKind = Kinds[0];

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);

        FromText = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ToText = FromText;
    }

    /// <summary>The reports the screen can show.</summary>
    public ObservableCollection<ReportKindOption> Kinds { get; } = [];

    /// <summary>The cashiers the reports can be narrowed to.</summary>
    public ObservableCollection<SalesCashierOption> Cashiers { get; } = [];

    /// <summary>The columns of the report on screen.</summary>
    public ObservableCollection<ReportColumnView> Columns { get; } = [];

    /// <summary>The rows of the report on screen.</summary>
    public IReadOnlyList<object> Rows => _rows;

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.ReportsTitle];

    /// <summary>The label of the report picker.</summary>
    public string KindLabel => _strings[StringKeys.ReportsKind];

    /// <summary>The label of the first date field.</summary>
    public string FromLabel => _strings[StringKeys.ReportsFrom];

    /// <summary>The label of the second date field.</summary>
    public string ToLabel => _strings[StringKeys.ReportsTo];

    /// <summary>The label of the button that runs the report.</summary>
    public string RunLabel => _strings[StringKeys.ReportsRun];

    /// <summary>The label of the button that writes the file.</summary>
    public string ExportLabel => _strings[StringKeys.ReportsExport];

    /// <summary>The label of the cashier filter.</summary>
    public string CashierLabel => _strings[StringKeys.ReportsCashier];

    /// <summary>The label of the field for the cash the drawer held at the start of the day.</summary>
    public string DrawerOpeningLabel => _strings[StringKeys.ReportsDrawerOpening];

    /// <summary>The label of the field for the cash the drawer was counted to hold.</summary>
    public string DrawerCountedLabel => _strings[StringKeys.ReportsDrawerCounted];

    /// <summary>The label of the button that records what was counted in the drawer.</summary>
    public string RecordDrawerLabel => _strings[StringKeys.ReportsRecordDrawer];

    /// <summary>The text shown when the report has nothing to say.</summary>
    public string EmptyText => _strings[StringKeys.ReportsEmpty];

    /// <summary>True when the report has nothing to show.</summary>
    public bool IsEmpty => !IsBusy && _rows.Count == 0;

    /// <summary>Runs the report that is chosen.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (SelectedKind is not ReportKindOption kind)
        {
            return;
        }

        if (!TryReadDates(out DateOnly from, out DateOnly to))
        {
            StatusMessage = _strings[StringKeys.ReportsInvalidDate];
            return;
        }

        IsBusy = true;

        try
        {
            await LoadCashiersAsync(cancellationToken);

            ReportFilter filter = new()
            {
                From = from,
                To = to,
                CashierUserId = SelectedCashier?.Id,
            };

            _lastFilter = filter;
            _totalRowCount = 0;
            _truncatedNote = string.Empty;

            if (kind.Kind == ReportKind.AuditTrail)
            {
                await RunAuditAsync(filter, cancellationToken);
            }
            else
            {
                await RunFiguresAsync(kind.Kind, filter, cancellationToken);
            }

            StatusMessage = _truncatedNote;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Rows));
        }
    }

    /// <summary>Writes the report that is on screen to a CSV file.</summary>
    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (SelectedKind is not ReportKindOption kind || _rows.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = kind.Kind.ToString().ToLowerInvariant() + ".csv",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var columns = new List<ExportColumn>();

        foreach (ReportColumnView column in Columns)
        {
            columns.Add(new ExportColumn(column.PropertyName, column.Header));
        }

        IReadOnlyList<object> source = _rows;

        // A report that was cut short on screen is read again in full, so that the file the
        // shop takes away is the whole report and not the part that fitted on the screen.
        if (_totalRowCount > _rows.Count
            && SelectedKind is ReportKindOption { Kind: ReportKind.StockMovements }
            && _lastFilter is not null)
        {
            StockMovementsReport full = await _reader.ReadStockMovementsAsync(
                _lastFilter with { RowLimit = null },
                cancellationToken);

            source = full.Rows.Cast<object>().ToList();
        }

        var rows = new List<IReadOnlyList<object?>>(source.Count);

        foreach (object row in source)
        {
            var values = new List<object?>(columns.Count);

            foreach (ReportColumnView column in Columns)
            {
                values.Add(ValueOf(row, column.PropertyName));
            }

            rows.Add(values);
        }

        var table = new ExportTable(columns, rows);

        Result written = await _exportService.WriteCsvAsync(dialog.FileName, table, cancellationToken);

        StatusMessage = written.IsFailure
            ? written.Error.Message
            : _strings.Format(StringKeys.ReportsExported, dialog.FileName);
    }

    /// <summary>Runs a report that is worked out from the documents.</summary>
    private async Task RunFiguresAsync(ReportKind kind, ReportFilter filter, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case ReportKind.DailySales:
                IReadOnlyList<DailySalesRow> daily = await _reader.ReadDailySalesAsync(filter, cancellationToken);
                SetRows(daily, DailySalesColumns());
                break;

            case ReportKind.SalesByProduct:
                IReadOnlyList<SalesByProductRow> byProduct = await _reader.ReadSalesByProductAsync(filter, cancellationToken);
                SetRows(byProduct, SalesByProductColumns());
                break;

            case ReportKind.BestSellers:
                IReadOnlyList<BestSellerRow> best = await _reader.ReadBestSellersAsync(filter, cancellationToken);
                SetRows(best, BestSellerColumns());
                break;

            case ReportKind.StockOnHand:
                IReadOnlyList<StockOnHandRow> onHand = await _reader.ReadStockOnHandAsync(filter, cancellationToken);
                SetRows(onHand, StockOnHandColumns());
                break;

            case ReportKind.LowOrOutOfStock:
                IReadOnlyList<StockOnHandRow> low = await _reader.ReadStockOnHandAsync(
                    filter with { StockStatus = StockStatusFilter.BelowReorder },
                    cancellationToken);
                SetRows(low, StockOnHandColumns());
                break;

            case ReportKind.StockValuation:
                IReadOnlyList<StockValuationRow> valuation = await _reader.ReadStockValuationAsync(filter, cancellationToken);
                SetRows(valuation, StockValuationColumns());
                break;

            case ReportKind.StockMovements:
                // The ledger is the one report that can have more rows than a screen can show, so
                // it is read with a limit. The shop is told how many there were in all, and the
                // file it exports is the whole report rather than the part on the screen.
                StockMovementsReport movements = await _reader.ReadStockMovementsAsync(
                    filter with { RowLimit = ScreenRowLimit },
                    cancellationToken);

                SetRows(movements.Rows, StockMovementColumns());

                _totalRowCount = movements.TotalCount;

                if (movements.IsTruncated)
                {
                    _truncatedNote = _strings.Format(
                        StringKeys.ReportsTruncated,
                        movements.Rows.Count,
                        movements.TotalCount);
                }

                break;

            case ReportKind.PurchasesBySupplier:
                IReadOnlyList<PurchaseBySupplierRow> purchases = await _reader.ReadPurchasesBySupplierAsync(filter, cancellationToken);
                SetRows(purchases, PurchaseColumns());
                break;

            case ReportKind.ProfitAndLoss:
                ProfitAndLossReport report = await _reader.ReadProfitAndLossAsync(filter, cancellationToken);
                SetRows(report.Lines, ProfitAndLossColumns());
                break;

            case ReportKind.CashierPerformance:
                IReadOnlyList<CashierPerformanceRow> cashiers = await _reader.ReadCashierPerformanceAsync(filter, cancellationToken);
                SetRows(cashiers, CashierColumns());
                break;

            case ReportKind.Returns:
                IReadOnlyList<ReturnsRow> returns = await _reader.ReadReturnsAsync(filter, cancellationToken);
                SetRows(returns, ReturnsColumns());
                break;

            case ReportKind.CashierShift:
                IReadOnlyList<CashierShiftRow> shifts = await _reader.ReadCashierShiftsAsync(filter, cancellationToken);
                SetRows(shifts, CashierShiftColumns());
                break;
        }
    }

    /// <summary>Loads the cashiers the reports can be narrowed to.</summary>
    private async Task LoadCashiersAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<SalesCashierOption>> result = await _cashiersQuery.HandleAsync(
            new ListSalesCashiersQuery(),
            cancellationToken);

        if (result.IsFailure)
        {
            return;
        }

        Cashiers.Clear();

        foreach (SalesCashierOption cashier in result.Value)
        {
            Cashiers.Add(cashier);
        }
    }

    /// <summary>
    /// Records what was counted in the cash drawer, so that the day's cash can be reconciled.
    /// </summary>
    [RelayCommand]
    private async Task RecordDrawerAsync(CancellationToken cancellationToken)
    {
        int cashierUserId = SelectedCashier?.Id ?? 0;

        if (cashierUserId == 0)
        {
            StatusMessage = _strings[StringKeys.ReportsNoCashier];
            return;
        }

        if (!TryReadAmount(DrawerOpeningText, out Money? opening) ||
            !TryReadAmount(DrawerCountedText, out Money? counted))
        {
            StatusMessage = _strings[StringKeys.ReportsInvalidAmount];
            return;
        }

        Result recorded = await _recordDrawerCash.HandleAsync(
            new RecordDrawerCashCommand(
                ReadDate(FromText) ?? DateOnly.FromDateTime(DateTime.Now),
                cashierUserId,
                opening,
                counted),
            cancellationToken);

        StatusMessage = recorded.IsFailure
            ? recorded.Error.Message
            : _strings[StringKeys.ReportsDrawerRecorded];

        if (recorded.IsSuccess && SelectedKind?.Kind == ReportKind.CashierShift)
        {
            await RunAsync(cancellationToken);
        }
    }

    /// <summary>Reads an amount out of a field.</summary>
    private static bool TryReadAmount(string text, out Money? amount)
    {
        amount = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
        {
            return false;
        }

        amount = new Money(value);

        return true;
    }

    /// <summary>Reads a day out of a field.</summary>
    private static DateOnly? ReadDate(string text) =>
        DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? day
            : null;

    /// <summary>Runs the audit trail, which is read from the trail itself.</summary>
    private async Task RunAuditAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AuditEntryView>> result = await _auditQuery.HandleAsync(
            new ListAuditEntriesQuery(null, null, 1000),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = result.Error.Message;
            SetRows(Array.Empty<AuditEntryView>(), AuditColumns());
            return;
        }

        SetRows(result.Value, AuditColumns());
    }

    /// <summary>Puts the rows and the columns of a report on screen.</summary>
    private void SetRows<T>(IReadOnlyList<T> rows, IReadOnlyList<ReportColumnView> columns)
    {
        Columns.Clear();

        foreach (ReportColumnView column in columns)
        {
            Columns.Add(column);
        }

        var objects = new List<object>(rows.Count);

        foreach (T row in rows)
        {
            objects.Add(row!);
        }

        _rows = objects;

        OnPropertyChanged(nameof(Rows));
    }

    /// <summary>Reads the value of a property of a row.</summary>
    private static object? ValueOf(object row, string propertyName)
    {
        PropertyInfo? property = row.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        return property?.GetValue(row);
    }

    /// <summary>Reads the two dates of the period.</summary>
    private bool TryReadDates(out DateOnly from, out DateOnly to)
    {
        from = default;
        to = default;

        if (!DateOnly.TryParse(FromText, CultureInfo.InvariantCulture, DateTimeStyles.None, out from))
        {
            return false;
        }

        if (!DateOnly.TryParse(ToText, CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
        {
            return false;
        }

        if (to < from)
        {
            (from, to) = (to, from);
        }

        return true;
    }

    /// <summary>Builds the list of reports the screen can show.</summary>
    private void BuildKinds()
    {
        Kinds.Clear();
        Kinds.Add(new ReportKindOption(ReportKind.DailySales, _strings[StringKeys.ReportsNameDailySales]));
        Kinds.Add(new ReportKindOption(ReportKind.SalesByProduct, _strings[StringKeys.ReportsNameSalesByProduct]));
        Kinds.Add(new ReportKindOption(ReportKind.BestSellers, _strings[StringKeys.ReportsNameBestSellers]));
        Kinds.Add(new ReportKindOption(ReportKind.StockOnHand, _strings[StringKeys.ReportsNameStockOnHand]));
        Kinds.Add(new ReportKindOption(ReportKind.LowOrOutOfStock, _strings[StringKeys.ReportsNameLowOrOutOfStock]));
        Kinds.Add(new ReportKindOption(ReportKind.StockValuation, _strings[StringKeys.ReportsNameStockValuation]));
        Kinds.Add(new ReportKindOption(ReportKind.StockMovements, _strings[StringKeys.ReportsNameStockMovements]));
        Kinds.Add(new ReportKindOption(ReportKind.PurchasesBySupplier, _strings[StringKeys.ReportsNamePurchasesBySupplier]));
        Kinds.Add(new ReportKindOption(ReportKind.ProfitAndLoss, _strings[StringKeys.ReportsNameProfitAndLoss]));
        Kinds.Add(new ReportKindOption(ReportKind.Returns, _strings[StringKeys.ReportsNameReturns]));
        Kinds.Add(new ReportKindOption(ReportKind.CashierPerformance, _strings[StringKeys.ReportsNameCashierPerformance]));
        Kinds.Add(new ReportKindOption(ReportKind.CashierShift, _strings[StringKeys.ReportsNameCashierShift]));
        Kinds.Add(new ReportKindOption(ReportKind.AuditTrail, _strings[StringKeys.ReportsNameAuditTrail]));
    }

    private IReadOnlyList<ReportColumnView> DailySalesColumns() =>
    [
        Column(nameof(DailySalesRow.Date), StringKeys.ReportsColumnDate),
        Column(nameof(DailySalesRow.InvoiceCount), StringKeys.ReportsColumnInvoices),
        Column(nameof(DailySalesRow.GrossSales), StringKeys.ReportsColumnGrossSales),
        Column(nameof(DailySalesRow.Discounts), StringKeys.ReportsColumnDiscounts),
        Column(nameof(DailySalesRow.Tax), StringKeys.ReportsColumnTax),
        Column(nameof(DailySalesRow.Returns), StringKeys.ReportsColumnReturns),
        Column(nameof(DailySalesRow.NetSales), StringKeys.ReportsColumnNetSales),
    ];

    private IReadOnlyList<ReportColumnView> SalesByProductColumns() =>
    [
        Column(nameof(SalesByProductRow.Sku), StringKeys.ReportsColumnSku),
        Column(nameof(SalesByProductRow.Name), StringKeys.ReportsColumnProduct),
        Column(nameof(SalesByProductRow.Quantity), StringKeys.ReportsColumnQuantity),
        Column(nameof(SalesByProductRow.NetRevenue), StringKeys.ReportsColumnRevenue),
        Column(nameof(SalesByProductRow.CostOfGoodsSold), StringKeys.ReportsColumnCogs),
        Column(nameof(SalesByProductRow.GrossProfit), StringKeys.ReportsColumnProfit),
    ];

    private IReadOnlyList<ReportColumnView> BestSellerColumns() =>
    [
        Column(nameof(BestSellerRow.Rank), StringKeys.ReportsColumnRank),
        Column(nameof(BestSellerRow.Sku), StringKeys.ReportsColumnSku),
        Column(nameof(BestSellerRow.Name), StringKeys.ReportsColumnProduct),
        Column(nameof(BestSellerRow.Quantity), StringKeys.ReportsColumnQuantity),
        Column(nameof(BestSellerRow.NetRevenue), StringKeys.ReportsColumnRevenue),
    ];

    private IReadOnlyList<ReportColumnView> StockOnHandColumns() =>
    [
        Column(nameof(StockOnHandRow.Sku), StringKeys.ReportsColumnSku),
        Column(nameof(StockOnHandRow.Name), StringKeys.ReportsColumnProduct),
        Column(nameof(StockOnHandRow.Category), StringKeys.ReportsColumnCategory),
        Column(nameof(StockOnHandRow.Quantity), StringKeys.ReportsColumnQuantity),
        Column(nameof(StockOnHandRow.ReorderLevel), StringKeys.ReportsColumnReorderLevel),
        Column(nameof(StockOnHandRow.AverageUnitCost), StringKeys.ReportsColumnAverageCost),
        Column(nameof(StockOnHandRow.Value), StringKeys.ReportsColumnValue),
    ];

    private IReadOnlyList<ReportColumnView> StockValuationColumns() =>
    [
        Column(nameof(StockValuationRow.Sku), StringKeys.ReportsColumnSku),
        Column(nameof(StockValuationRow.Name), StringKeys.ReportsColumnProduct),
        Column(nameof(StockValuationRow.Quantity), StringKeys.ReportsColumnQuantity),
        Column(nameof(StockValuationRow.AverageUnitCost), StringKeys.ReportsColumnAverageCost),
        Column(nameof(StockValuationRow.Value), StringKeys.ReportsColumnValue),
    ];

    private IReadOnlyList<ReportColumnView> StockMovementColumns() =>
    [
        Column(nameof(StockMovementRow.Date), StringKeys.ReportsColumnDate),
        Column(nameof(StockMovementRow.Sku), StringKeys.ReportsColumnSku),
        Column(nameof(StockMovementRow.Name), StringKeys.ReportsColumnProduct),
        Column(nameof(StockMovementRow.Movement), StringKeys.ReportsColumnMovement),
        Column(nameof(StockMovementRow.Disposition), StringKeys.ReportsColumnDisposition),
        Column(nameof(StockMovementRow.Quantity), StringKeys.ReportsColumnQuantity),
        Column(nameof(StockMovementRow.UnitCost), StringKeys.ReportsColumnUnitCost),
        Column(nameof(StockMovementRow.Balance), StringKeys.ReportsColumnBalance),
        Column(nameof(StockMovementRow.Reason), StringKeys.ReportsColumnReason),
    ];

    private IReadOnlyList<ReportColumnView> PurchaseColumns() =>
    [
        Column(nameof(PurchaseBySupplierRow.Supplier), StringKeys.ReportsColumnSupplier),
        Column(nameof(PurchaseBySupplierRow.DocumentCount), StringKeys.ReportsColumnDocuments),
        Column(nameof(PurchaseBySupplierRow.Subtotal), StringKeys.ReportsColumnSubtotal),
        Column(nameof(PurchaseBySupplierRow.Discount), StringKeys.ReportsColumnDiscounts),
        Column(nameof(PurchaseBySupplierRow.Tax), StringKeys.ReportsColumnTax),
        Column(nameof(PurchaseBySupplierRow.Total), StringKeys.ReportsColumnTotal),
    ];

    private IReadOnlyList<ReportColumnView> ProfitAndLossColumns() =>
    [
        Column(nameof(ProfitAndLossLine.Key), StringKeys.ReportsColumnProduct),
        Column(nameof(ProfitAndLossLine.Amount), StringKeys.ReportsColumnTotal),
    ];

    private IReadOnlyList<ReportColumnView> CashierColumns() =>
    [
        Column(nameof(CashierPerformanceRow.DisplayName), StringKeys.ReportsColumnCashier),
        Column(nameof(CashierPerformanceRow.InvoiceCount), StringKeys.ReportsColumnInvoices),
        Column(nameof(CashierPerformanceRow.NetSales), StringKeys.ReportsColumnNetSales),
        Column(nameof(CashierPerformanceRow.AverageInvoice), StringKeys.ReportsColumnAverageInvoice),
    ];

    private IReadOnlyList<ReportColumnView> ReturnsColumns() =>
    [
        Column(nameof(ReturnsRow.Sku), StringKeys.ReportsColumnSku),
        Column(nameof(ReturnsRow.Name), StringKeys.ReportsColumnProduct),
        Column(nameof(ReturnsRow.Count), StringKeys.ReportsColumnInvoices),
        Column(nameof(ReturnsRow.Quantity), StringKeys.ReportsColumnQuantity),
        Column(nameof(ReturnsRow.Amount), StringKeys.ReportsColumnReturns),
        Column(nameof(ReturnsRow.Reasons), StringKeys.ReportsColumnReason),
    ];

    private IReadOnlyList<ReportColumnView> CashierShiftColumns() =>
    [
        Column(nameof(CashierShiftRow.Date), StringKeys.ReportsColumnDate),
        Column(nameof(CashierShiftRow.Cashier), StringKeys.ReportsColumnCashier),
        Column(nameof(CashierShiftRow.CashOpening), StringKeys.ReportsColumnDrawerOpening),
        Column(nameof(CashierShiftRow.CashTaken), StringKeys.ReportsColumnGrossSales),
        Column(nameof(CashierShiftRow.CashRefunded), StringKeys.ReportsColumnReturns),
        Column(nameof(CashierShiftRow.CashClosing), StringKeys.ReportsColumnTotal),
        Column(nameof(CashierShiftRow.CountedCash), StringKeys.ReportsColumnDrawerCounted),
        Column(nameof(CashierShiftRow.Variance), StringKeys.ReportsColumnVariance),
    ];

    private IReadOnlyList<ReportColumnView> AuditColumns() =>
    [
        Column(nameof(AuditEntryView.TimestampUtc), StringKeys.ReportsColumnDate),
        Column(nameof(AuditEntryView.Actor), StringKeys.ReportsColumnCashier),
        Column(nameof(AuditEntryView.Action), StringKeys.ReportsColumnMovement),
        Column(nameof(AuditEntryView.EntityType), StringKeys.ReportsColumnCategory),
        Column(nameof(AuditEntryView.Summary), StringKeys.ReportsColumnReason),
    ];

    /// <summary>Builds a column with its heading.</summary>
    private ReportColumnView Column(string propertyName, string headerKey) =>
        new(propertyName, _strings[headerKey], KindOf(propertyName));

    /// <summary>
    /// Works out how a column's value is written. The kind follows the name of the property, so
    /// that every report writes its money the same way.
    /// </summary>
    private static ReportColumnKind KindOf(string propertyName)
    {
        if (MoneyProperties.Contains(propertyName))
        {
            return ReportColumnKind.Money;
        }

        if (propertyName is nameof(DailySalesRow.Date) or nameof(StockMovementRow.Date))
        {
            return ReportColumnKind.Date;
        }

        if (propertyName == nameof(AuditEntryView.TimestampUtc))
        {
            return ReportColumnKind.Timestamp;
        }

        return ReportColumnKind.Text;
    }

    /// <summary>The properties that hold money.</summary>
    private static readonly HashSet<string> MoneyProperties = new(StringComparer.Ordinal)
    {
        nameof(DailySalesRow.GrossSales),
        nameof(DailySalesRow.Discounts),
        nameof(DailySalesRow.Tax),
        nameof(DailySalesRow.Returns),
        nameof(DailySalesRow.NetSales),
        nameof(SalesByProductRow.NetRevenue),
        nameof(SalesByProductRow.CostOfGoodsSold),
        nameof(SalesByProductRow.GrossProfit),
        nameof(StockOnHandRow.AverageUnitCost),
        nameof(StockOnHandRow.Value),
        nameof(StockValuationRow.AverageUnitCost),
        nameof(StockValuationRow.Value),
        nameof(StockMovementRow.UnitCost),
        nameof(PurchaseBySupplierRow.Subtotal),
        nameof(PurchaseBySupplierRow.Discount),
        nameof(PurchaseBySupplierRow.Tax),
        nameof(PurchaseBySupplierRow.Total),
        nameof(ProfitAndLossLine.Amount),
        nameof(CashierPerformanceRow.NetSales),
        nameof(CashierPerformanceRow.AverageInvoice),
        nameof(ReturnsRow.Amount),
        nameof(CashierShiftRow.CashOpening),
        nameof(CashierShiftRow.CashTaken),
        nameof(CashierShiftRow.CashRefunded),
        nameof(CashierShiftRow.CashClosing),
        nameof(CashierShiftRow.CountedCash),
        nameof(CashierShiftRow.Variance),
    };

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        BuildKinds();
        SelectedKind = Kinds[0];
        OnPropertyChanged(string.Empty);
    }
}
