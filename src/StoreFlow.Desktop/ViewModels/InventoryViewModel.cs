using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The inventory screen: what is on the shelf, what moved, and the counts that were taken. The
/// valuation panel at the top answers the question the owner asks most often, which is what the
/// stock is worth right now (PRD INV-01, INV-02, INV-03, INV-05, INV-07).
/// </summary>
public sealed partial class InventoryViewModel : ObservableObject
{
    private readonly IQueryHandler<ListStockBalancesQuery, Result<IReadOnlyList<StockBalanceSummary>>> _balancesHandler;
    private readonly IQueryHandler<ListStockMovementsQuery, Result<IReadOnlyList<StockMovementSummary>>> _movementsHandler;
    private readonly IQueryHandler<ListStockCountsQuery, Result<IReadOnlyList<StockCountSummary>>> _countsHandler;
    private readonly IQueryHandler<GetInventoryValuationQuery, Result<InventoryValuation>> _valuationHandler;
    private readonly IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> _referenceHandler;
    private readonly ICommandHandler<ApproveStockCountCommand, Result> _approveCountHandler;
    private readonly ICommandHandler<PostStockCountCommand, Result<int>> _postCountHandler;
    private readonly ICommandHandler<CancelStockCountCommand, Result> _cancelCountHandler;
    private readonly IInventoryDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    /// <summary>
    /// The read of the stock on hand that is on its way. It is cancelled when the owner changes the
    /// filter, so the rows on screen are always the rows the filter asks for.
    /// </summary>
    private CancellationTokenSource? _balancesCancellation;

    /// <summary>
    /// The read of the ledger that is on its way. It is cancelled when the owner narrows the
    /// ledger down, so an older answer cannot land on top of a newer one.
    /// </summary>
    private CancellationTokenSource? _movementsCancellation;

    /// <summary>
    /// The read of the valuation that is on its way. It is cancelled when the owner changes the
    /// date the valuation is taken at.
    /// </summary>
    private CancellationTokenSource? _valuationCancellation;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _onlyLowStock;

    [ObservableProperty]
    private int _movementProductId;

    [ObservableProperty]
    private string _fromDateText = string.Empty;

    [ObservableProperty]
    private string _toDateText = string.Empty;

    [ObservableProperty]
    private string _asOfDateText = string.Empty;

    [ObservableProperty]
    private StockBalanceSummary? _selectedBalance;

    [ObservableProperty]
    private StockCountSummary? _selectedCount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private InventoryValuation? _valuation;

    /// <summary>Creates the screen.</summary>
    public InventoryViewModel(
        IQueryHandler<ListStockBalancesQuery, Result<IReadOnlyList<StockBalanceSummary>>> balancesHandler,
        IQueryHandler<ListStockMovementsQuery, Result<IReadOnlyList<StockMovementSummary>>> movementsHandler,
        IQueryHandler<ListStockCountsQuery, Result<IReadOnlyList<StockCountSummary>>> countsHandler,
        IQueryHandler<GetInventoryValuationQuery, Result<InventoryValuation>> valuationHandler,
        IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> referenceHandler,
        ICommandHandler<ApproveStockCountCommand, Result> approveCountHandler,
        ICommandHandler<PostStockCountCommand, Result<int>> postCountHandler,
        ICommandHandler<CancelStockCountCommand, Result> cancelCountHandler,
        IInventoryDialogs dialogs,
        IStrings strings,
        LanguageService languageService)
    {
        _balancesHandler = balancesHandler;
        _movementsHandler = movementsHandler;
        _countsHandler = countsHandler;
        _valuationHandler = valuationHandler;
        _referenceHandler = referenceHandler;
        _approveCountHandler = approveCountHandler;
        _postCountHandler = postCountHandler;
        _cancelCountHandler = cancelCountHandler;
        _dialogs = dialogs;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The stock on hand.</summary>
    public ObservableCollection<StockBalanceSummary> Balances { get; } = [];

    /// <summary>The stock ledger.</summary>
    public ObservableCollection<StockMovementSummary> Movements { get; } = [];

    /// <summary>The stock count sessions.</summary>
    public ObservableCollection<StockCountSummary> Counts { get; } = [];

    /// <summary>The products the owner can filter the ledger by.</summary>
    public ObservableCollection<InventoryOptionItem> Products { get; } = [];

    /// <summary>The title of the screen.</summary>
    public string Title => _strings[StringKeys.NavInventory];

    /// <summary>The label of the first tab.</summary>
    public string StockOnHandText => _strings[StringKeys.InventoryStockOnHand];

    /// <summary>The label of the second tab.</summary>
    public string MovementsText => _strings[StringKeys.InventoryMovements];

    /// <summary>The label of the third tab.</summary>
    public string CountsText => _strings[StringKeys.InventoryCounts];

    /// <summary>The label of the valuation panel.</summary>
    public string ValuationText => _strings[StringKeys.InventoryValuation];

    /// <summary>The label on the report date field.</summary>
    public string AsOfDateLabel => _strings[StringKeys.InventoryAsOfDate];

    /// <summary>The label of the product count in the valuation panel.</summary>
    public string ProductCountLabel => _strings[StringKeys.InventoryProductCount];

    /// <summary>The label of the column that holds the document date.</summary>
    public string DateHeader => _strings[StringKeys.InventoryDate];

    /// <summary>The label of the total quantity in the valuation panel.</summary>
    public string TotalQuantityLabel => _strings[StringKeys.InventoryTotalQuantity];

    /// <summary>The label of the total value in the valuation panel.</summary>
    public string TotalValueLabel => _strings[StringKeys.InventoryTotalValue];

    /// <summary>The label of the low-stock count in the valuation panel.</summary>
    public string LowStockCountLabel => _strings[StringKeys.InventoryLowStockCount];

    /// <summary>The text shown inside the empty search box.</summary>
    public string SearchPlaceholder => _strings[StringKeys.InventorySearch];

    /// <summary>The label on the low-stock filter.</summary>
    public string OnlyLowStockText => _strings[StringKeys.InventoryOnlyLowStock];

    /// <summary>The label of the column that holds the stock-keeping unit code.</summary>
    public string SkuHeader => _strings[StringKeys.InventorySku];

    /// <summary>The label of the column that holds the name.</summary>
    public string NameHeader => _strings[StringKeys.InventoryName];

    /// <summary>The label of the column that holds the Arabic name.</summary>
    public string NameArHeader => _strings[StringKeys.InventoryNameAr];

    /// <summary>The label of the column that holds the unit.</summary>
    public string UnitHeader => _strings[StringKeys.InventoryUnit];

    /// <summary>The label of the column that holds the quantity on hand.</summary>
    public string OnHandHeader => _strings[StringKeys.InventoryOnHand];

    /// <summary>The label of the column that holds the damaged quantity.</summary>
    public string DamagedHeader => _strings[StringKeys.InventoryDamaged];

    /// <summary>The label of the column that holds the average cost.</summary>
    public string AverageCostHeader => _strings[StringKeys.InventoryAverageCost];

    /// <summary>The label of the column that holds the stock value.</summary>
    public string StockValueHeader => _strings[StringKeys.InventoryStockValue];

    /// <summary>The label of the column that holds the reorder level.</summary>
    public string ReorderLevelHeader => _strings[StringKeys.InventoryReorderLevel];

    /// <summary>The label of the column that holds the state.</summary>
    public string StatusHeader => _strings[StringKeys.CommonStatus];

    /// <summary>The label of the column that holds the kind of movement.</summary>
    public string MovementTypeHeader => _strings[StringKeys.InventoryMovementType];

    /// <summary>The label of the column that holds the signed quantity.</summary>
    public string QuantityDeltaHeader => _strings[StringKeys.InventoryQuantityDelta];

    /// <summary>The label of the column that holds the running balance.</summary>
    public string RunningBalanceHeader => _strings[StringKeys.InventoryRunningBalance];

    /// <summary>The label of the column that holds the reason.</summary>
    public string ReasonHeader => _strings[StringKeys.InventoryReason];

    /// <summary>The label of the column that holds the posting moment.</summary>
    public string PostedHeader => _strings[StringKeys.InventoryPosted];

    /// <summary>The label of the column that holds the count number.</summary>
    public string CountNumberHeader => _strings[StringKeys.InventoryCountNumber];

    /// <summary>The label of the column that holds the number of counted products.</summary>
    public string CountLinesHeader => _strings[StringKeys.InventoryCountLines];

    /// <summary>The label of the column that holds the quantity difference.</summary>
    public string CountDifferenceHeader => _strings[StringKeys.InventoryCountDifference];

    /// <summary>The label of the column that holds the value difference.</summary>
    public string CountDifferenceValueHeader => _strings[StringKeys.InventoryCountDifferenceValue];

    /// <summary>The label on the from-date field.</summary>
    public string FromLabel => _strings[StringKeys.PurchasesFrom];

    /// <summary>
    /// The label of the product picker that narrows the movements. The picker has no visible
    /// label of its own, so this is what names it for a screen reader.
    /// </summary>
    public string ProductLabel => _strings[StringKeys.InventoryProduct];

    /// <summary>The label on the to-date field.</summary>
    public string ToLabel => _strings[StringKeys.PurchasesTo];

    /// <summary>The label on the new-count command.</summary>
    public string NewCountText => _strings[StringKeys.InventoryNewCount];

    /// <summary>The label on the approve command.</summary>
    public string ApproveCountText => _strings[StringKeys.InventoryApproveCount];

    /// <summary>The label on the post command.</summary>
    public string PostCountText => _strings[StringKeys.InventoryPostCount];

    /// <summary>The label on the cancel command.</summary>
    public string CancelCountText => _strings[StringKeys.InventoryCancelCount];

    /// <summary>The label on the adjustment command.</summary>
    public string AdjustmentText => _strings[StringKeys.InventoryAdjustment];

    /// <summary>The label on the opening stock command.</summary>
    public string OpeningStockText => _strings[StringKeys.InventoryOpeningStock];

    /// <summary>The text shown when the shop holds no stock records.</summary>
    public string EmptyText => _strings[StringKeys.InventoryEmpty];

    /// <summary>The text shown when the ledger holds nothing.</summary>
    public string NoMovementsText => _strings[StringKeys.InventoryNoMovements];

    /// <summary>The text shown when no count has been taken.</summary>
    public string NoCountsText => _strings[StringKeys.InventoryNoCounts];

    /// <summary>True when the search box holds nothing, so the placeholder should show.</summary>
    public bool HasNoSearchText => string.IsNullOrEmpty(SearchText);

    /// <summary>True when no product carries a stock position.</summary>
    public bool HasNoBalances => Balances.Count == 0;

    /// <summary>True when the ledger holds nothing.</summary>
    public bool HasNoMovements => Movements.Count == 0;

    /// <summary>True when no count has been taken.</summary>
    public bool HasNoCounts => Counts.Count == 0;

    /// <summary>The number of products the valuation covers.</summary>
    public string ValuationProductCount => (Valuation?.ProductCount ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>The total quantity the valuation covers.</summary>
    public string ValuationTotalQuantity => InventoryInput.WriteNumber(Valuation?.TotalQuantity ?? 0m);

    /// <summary>The total value the valuation covers.</summary>
    public decimal ValuationTotalValue => Valuation?.TotalValue.Amount ?? 0m;

    /// <summary>The number of products at or below their reorder level.</summary>
    public string ValuationLowStockCount => (Valuation?.LowStockCount ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>True when the selected count is waiting for approval.</summary>
    public bool CanApproveCount => SelectedCount?.Status == StockCountStatus.AwaitingApproval;

    /// <summary>True when the selected count is approved and may be posted.</summary>
    public bool CanPostCount => SelectedCount?.Status == StockCountStatus.Approved;

    /// <summary>True when the selected count may still be cancelled.</summary>
    public bool CanCancelCount => SelectedCount is { } count
        && count.Status is StockCountStatus.Draft or StockCountStatus.AwaitingApproval;

    /// <summary>Loads everything the screen shows.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            await LoadReferenceDataAsync(cancellationToken);
            await LoadBalancesAsync(cancellationToken);
            await LoadMovementsAsync(cancellationToken);
            await LoadCountsAsync(cancellationToken);
            await LoadValuationAsync(cancellationToken);

            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Starts a new stock count.</summary>
    [RelayCommand]
    private async Task NewCountAsync(CancellationToken cancellationToken)
    {
        bool saved = await _dialogs.ShowStockCountAsync(cancellationToken);

        if (saved)
        {
            await LoadCountsAsync(cancellationToken);
            await LoadBalancesAsync(cancellationToken);
        }
    }

    /// <summary>Approves the selected count.</summary>
    [RelayCommand]
    private async Task ApproveCountAsync(CancellationToken cancellationToken)
    {
        if (SelectedCount is not { } count || count.Status != StockCountStatus.AwaitingApproval)
        {
            return;
        }

        string? reason = await _dialogs.AskForReasonAsync(
            _strings[StringKeys.StockCountApproveTitle],
            _strings[StringKeys.StockCountApproveReason],
            cancellationToken);

        if (reason is null)
        {
            return;
        }

        Result result = await _approveCountHandler.HandleAsync(
            new ApproveStockCountCommand(count.Id, reason),
            cancellationToken);

        StatusMessage = result.IsFailure
            ? InventoryErrorMessages.Resolve(_strings, result.Error.Code)
            : _strings.Format(StringKeys.InventoryCountApproved, count.Number);

        await LoadCountsAsync(cancellationToken);
    }

    /// <summary>Posts the selected count, writing its corrections.</summary>
    [RelayCommand]
    private async Task PostCountAsync(CancellationToken cancellationToken)
    {
        if (SelectedCount is not { } count || count.Status != StockCountStatus.Approved)
        {
            return;
        }

        Result<int> result = await _postCountHandler.HandleAsync(new PostStockCountCommand(count.Id), cancellationToken);

        StatusMessage = result.IsFailure
            ? InventoryErrorMessages.Resolve(_strings, result.Error.Code)
            : _strings.Format(StringKeys.InventoryCountPosted, count.Number);

        await LoadCountsAsync(cancellationToken);
        await LoadBalancesAsync(cancellationToken);
        await LoadMovementsAsync(cancellationToken);
        await LoadValuationAsync(cancellationToken);
    }

    /// <summary>Cancels the selected count.</summary>
    [RelayCommand]
    private async Task CancelCountAsync(CancellationToken cancellationToken)
    {
        if (SelectedCount is not { } count)
        {
            return;
        }

        Result result = await _cancelCountHandler.HandleAsync(new CancelStockCountCommand(count.Id), cancellationToken);

        StatusMessage = result.IsFailure
            ? InventoryErrorMessages.Resolve(_strings, result.Error.Code)
            : _strings.Format(StringKeys.InventoryCountCancelled, count.Number);

        await LoadCountsAsync(cancellationToken);
    }

    /// <summary>Records a manual stock adjustment.</summary>
    [RelayCommand]
    private async Task AdjustStockAsync(CancellationToken cancellationToken)
    {
        bool posted = await _dialogs.ShowStockAdjustmentAsync(cancellationToken);

        if (posted)
        {
            await LoadBalancesAsync(cancellationToken);
            await LoadMovementsAsync(cancellationToken);
            await LoadValuationAsync(cancellationToken);
        }
    }

    /// <summary>Records opening stock.</summary>
    [RelayCommand]
    private async Task RecordOpeningStockAsync(CancellationToken cancellationToken)
    {
        bool recorded = await _dialogs.ShowOpeningStockAsync(cancellationToken);

        if (recorded)
        {
            await LoadBalancesAsync(cancellationToken);
            await LoadMovementsAsync(cancellationToken);
            await LoadValuationAsync(cancellationToken);
        }
    }

    private async Task LoadReferenceDataAsync(CancellationToken cancellationToken)
    {
        Result<InventoryReferenceData> reference = await _referenceHandler.HandleAsync(
            new GetInventoryReferenceDataQuery(),
            cancellationToken);

        if (reference.IsFailure)
        {
            return;
        }

        int previous = MovementProductId;

        Products.Clear();
        Products.Add(new InventoryOptionItem(0, _strings[StringKeys.InventoryMovements]));

        foreach (InventoryReferenceProduct product in reference.Value.Products)
        {
            string name = _languageService.CurrentLanguage == "ar" && !string.IsNullOrWhiteSpace(product.NameAr)
                ? product.NameAr
                : product.Name;

            Products.Add(new InventoryOptionItem(product.Id, product.Sku + " - " + name, product.QuantityScale, product.CostPrice.Amount, product.UnitName));
        }

        MovementProductId = previous;
    }

    private async Task LoadBalancesAsync(CancellationToken cancellationToken)
    {
        // A read that is already on its way is cancelled, because the newest filter is the one the
        // owner means. The old source is not disposed: the read it belongs to may still be handing
        // the token to the database.
        _balancesCancellation?.Cancel();
        _balancesCancellation = new CancellationTokenSource();

        CancellationToken token = _balancesCancellation.Token;

        try
        {
            Result<IReadOnlyList<StockBalanceSummary>> result = await _balancesHandler.HandleAsync(
                new ListStockBalancesQuery(SearchText, OnlyLowStock),
                token);

            token.ThrowIfCancellationRequested();

            if (result.IsFailure)
            {
                StatusMessage = InventoryErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            Balances.Clear();

            foreach (StockBalanceSummary balance in result.Value)
            {
                Balances.Add(balance);
            }

            OnPropertyChanged(nameof(HasNoBalances));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another read of the stock on hand has taken over; this answer is stale and is dropped.
        }
#pragma warning disable CA1031 // A read that fails must not take the screen down with it.
        catch (Exception ex)
        {
            // A read that fails outright must not lose its exception in the void. The log keeps the
            // detail and the owner is told that the stock could not be read.
            StartupDiagnostics.Write("The stock on hand could not be read.", ex);

            if (!token.IsCancellationRequested)
            {
                StatusMessage = _strings[StringKeys.ErrorUnexpected];
            }
        }
#pragma warning restore CA1031
    }

    private async Task LoadMovementsAsync(CancellationToken cancellationToken)
    {
        // A read that is already on its way is cancelled, because the newest filter is the one the
        // owner means. The old source is not disposed: the read it belongs to may still be handing
        // the token to the database.
        _movementsCancellation?.Cancel();
        _movementsCancellation = new CancellationTokenSource();

        CancellationToken token = _movementsCancellation.Token;

        try
        {
            Result<IReadOnlyList<StockMovementSummary>> result = await _movementsHandler.HandleAsync(
                new ListStockMovementsQuery(
                    MovementProductId == 0 ? null : MovementProductId,
                    InventoryInput.TryReadDate(FromDateText, out DateOnly from) ? from : null,
                    InventoryInput.TryReadDate(ToDateText, out DateOnly to) ? to : null),
                token);

            token.ThrowIfCancellationRequested();

            if (result.IsFailure)
            {
                StatusMessage = InventoryErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            Movements.Clear();

            foreach (StockMovementSummary movement in result.Value)
            {
                Movements.Add(movement);
            }

            OnPropertyChanged(nameof(HasNoMovements));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another read of the ledger has taken over; this answer is stale and is dropped.
        }
#pragma warning disable CA1031 // A read that fails must not take the screen down with it.
        catch (Exception ex)
        {
            // A read that fails outright must not lose its exception in the void. The log keeps the
            // detail and the owner is told that the ledger could not be read.
            StartupDiagnostics.Write("The stock ledger could not be read.", ex);

            if (!token.IsCancellationRequested)
            {
                StatusMessage = _strings[StringKeys.ErrorUnexpected];
            }
        }
#pragma warning restore CA1031
    }

    private async Task LoadCountsAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<StockCountSummary>> result = await _countsHandler.HandleAsync(
            new ListStockCountsQuery(),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = InventoryErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        Counts.Clear();

        int selectedId = SelectedCount?.Id ?? 0;

        foreach (StockCountSummary count in result.Value)
        {
            Counts.Add(count);
        }

        // The newest count is the one the operator is working on, so it is selected as soon as the
        // list arrives. A count that is still selected stays selected.
        SelectedCount = Counts.FirstOrDefault(count => count.Id == selectedId) ?? Counts.FirstOrDefault();

        OnPropertyChanged(nameof(HasNoCounts));
    }

    private async Task LoadValuationAsync(CancellationToken cancellationToken)
    {
        // A read that is already on its way is cancelled, because the newest date is the one the
        // owner means. The old source is not disposed: the read it belongs to may still be handing
        // the token to the database.
        _valuationCancellation?.Cancel();
        _valuationCancellation = new CancellationTokenSource();

        CancellationToken token = _valuationCancellation.Token;

        try
        {
            Result<InventoryValuation> result = await _valuationHandler.HandleAsync(
                new GetInventoryValuationQuery(
                    InventoryInput.TryReadDate(AsOfDateText, out DateOnly asOf) ? asOf : null),
                token);

            token.ThrowIfCancellationRequested();

            if (result.IsFailure)
            {
                StatusMessage = InventoryErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            Valuation = result.Value;

            OnPropertyChanged(nameof(ValuationProductCount));
            OnPropertyChanged(nameof(ValuationTotalQuantity));
            OnPropertyChanged(nameof(ValuationTotalValue));
            OnPropertyChanged(nameof(ValuationLowStockCount));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another read of the valuation has taken over; this answer is stale and is dropped.
        }
#pragma warning disable CA1031 // A read that fails must not take the screen down with it.
        catch (Exception ex)
        {
            // A read that fails outright must not lose its exception in the void. The log keeps the
            // detail and the owner is told that the valuation could not be read.
            StartupDiagnostics.Write("The stock valuation could not be read.", ex);

            if (!token.IsCancellationRequested)
            {
                StatusMessage = _strings[StringKeys.ErrorUnexpected];
            }
        }
#pragma warning restore CA1031
    }

    partial void OnSearchTextChanged(string value) => ScheduleBalancesLoad();

    partial void OnOnlyLowStockChanged(bool value) => ScheduleBalancesLoad();

    partial void OnMovementProductIdChanged(int value) => ScheduleMovementsLoad();

    partial void OnFromDateTextChanged(string value) => ScheduleMovementsLoad();

    partial void OnToDateTextChanged(string value) => ScheduleMovementsLoad();

    partial void OnAsOfDateTextChanged(string value) => ScheduleValuationLoad();

    /// <summary>
    /// Reads the stock on hand again, and forgets the read that was waiting.
    /// </summary>
    private void ScheduleBalancesLoad() => _ = LoadBalancesAsync(CancellationToken.None);

    /// <summary>
    /// Reads the ledger again, and forgets the read that was waiting.
    /// </summary>
    private void ScheduleMovementsLoad() => _ = LoadMovementsAsync(CancellationToken.None);

    /// <summary>
    /// Reads the valuation again, and forgets the read that was waiting.
    /// </summary>
    private void ScheduleValuationLoad() => _ = LoadValuationAsync(CancellationToken.None);

    partial void OnSelectedCountChanged(StockCountSummary? value)
    {
        OnPropertyChanged(nameof(CanApproveCount));
        OnPropertyChanged(nameof(CanPostCount));
        OnPropertyChanged(nameof(CanCancelCount));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
