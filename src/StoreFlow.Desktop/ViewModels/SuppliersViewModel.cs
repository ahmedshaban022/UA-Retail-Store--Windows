using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The suppliers screen. A supplier records who the shop buys from; the purchase documents that
/// will use it arrive with the inventory phase.
/// </summary>
public sealed partial class SuppliersViewModel : ObservableObject
{
    /// <summary>How long the search waits before it asks the database, in milliseconds.</summary>
    private const int SearchDelayMilliseconds = 250;

    private readonly IQueryHandler<ListSuppliersQuery, Result<IReadOnlyList<SupplierSummary>>> _query;
    private readonly ICommandHandler<SetSupplierActiveCommand, Result> _setActiveHandler;
    private readonly ICatalogDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    private CancellationTokenSource? _searchCancellation;

    [ObservableProperty]
    private SupplierSummary? _selectedSupplier;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the screen.</summary>
    /// <param name="query">Reads the suppliers.</param>
    /// <param name="setActiveHandler">Activates and deactivates suppliers.</param>
    /// <param name="dialogs">Shows the catalogue's dialogs.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public SuppliersViewModel(
        IQueryHandler<ListSuppliersQuery, Result<IReadOnlyList<SupplierSummary>>> query,
        ICommandHandler<SetSupplierActiveCommand, Result> setActiveHandler,
        ICatalogDialogs dialogs,
        IStrings strings,
        LanguageService languageService)
    {
        _query = query;
        _setActiveHandler = setActiveHandler;
        _dialogs = dialogs;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The suppliers on screen.</summary>
    public ObservableCollection<SupplierSummary> Suppliers { get; } = [];

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.SuppliersTitle];

    /// <summary>The label on the new-supplier command.</summary>
    public string NewText => _strings[StringKeys.SuppliersNew];

    /// <summary>The label on the edit command.</summary>
    public string EditText => _strings[StringKeys.CommonEdit];

    /// <summary>The label on the activate command.</summary>
    public string ActivateText => _strings[StringKeys.CommonActivate];

    /// <summary>The label on the deactivate command.</summary>
    public string DeactivateText => _strings[StringKeys.CommonDeactivate];

    /// <summary>The label on the show-inactive switch.</summary>
    public string ShowInactiveText => _strings[StringKeys.CommonShowInactive];

    /// <summary>The text shown inside the empty search box.</summary>
    public string SearchPlaceholder => _strings[StringKeys.CommonSearch];

    /// <summary>True when the search box holds nothing, so the placeholder should show.</summary>
    public bool HasNoSearchText => string.IsNullOrEmpty(SearchText);

    /// <summary>The label of the column that holds the name.</summary>
    public string NameHeader => _strings[StringKeys.SuppliersName];

    /// <summary>The label of the column that holds the Arabic name.</summary>
    public string NameArHeader => _strings[StringKeys.SuppliersNameAr];

    /// <summary>The label of the column that holds the contact number.</summary>
    public string PhoneHeader => _strings[StringKeys.SuppliersPhone];

    /// <summary>The label of the column that holds the state.</summary>
    public string StatusHeader => _strings[StringKeys.CommonStatus];

    /// <summary>The text shown when the shop has no suppliers yet.</summary>
    public string EmptyText => _strings[StringKeys.SuppliersEmpty];

    /// <summary>The label of the state badge when the supplier is active.</summary>
    public string ActiveText => _strings[StringKeys.ProductsActive];

    /// <summary>The label of the state badge when the supplier is inactive.</summary>
    public string InactiveText => _strings[StringKeys.ProductsInactive];

    /// <summary>True when the shop has no suppliers yet.</summary>
    public bool IsEmpty => !IsBusy && Suppliers.Count == 0;

    /// <summary>True when a supplier is selected and can be opened.</summary>
    public bool CanEditSelected => SelectedSupplier is not null;

    /// <summary>Loads the suppliers.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result<IReadOnlyList<SupplierSummary>> result = await _query.HandleAsync(
                new ListSuppliersQuery(SearchText, IncludeInactive),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            Suppliers.Clear();

            foreach (SupplierSummary supplier in result.Value)
            {
                Suppliers.Add(supplier);
            }

            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Creates a new supplier.</summary>
    [RelayCommand]
    private async Task NewAsync(CancellationToken cancellationToken)
    {
        bool saved = await _dialogs.ShowReferenceEditorAsync(CatalogReferenceKind.Supplier, null, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Opens the supplier that is selected.</summary>
    [RelayCommand]
    private async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedSupplier is not { } supplier)
        {
            return;
        }

        bool saved = await _dialogs.ShowReferenceEditorAsync(CatalogReferenceKind.Supplier, supplier.Id, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Activates or deactivates the supplier that is selected.</summary>
    [RelayCommand]
    private async Task ToggleActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedSupplier is not { } supplier)
        {
            return;
        }

        bool target = !supplier.IsActive;

        Result result = await _setActiveHandler.HandleAsync(
            new SetSupplierActiveCommand(supplier.Id, target),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        StatusMessage = target
            ? _strings.Format(StringKeys.ProductsActivated, supplier.Name)
            : _strings.Format(StringKeys.ProductsDeactivated, supplier.Name);

        await RefreshAsync(cancellationToken);
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasNoSearchText));

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();

        CancellationToken token = _searchCancellation.Token;

        _ = DebounceAsync(token);
    }

    partial void OnIncludeInactiveChanged(bool value) => ScheduleSearch();

    partial void OnSelectedSupplierChanged(SupplierSummary? value) =>
        OnPropertyChanged(nameof(CanEditSelected));

    private void ScheduleSearch()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();

        CancellationToken token = _searchCancellation.Token;

        _ = DebounceAsync(token);
    }

    private async Task DebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDelayMilliseconds, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(string.Empty);
}
