using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The purchase register: every goods receipt, searchable by document number and supplier, and the
/// place where a receipt is posted or reversed (PRD PUR-05, PUR-06).
/// </summary>
public sealed partial class PurchasesViewModel : ObservableObject
{
    /// <summary>How long the register waits before it asks the database, in milliseconds.</summary>
    private const int RefreshDelayMilliseconds = 250;

    private readonly IQueryHandler<ListPurchasesQuery, Result<IReadOnlyList<PurchaseSummary>>> _listHandler;
    private readonly ICommandHandler<PostPurchaseCommand, Result<int>> _postHandler;
    private readonly ICommandHandler<ReversePurchaseCommand, Result<int>> _reverseHandler;
    private readonly ICommandHandler<ExportPurchasesCommand, Result<int>> _exportHandler;
    private readonly IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> _referenceHandler;
    private readonly IInventoryDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    private CancellationTokenSource? _refreshCancellation;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _fromDateText = string.Empty;

    [ObservableProperty]
    private string _toDateText = string.Empty;

    [ObservableProperty]
    private int _supplierId;

    [ObservableProperty]
    private PurchaseSummary? _selectedPurchase;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _totalCount;

    /// <summary>Creates the screen.</summary>
    public PurchasesViewModel(
        IQueryHandler<ListPurchasesQuery, Result<IReadOnlyList<PurchaseSummary>>> listHandler,
        ICommandHandler<PostPurchaseCommand, Result<int>> postHandler,
        ICommandHandler<ReversePurchaseCommand, Result<int>> reverseHandler,
        ICommandHandler<ExportPurchasesCommand, Result<int>> exportHandler,
        IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> referenceHandler,
        IInventoryDialogs dialogs,
        IStrings strings,
        LanguageService languageService)
    {
        _listHandler = listHandler;
        _postHandler = postHandler;
        _reverseHandler = reverseHandler;
        _exportHandler = exportHandler;
        _referenceHandler = referenceHandler;
        _dialogs = dialogs;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The purchases on screen.</summary>
    public ObservableCollection<PurchaseSummary> Purchases { get; } = [];

    /// <summary>The suppliers the owner can filter by.</summary>
    public ObservableCollection<InventoryOptionItem> Suppliers { get; } = [];

    /// <summary>The title of the screen.</summary>
    public string Title => _strings[StringKeys.PurchasesTitle];

    /// <summary>The label on the new-purchase command.</summary>
    public string NewText => _strings[StringKeys.PurchasesNew];

    /// <summary>The label on the edit command.</summary>
    public string EditText => _strings[StringKeys.CommonEdit];

    /// <summary>The label on the post command.</summary>
    public string PostText => _strings[StringKeys.PurchasesPost];

    /// <summary>The label on the reverse command.</summary>
    public string ReverseText => _strings[StringKeys.PurchasesReverse];

    /// <summary>The label on the export command.</summary>
    public string ExportText => _strings[StringKeys.CommonExport];

    /// <summary>The text shown inside the empty search box.</summary>
    public string SearchPlaceholder => _strings[StringKeys.PurchasesSearch];

    /// <summary>The label of the column that holds the document number.</summary>
    public string NumberHeader => _strings[StringKeys.PurchasesNumber];

    /// <summary>The label of the column that holds the date.</summary>
    public string DateHeader => _strings[StringKeys.PurchasesDate];

    /// <summary>The label of the column that holds the supplier.</summary>
    public string SupplierHeader => _strings[StringKeys.PurchasesSupplier];

    /// <summary>The label of the column that holds the state.</summary>
    public string StatusHeader => _strings[StringKeys.PurchasesStatus];

    /// <summary>The label of the column that holds the number of lines.</summary>
    public string LinesHeader => _strings[StringKeys.PurchasesLines];

    /// <summary>The label of the column that holds the total quantity.</summary>
    public string QuantityHeader => _strings[StringKeys.PurchasesQuantity];

    /// <summary>The label of the column that holds the total.</summary>
    public string TotalHeader => _strings[StringKeys.PurchasesTotal];

    /// <summary>The label of the column that holds the reversed document.</summary>
    public string ReversesHeader => _strings[StringKeys.PurchasesReverses];

    /// <summary>The label on the from-date field.</summary>
    public string FromLabel => _strings[StringKeys.PurchasesFrom];

    /// <summary>The label on the to-date field.</summary>
    public string ToLabel => _strings[StringKeys.PurchasesTo];

    /// <summary>The label on the supplier filter.</summary>
    public string SupplierFilterLabel => _strings[StringKeys.PurchasesSupplier];

    /// <summary>The label of the entry that keeps every supplier.</summary>
    public string AllSuppliersText => _strings[StringKeys.PurchasesAllSuppliers];

    /// <summary>The text shown when the register holds nothing at all.</summary>
    public string EmptyText => _strings[StringKeys.PurchasesEmpty];

    /// <summary>The text shown when a search finds nothing.</summary>
    public string NoMatchesText => _strings[StringKeys.PurchasesNoMatches];

    /// <summary>True when the register holds nothing at all.</summary>
    public bool IsEmpty => !IsBusy && TotalCount == 0 && string.IsNullOrWhiteSpace(SearchText);

    /// <summary>True when a search found nothing.</summary>
    public bool HasNoMatches => !IsBusy && TotalCount == 0 && !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>True when the search box holds nothing, so the placeholder should show.</summary>
    public bool HasNoSearchText => string.IsNullOrEmpty(SearchText);

    /// <summary>True when the selected document is still a draft and may be edited or posted.</summary>
    public bool CanEditSelected => SelectedPurchase?.Status == DocumentStatus.Draft;

    /// <summary>True when the selected document has been posted and may be reversed.</summary>
    public bool CanReverseSelected => SelectedPurchase?.Status == DocumentStatus.Posted;

    /// <summary>Loads the purchases the current filter matches.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result<InventoryReferenceData> reference = await _referenceHandler.HandleAsync(
                new GetInventoryReferenceDataQuery(),
                cancellationToken);

            if (reference.IsSuccess)
            {
                ApplySuppliers(reference.Value);
            }

            Result<IReadOnlyList<PurchaseSummary>> result = await _listHandler.HandleAsync(
                new ListPurchasesQuery(BuildFilter()),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = InventoryErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            Purchases.Clear();

            foreach (PurchaseSummary purchase in result.Value)
            {
                Purchases.Add(purchase);
            }

            // The newest document is the one the operator is working on, so it is selected as soon
            // as the register arrives. A document that is still selected stays selected.
            int selectedId = SelectedPurchase?.Id ?? 0;

            SelectedPurchase = Purchases.FirstOrDefault(purchase => purchase.Id == selectedId)
                ?? Purchases.FirstOrDefault();

            TotalCount = Purchases.Count;
            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            RaiseEmptyStateChanged();
        }
    }

    /// <summary>Creates a new purchase document.</summary>
    [RelayCommand]
    private async Task NewPurchaseAsync(CancellationToken cancellationToken)
    {
        bool saved = await _dialogs.ShowPurchaseEditorAsync(null, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Opens the selected purchase document.</summary>
    [RelayCommand]
    private async Task EditSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedPurchase is not { } purchase || purchase.Status != DocumentStatus.Draft)
        {
            return;
        }

        bool saved = await _dialogs.ShowPurchaseEditorAsync(purchase.Id, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Posts the selected purchase document, receiving the goods.</summary>
    [RelayCommand]
    private async Task PostSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedPurchase is not { } purchase || purchase.Status != DocumentStatus.Draft)
        {
            return;
        }

        Result<int> result = await _postHandler.HandleAsync(new PostPurchaseCommand(purchase.Id), cancellationToken);

        StatusMessage = result.IsFailure
            ? InventoryErrorMessages.Resolve(_strings, result.Error.Code)
            : _strings.Format(StringKeys.PurchasesPosted, purchase.Number);

        await RefreshAsync(cancellationToken);
    }

    /// <summary>Reverses the selected purchase document.</summary>
    [RelayCommand]
    private async Task ReverseSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedPurchase is not { } purchase || purchase.Status != DocumentStatus.Posted)
        {
            return;
        }

        string? reason = await _dialogs.AskForReasonAsync(
            _strings[StringKeys.PurchaseEditorReverseTitle],
            _strings[StringKeys.PurchaseEditorReverseReason],
            cancellationToken);

        if (reason is null)
        {
            return;
        }

        Result<int> result = await _reverseHandler.HandleAsync(
            new ReversePurchaseCommand(purchase.Id, reason),
            cancellationToken);

        StatusMessage = result.IsFailure
            ? InventoryErrorMessages.Resolve(_strings, result.Error.Code)
            : _strings.Format(StringKeys.PurchasesReversed, purchase.Number);

        await RefreshAsync(cancellationToken);
    }

    /// <summary>Writes the filtered register to a CSV file.</summary>
    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = "purchases.csv",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Result<int> result = await _exportHandler.HandleAsync(
            new ExportPurchasesCommand(dialog.FileName, BuildFilter(), BuildHeaders()),
            cancellationToken);

        StatusMessage = result.IsFailure
            ? InventoryErrorMessages.Resolve(_strings, result.Error.Code)
            : _strings.Format(StringKeys.PurchasesExported, result.Value);
    }

    /// <summary>
    /// The headings used for the exported file.
    /// </summary>
    /// <returns>The heading of each field.</returns>
    public IReadOnlyDictionary<string, string> BuildHeaders() => new Dictionary<string, string>
    {
        ["number"] = _strings[StringKeys.PurchasesNumber],
        ["date"] = _strings[StringKeys.PurchasesDate],
        ["supplier"] = _strings[StringKeys.PurchasesSupplier],
        ["status"] = _strings[StringKeys.PurchasesStatus],
        ["lines"] = _strings[StringKeys.PurchasesLines],
        ["quantity"] = _strings[StringKeys.PurchasesQuantity],
        ["subtotal"] = _strings[StringKeys.PurchasesSubtotal],
        ["discount"] = _strings[StringKeys.PurchasesDiscount],
        ["tax"] = _strings[StringKeys.PurchasesTax],
        ["total"] = _strings[StringKeys.PurchasesTotal],
        ["reverses"] = _strings[StringKeys.PurchasesReverses],
    };

    private PurchaseFilter BuildFilter() => new(
        SearchText,
        SupplierId == 0 ? null : SupplierId,
        InventoryInput.TryReadDate(FromDateText, out DateOnly from) ? from : null,
        InventoryInput.TryReadDate(ToDateText, out DateOnly to) ? to : null);

    private void ApplySuppliers(InventoryReferenceData data)
    {
        int previous = SupplierId;

        Suppliers.Clear();
        Suppliers.Add(new InventoryOptionItem(0, AllSuppliersText));

        foreach (InventoryReferenceSupplier supplier in data.Suppliers)
        {
            string name = _languageService.CurrentLanguage == "ar" && !string.IsNullOrWhiteSpace(supplier.NameAr)
                ? supplier.NameAr
                : supplier.Name;

            Suppliers.Add(new InventoryOptionItem(supplier.Id, name));
        }

        SupplierId = previous;
    }

    partial void OnSelectedPurchaseChanged(PurchaseSummary? value)
    {
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanReverseSelected));
    }

    partial void OnSearchTextChanged(string value)
    {
        RaiseEmptyStateChanged();
        ScheduleRefresh();
    }

    partial void OnFromDateTextChanged(string value) => ScheduleRefresh();

    partial void OnToDateTextChanged(string value) => ScheduleRefresh();

    partial void OnSupplierIdChanged(int value) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();

        CancellationToken token = _refreshCancellation.Token;

        _ = DebounceAsync(token);
    }

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

    private void RaiseEmptyStateChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HasNoSearchText));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
