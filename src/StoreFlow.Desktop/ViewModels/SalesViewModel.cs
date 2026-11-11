using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Common;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The sales register: every document that touched the money of a sale — the sale itself, the
/// returns against it and the reversal that may have undone it — and the place where a return or a
/// reversal is posted (PRD SAL-01, SAL-03, SAL-06).
/// </summary>
public sealed partial class SalesViewModel : ObservableObject
{
    /// <summary>How long the register waits before it asks the database, in milliseconds.</summary>
    private const int RefreshDelayMilliseconds = 250;

    private readonly IQueryHandler<ListSalesRegisterQuery, Result<IReadOnlyList<SalesRegisterRow>>> _listHandler;
    private readonly IQueryHandler<GetSalesInvoiceQuery, Result<SalesInvoiceDetails>> _invoiceHandler;
    private readonly IQueryHandler<GetSalesReturnQuery, Result<SalesReturnDetails>> _returnDetailHandler;
    private readonly IQueryHandler<ListSalesCashiersQuery, Result<IReadOnlyList<SalesCashierOption>>> _cashiersHandler;
    private readonly ICommandHandler<PostReturnCommand, Result<RefundReceipt>> _postReturnHandler;
    private readonly ICommandHandler<ReverseInvoiceCommand, Result<RefundReceipt>> _reverseHandler;
    private readonly ISalesDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    private CancellationTokenSource? _refreshCancellation;

    /// <summary>
    /// The load of the document that is on screen. It is cancelled when another document is picked,
    /// so a slow answer cannot land on top of the document the owner has just chosen.
    /// </summary>
    private CancellationTokenSource? _detailCancellation;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _fromDateText = string.Empty;

    [ObservableProperty]
    private string _toDateText = string.Empty;

    [ObservableProperty]
    private int _cashierId;

    [ObservableProperty]
    private SalesRegisterRow? _selectedDocument;

    [ObservableProperty]
    private SalesInvoiceDetails? _selectedInvoice;

    [ObservableProperty]
    private SalesReturnDetails? _selectedReturn;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _totalCount;

    /// <summary>Creates the screen.</summary>
    /// <param name="listHandler">Reads the register.</param>
    /// <param name="invoiceHandler">Reads one invoice with its lines and its returns.</param>
    /// <param name="returnDetailHandler">Reads one return with the lines that came back.</param>
    /// <param name="cashiersHandler">Reads the cashiers the register can be filtered by.</param>
    /// <param name="postReturnHandler">Posts a return.</param>
    /// <param name="reverseHandler">Reverses an invoice.</param>
    /// <param name="dialogs">Shows the return and the reversal.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The service that says which language is running.</param>
    public SalesViewModel(
        IQueryHandler<ListSalesRegisterQuery, Result<IReadOnlyList<SalesRegisterRow>>> listHandler,
        IQueryHandler<GetSalesInvoiceQuery, Result<SalesInvoiceDetails>> invoiceHandler,
        IQueryHandler<GetSalesReturnQuery, Result<SalesReturnDetails>> returnDetailHandler,
        IQueryHandler<ListSalesCashiersQuery, Result<IReadOnlyList<SalesCashierOption>>> cashiersHandler,
        ICommandHandler<PostReturnCommand, Result<RefundReceipt>> postReturnHandler,
        ICommandHandler<ReverseInvoiceCommand, Result<RefundReceipt>> reverseHandler,
        ISalesDialogs dialogs,
        IStrings strings,
        LanguageService languageService)
    {
        _listHandler = listHandler;
        _invoiceHandler = invoiceHandler;
        _returnDetailHandler = returnDetailHandler;
        _cashiersHandler = cashiersHandler;
        _postReturnHandler = postReturnHandler;
        _reverseHandler = reverseHandler;
        _dialogs = dialogs;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The documents on screen.</summary>
    public ObservableCollection<SalesRegisterRow> Documents { get; } = [];

    /// <summary>The cashiers the owner can filter by.</summary>
    public ObservableCollection<SalesCashierOption> Cashiers { get; } = [];

    /// <summary>The lines of the document in the detail panel.</summary>
    public ObservableCollection<SalesDetailLine> DetailLines { get; } = [];

    /// <summary>The returns recorded against the invoice in the detail panel.</summary>
    public ObservableCollection<SalesReturnRow> DetailReturns { get; } = [];

    /// <summary>The title of the screen.</summary>
    public string Title => _strings[StringKeys.SalesTitle];

    /// <summary>The text shown inside the empty search box.</summary>
    public string SearchPlaceholder => _strings[StringKeys.SalesSearch];

    /// <summary>The label on the from-date field.</summary>
    public string FromLabel => _strings[StringKeys.SalesFrom];

    /// <summary>The label on the to-date field.</summary>
    public string ToLabel => _strings[StringKeys.SalesTo];

    /// <summary>The label on the cashier filter.</summary>
    public string CashierLabel => _strings[StringKeys.SalesCashier];

    /// <summary>The label of the entry that keeps every cashier.</summary>
    public string AllCashiersText => _strings[StringKeys.SalesAllCashiers];

    /// <summary>The label on the refresh command.</summary>
    public string RefreshText => _strings[StringKeys.SalesRefresh];

    /// <summary>The label on the return command.</summary>
    public string ReturnText => _strings[StringKeys.SalesReturn];

    /// <summary>The label on the reversal command.</summary>
    public string ReverseText => _strings[StringKeys.SalesReverse];

    /// <summary>The label of the column that holds the document number.</summary>
    public string NumberHeader => _strings[StringKeys.SalesNumber];

    /// <summary>The label of the column that holds the kind of the document.</summary>
    public string KindHeader => _strings[StringKeys.SalesKind];

    /// <summary>The label of the column that holds the date.</summary>
    public string DateHeader => _strings[StringKeys.SalesDate];

    /// <summary>The label of the column that holds the cashier.</summary>
    public string CashierHeader => _strings[StringKeys.SalesCashier];

    /// <summary>The label of the column that holds the number of lines.</summary>
    public string LinesHeader => _strings[StringKeys.SalesLines];

    /// <summary>The label of the column that holds the total.</summary>
    public string TotalHeader => _strings[StringKeys.SalesTotal];

    /// <summary>The label of the column that holds what has been given back.</summary>
    public string ReturnedHeader => _strings[StringKeys.SalesReturned];

    /// <summary>The label of the column that holds the invoice a document belongs to.</summary>
    public string RelatedHeader => _strings[StringKeys.SalesRelated];

    /// <summary>The text shown when the register holds nothing at all.</summary>
    public string EmptyText => _strings[StringKeys.SalesEmpty];

    /// <summary>The text shown when a search finds nothing.</summary>
    public string NoMatchesText => _strings[StringKeys.SalesNoMatches];

    /// <summary>The text shown when nothing has been picked in the register.</summary>
    public string SelectDocumentText => _strings[StringKeys.SalesSelectDocument];

    /// <summary>The title of the detail panel.</summary>
    public string DetailTitle => _strings[StringKeys.SalesDetail];

    /// <summary>The label of the number of the document in the detail panel.</summary>
    public string DetailNumberLabel => _strings[StringKeys.SalesDetailNumber];

    /// <summary>The label of the date of the document in the detail panel.</summary>
    public string DetailDateLabel => _strings[StringKeys.SalesDetailDate];

    /// <summary>The label of the cashier of the document in the detail panel.</summary>
    public string DetailCashierLabel => _strings[StringKeys.SalesDetailCashier];

    /// <summary>The label of the state of the document in the detail panel.</summary>
    public string DetailStatusLabel => _strings[StringKeys.SalesDetailStatus];

    /// <summary>The label of the subtotal in the detail panel.</summary>
    public string DetailSubtotalLabel => _strings[StringKeys.SalesDetailSubtotal];

    /// <summary>The label of the discount in the detail panel.</summary>
    public string DetailDiscountLabel => _strings[StringKeys.SalesDetailDiscount];

    /// <summary>The label of the tax in the detail panel.</summary>
    public string DetailTaxLabel => _strings[StringKeys.SalesDetailTax];

    /// <summary>The label of the total in the detail panel.</summary>
    public string DetailTotalLabel => _strings[StringKeys.SalesDetailTotal];

    /// <summary>The label of what was paid in the detail panel.</summary>
    public string DetailPaidLabel => _strings[StringKeys.SalesDetailPaid];

    /// <summary>The label of the change in the detail panel.</summary>
    public string DetailChangeLabel => _strings[StringKeys.SalesDetailChange];

    /// <summary>The label of what has been given back in the detail panel.</summary>
    public string DetailReturnedLabel => _strings[StringKeys.SalesDetailReturned];

    /// <summary>The label of the reason in the detail panel.</summary>
    public string DetailReasonLabel => _strings[StringKeys.SalesDetailReason];

    /// <summary>The label of the way the money went in the detail panel.</summary>
    public string DetailMethodLabel => _strings[StringKeys.SalesDetailMethod];

    /// <summary>The label of the invoice a reversal undoes.</summary>
    public string DetailReversesLabel => _strings[StringKeys.SalesDetailReverses];

    /// <summary>The label of the reversal that undid an invoice.</summary>
    public string DetailReversedByLabel => _strings[StringKeys.SalesDetailReversedBy];

    /// <summary>The label of the lines of the document in the detail panel.</summary>
    public string DetailLinesLabel => _strings[StringKeys.SalesDetailLines];

    /// <summary>The label of the returns against the invoice in the detail panel.</summary>
    public string DetailReturnsLabel => _strings[StringKeys.SalesDetailReturns];

    /// <summary>The label of the column that holds the product of a line.</summary>
    public string DetailProductHeader => _strings[StringKeys.SalesLineProduct];

    /// <summary>The label of the column that holds the quantity of a line.</summary>
    public string DetailQuantityHeader => _strings[StringKeys.SalesLineQuantity];

    /// <summary>The label of the column that holds what has come back of a line.</summary>
    public string DetailLineReturnedHeader => _strings[StringKeys.SalesLineReturned];

    /// <summary>The label of the column that holds the price of a line.</summary>
    public string DetailPriceHeader => _strings[StringKeys.SalesLinePrice];

    /// <summary>The label of the column that holds the discount of a line.</summary>
    public string DetailLineDiscountHeader => _strings[StringKeys.SalesLineDiscount];

    /// <summary>The label of the column that holds the tax of a line.</summary>
    public string DetailLineTaxHeader => _strings[StringKeys.SalesLineTax];

    /// <summary>The label of the column that holds the total of a line.</summary>
    public string DetailLineTotalHeader => _strings[StringKeys.SalesLineTotal];

    /// <summary>True when the register holds nothing at all.</summary>
    public bool IsEmpty => !IsBusy && TotalCount == 0 && string.IsNullOrWhiteSpace(SearchText);

    /// <summary>True when the search found nothing.</summary>
    public bool HasNoMatches => !IsBusy && TotalCount == 0 && !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>True when the search box holds nothing, so the placeholder should show.</summary>
    public bool HasNoSearchText => string.IsNullOrEmpty(SearchText);

    /// <summary>True when nothing has been picked in the register.</summary>
    public bool HasNoSelection => SelectedInvoice is null && SelectedReturn is null;

    /// <summary>True when an invoice is on screen in the detail panel.</summary>
    public bool HasInvoice => SelectedInvoice is not null;

    /// <summary>The invoice a reversal undoes, when the document on screen is a reversal.</summary>
    public string ReversesText => Describe(StringKeys.SalesDetailReverses, SelectedInvoice?.ReversesNumber);

    /// <summary>The reversal that undid an invoice, when there is one.</summary>
    public string ReversedByText => Describe(StringKeys.SalesDetailReversedBy, SelectedInvoice?.ReversedByNumber);

    /// <summary>
    /// Writes a label and a number together, and nothing at all when there is no number.
    /// </summary>
    /// <param name="labelKey">The resource key of the label.</param>
    /// <param name="value">The number, when there is one.</param>
    /// <returns>The line to show.</returns>
    private string Describe(string labelKey, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"{_strings[labelKey]} {value}";

    /// <summary>True when a return is on screen in the detail panel.</summary>
    public bool HasReturn => SelectedReturn is not null;

    /// <summary>
    /// True when the selected document may be returned against: it is a posted sale that has not
    /// been reversed, and something on it is still waiting to come back.
    /// </summary>
    public bool CanReturnSelected
    {
        get
        {
            if (SelectedDocument is not { } document)
            {
                return false;
            }

            if (document.Kind != SalesDocumentKind.Invoice || document.Status != DocumentStatus.Posted)
            {
                return false;
            }

            return SelectedInvoice is { } invoice
                && invoice.Lines.Any(line => line.ReturnedQuantityScaled < line.QuantityScaled);
        }
    }

    /// <summary>
    /// True when the selected document may be reversed: it is a posted sale that has not been
    /// reversed already.
    /// </summary>
    public bool CanReverseSelected
    {
        get
        {
            if (SelectedDocument is not { } document)
            {
                return false;
            }

            return document.Kind == SalesDocumentKind.Invoice && document.Status == DocumentStatus.Posted;
        }
    }

    /// <summary>Loads the documents the current filter matches.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result<IReadOnlyList<SalesCashierOption>> cashiers = await _cashiersHandler.HandleAsync(
                new ListSalesCashiersQuery(),
                cancellationToken);

            if (cashiers.IsSuccess)
            {
                ApplyCashiers(cashiers.Value);
            }

            Result<IReadOnlyList<SalesRegisterRow>> rows = await _listHandler.HandleAsync(
                new ListSalesRegisterQuery(new SalesFilter(
                    SearchText,
                    InventoryInput.TryReadDate(FromDateText, out DateOnly from) ? from : null,
                    InventoryInput.TryReadDate(ToDateText, out DateOnly to) ? to : null,
                    CashierId == 0 ? null : CashierId)),
                cancellationToken);

            if (rows.IsFailure)
            {
                StatusMessage = SalesErrorMessages.Resolve(_strings, rows.Error.Code);
                OnPropertyChanged(nameof(StatusMessage));
                return;
            }

            int? selectedId = SelectedDocument?.Id;
            SalesDocumentKind selectedKind = SelectedDocument?.Kind ?? SalesDocumentKind.Invoice;

            Documents.Clear();

            foreach (SalesRegisterRow row in rows.Value)
            {
                Documents.Add(row);
            }

            TotalCount = Documents.Count;

            SelectedDocument = Documents.FirstOrDefault(row => row.Id == selectedId && row.Kind == selectedKind)
                ?? Documents.FirstOrDefault();

            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            RaiseEmptyStateChanged();
        }
    }

    /// <summary>
    /// Posts a return against the selected invoice.
    /// </summary>
    [RelayCommand]
    private async Task ReturnSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedInvoice is not { } invoice || !CanReturnSelected)
        {
            return;
        }

        ReturnDialogResult? chosen = await _dialogs.AskForReturnAsync(invoice, cancellationToken);

        if (chosen is null)
        {
            return;
        }

        Result<RefundReceipt> posted = await _postReturnHandler.HandleAsync(
            new PostReturnCommand(
                invoice.Id,
                invoice.BusinessDate,
                chosen.Lines
                    .Select(line => new ReturnLineInput(line.SalesInvoiceLineId, line.Quantity, line.IsResellable))
                    .ToList(),
                chosen.RefundMethod,
                chosen.Reason),
            cancellationToken);

        StatusMessage = posted.IsFailure
            ? SalesErrorMessages.Resolve(_strings, posted.Error.Code)
            : _strings.Format(StringKeys.SalesReturnPosted, posted.Value.Number);

        OnPropertyChanged(nameof(StatusMessage));

        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Reverses the selected invoice.
    /// </summary>
    [RelayCommand]
    private async Task ReverseSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedInvoice is not { } invoice || !CanReverseSelected)
        {
            return;
        }

        ReverseDialogResult? chosen = await _dialogs.AskForReverseAsync(invoice, cancellationToken);

        if (chosen is null)
        {
            return;
        }

        Result<RefundReceipt> posted = await _reverseHandler.HandleAsync(
            new ReverseInvoiceCommand(
                invoice.Id,
                invoice.BusinessDate,
                chosen.RefundMethod,
                chosen.Reason,
                chosen.IsResellable),
            cancellationToken);

        StatusMessage = posted.IsFailure
            ? SalesErrorMessages.Resolve(_strings, posted.Error.Code)
            : _strings.Format(StringKeys.SalesReversalPosted, posted.Value.Number);

        OnPropertyChanged(nameof(StatusMessage));

        await RefreshAsync(cancellationToken);
    }

    partial void OnSelectedDocumentChanged(SalesRegisterRow? value)
    {
        OnPropertyChanged(nameof(CanReturnSelected));
        OnPropertyChanged(nameof(CanReverseSelected));

        // The load of the document that was on screen before is cancelled, so the lines of an older
        // document can never be shown under a newer choice.
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        _detailCancellation = new CancellationTokenSource();

        _ = LoadDetailAsync(value, _detailCancellation.Token);
    }

    partial void OnSearchTextChanged(string value)
    {
        RaiseEmptyStateChanged();
        ScheduleRefresh();
    }

    partial void OnFromDateTextChanged(string value) => ScheduleRefresh();

    partial void OnToDateTextChanged(string value) => ScheduleRefresh();

    partial void OnCashierIdChanged(int value) => ScheduleRefresh();

    /// <summary>
    /// Loads the document the operator has picked, so the detail panel can show it.
    /// </summary>
    /// <param name="document">The document that was picked.</param>
    /// <param name="cancellationToken">The token that is cancelled when another document is picked.</param>
    /// <remarks>
    /// Only the document that is on screen now may fill the panel. A load that has been overtaken
    /// stops as soon as its answer arrives, so the lines of an older document are never shown under
    /// a newer choice.
    /// </remarks>
    private async Task LoadDetailAsync(SalesRegisterRow? document, CancellationToken cancellationToken)
    {
        try
        {
            SelectedInvoice = null;
            SelectedReturn = null;
            DetailLines.Clear();
            DetailReturns.Clear();

            if (document is null)
            {
                RaiseDetailChanged();
                return;
            }

            if (document.Kind == SalesDocumentKind.Return)
            {
                Result<SalesReturnDetails> detail = await _returnDetailHandler.HandleAsync(
                    new GetSalesReturnQuery(document.Id),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (detail.IsFailure)
                {
                    StatusMessage = SalesErrorMessages.Resolve(_strings, detail.Error.Code);
                    OnPropertyChanged(nameof(StatusMessage));
                    RaiseDetailChanged();
                    return;
                }

                SelectedReturn = detail.Value;

                foreach (ReturnLineSummary line in detail.Value.Lines)
                {
                    DetailLines.Add(new SalesDetailLine(
                        line.Name,
                        line.Quantity,
                        0m,
                        Money.Zero,
                        Money.Zero,
                        Money.Zero,
                        line.RefundAmount));
                }
            }
            else
            {
                Result<SalesInvoiceDetails> detail = await _invoiceHandler.HandleAsync(
                    new GetSalesInvoiceQuery(document.Id),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (detail.IsFailure)
                {
                    StatusMessage = SalesErrorMessages.Resolve(_strings, detail.Error.Code);
                    OnPropertyChanged(nameof(StatusMessage));
                    RaiseDetailChanged();
                    return;
                }

                SelectedInvoice = detail.Value;

                foreach (SalesLineDetails line in detail.Value.Lines)
                {
                    DetailLines.Add(new SalesDetailLine(
                        line.Name,
                        line.Quantity,
                        SalesRules.Unscale(line.ReturnedQuantityScaled, line.QuantityScale),
                        line.UnitPrice,
                        line.Discount,
                        line.Tax,
                        line.Total));
                }

                foreach (SalesReturnSummary salesReturn in detail.Value.Returns)
                {
                    DetailReturns.Add(new SalesReturnRow(
                        salesReturn.Number,
                        salesReturn.BusinessDate.ToString("yyyy-MM-dd"),
                        salesReturn.CashierName,
                        salesReturn.Reason,
                        MethodName(salesReturn.RefundMethod),
                        salesReturn.RefundTotal,
                        salesReturn.IsFullReturn));
                }
            }

            RaiseDetailChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Another document has been picked while this one was loading. The answer is stale and
            // is dropped; the load that took over owns the panel.
        }
#pragma warning disable CA1031 // A load that fails must not take the screen down with it.
        catch (Exception ex)
        {
            // A load that fails outright must not lose its exception in the void. The log keeps the
            // detail for whoever looks after the shop, and the owner is told that the document
            // could not be read.
            StartupDiagnostics.Write("The sales detail could not be loaded.", ex);

            if (!cancellationToken.IsCancellationRequested)
            {
                StatusMessage = _strings[StringKeys.ErrorUnexpected];
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
#pragma warning restore CA1031
    }

    /// <summary>The name of a way of paying, in the running language.</summary>
    /// <param name="method">The way of paying.</param>
    /// <returns>The name.</returns>
    private string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => _strings[StringKeys.PosCash],
        PaymentMethod.Card => _strings[StringKeys.PosCard],
        PaymentMethod.Other => _strings[StringKeys.PosOther],
        _ => _strings[StringKeys.PosOther],
    };

    /// <summary>Writes the cashiers the register can be filtered by.</summary>
    /// <param name="options">The cashiers.</param>
    private void ApplyCashiers(IReadOnlyList<SalesCashierOption> options)
    {
        int previous = CashierId;

        Cashiers.Clear();
        Cashiers.Add(new SalesCashierOption(0, AllCashiersText));

        foreach (SalesCashierOption option in options)
        {
            Cashiers.Add(option);
        }

        CashierId = previous;
    }

    /// <summary>Schedules a refresh, and forgets the one that was waiting.</summary>
    private void ScheduleRefresh()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();

        CancellationToken token = _refreshCancellation.Token;

        _ = DebounceAsync(token);
    }

    /// <summary>Waits for the owner to stop typing before asking the database anything.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task DebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(RefreshDelayMilliseconds, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    /// <summary>Raises the empty states, so the register can say what it has not found.</summary>
    private void RaiseEmptyStateChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HasNoSearchText));
    }

    /// <summary>Raises the detail panel's states.</summary>
    private void RaiseDetailChanged()
    {
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(HasInvoice));
        OnPropertyChanged(nameof(HasReturn));
        OnPropertyChanged(nameof(ReversesText));
        OnPropertyChanged(nameof(ReversedByText));
        OnPropertyChanged(nameof(CanReturnSelected));
        OnPropertyChanged(nameof(CanReverseSelected));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}

/// <summary>One line of the document in the detail panel.</summary>
/// <param name="Product">The name of the product.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="Returned">How much of it has come back.</param>
/// <param name="UnitPrice">The price of one unit.</param>
/// <param name="Discount">The discount granted.</param>
/// <param name="Tax">The tax charged.</param>
/// <param name="Total">The amount of the line.</param>
public sealed record SalesDetailLine(
    string Product,
    decimal Quantity,
    decimal Returned,
    Money UnitPrice,
    Money Discount,
    Money Tax,
    Money Total);

/// <summary>One return recorded against the invoice in the detail panel.</summary>
/// <param name="Number">The number of the return.</param>
/// <param name="Date">The date of the return.</param>
/// <param name="Cashier">The name of the person who posted it.</param>
/// <param name="Reason">Why the goods came back.</param>
/// <param name="Method">How the money went back.</param>
/// <param name="Refund">The amount that went back.</param>
/// <param name="IsFullReturn">True when every unit on the invoice came back.</param>
public sealed record SalesReturnRow(
    string Number,
    string Date,
    string Cashier,
    string Reason,
    string Method,
    Money Refund,
    bool IsFullReturn);
