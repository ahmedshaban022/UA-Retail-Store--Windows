using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The products screen: the catalogue the shop sells from, with the search the owner actually uses
/// at the counter.
/// </summary>
public sealed partial class ProductsViewModel : ObservableObject
{
    /// <summary>How long the search waits before it asks the database, in milliseconds.</summary>
    private const int SearchDelayMilliseconds = 250;

    /// <summary>How many products one page holds.</summary>
    private const int PageSize = 200;

    private readonly IQueryHandler<SearchProductsQuery, Result<ProductPage>> _searchHandler;
    private readonly ICommandHandler<SetProductActiveCommand, Result> _setActiveHandler;
    private readonly ICommandHandler<ExportProductsCommand, Result<int>> _exportHandler;
    private readonly ICatalogDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    private CancellationTokenSource? _searchCancellation;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private ProductListItem? _selectedProduct;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _totalCount;

    /// <summary>Creates the screen.</summary>
    /// <param name="searchHandler">Reads the product list.</param>
    /// <param name="setActiveHandler">Activates and deactivates products.</param>
    /// <param name="exportHandler">Writes the product list to a file.</param>
    /// <param name="dialogs">Shows the catalogue's dialogs.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public ProductsViewModel(
        IQueryHandler<SearchProductsQuery, Result<ProductPage>> searchHandler,
        ICommandHandler<SetProductActiveCommand, Result> setActiveHandler,
        ICommandHandler<ExportProductsCommand, Result<int>> exportHandler,
        ICatalogDialogs dialogs,
        IStrings strings,
        LanguageService languageService)
    {
        _searchHandler = searchHandler;
        _setActiveHandler = setActiveHandler;
        _exportHandler = exportHandler;
        _dialogs = dialogs;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The products on screen.</summary>
    public ObservableCollection<ProductListItem> Products { get; } = [];

    /// <summary>The text shown inside the empty search box.</summary>
    public string SearchPlaceholder => _strings[StringKeys.ProductsSearch];

    /// <summary>The title of the screen, which also names the grid for a screen reader.</summary>
    public string Title => _strings[StringKeys.ProductsTitle];

    /// <summary>The label on the new-product command.</summary>
    public string NewProductText => _strings[StringKeys.ProductsNew];

    /// <summary>The label on the import command.</summary>
    public string ImportText => _strings[StringKeys.CommonImport];

    /// <summary>The label on the export command.</summary>
    public string ExportText => _strings[StringKeys.CommonExport];

    /// <summary>The label on the edit command.</summary>
    public string EditText => _strings[StringKeys.CommonEdit];

    /// <summary>The label on the activate command.</summary>
    public string ActivateText => _strings[StringKeys.CommonActivate];

    /// <summary>The label on the deactivate command.</summary>
    public string DeactivateText => _strings[StringKeys.CommonDeactivate];

    /// <summary>The label on the show-inactive switch.</summary>
    public string ShowInactiveText => _strings[StringKeys.CommonShowInactive];

    /// <summary>The label of the column that holds the code.</summary>
    public string SkuHeader => _strings[StringKeys.ProductsSku];

    /// <summary>The label of the column that holds the name.</summary>
    public string NameHeader => _strings[StringKeys.ProductsName];

    /// <summary>The label of the column that holds the Arabic name.</summary>
    public string NameArHeader => _strings[StringKeys.ProductsNameAr];

    /// <summary>The label of the column that holds the category.</summary>
    public string CategoryHeader => _strings[StringKeys.ProductsCategory];

    /// <summary>The label of the column that holds the unit.</summary>
    public string UnitHeader => _strings[StringKeys.ProductsUnit];

    /// <summary>The label of the column that holds the selling price.</summary>
    public string SalePriceHeader => _strings[StringKeys.ProductsSalePrice];

    /// <summary>The label of the column that holds the cost.</summary>
    public string CostPriceHeader => _strings[StringKeys.ProductsCostPrice];

    /// <summary>The label of the column that holds the barcode.</summary>
    public string BarcodeHeader => _strings[StringKeys.ProductsBarcode];

    /// <summary>The label of the column that holds the state.</summary>
    public string StatusHeader => _strings[StringKeys.CommonStatus];

    /// <summary>The text shown when the catalogue holds nothing at all.</summary>
    public string EmptyText => _strings[StringKeys.ProductsEmpty];

    /// <summary>The text shown when a search finds nothing.</summary>
    public string NoMatchesText => _strings[StringKeys.ProductsNoMatches];

    /// <summary>The label of the product state badge when the product is active.</summary>
    public string ActiveText => _strings[StringKeys.ProductsActive];

    /// <summary>The label of the product state badge when the product is inactive.</summary>
    public string InactiveText => _strings[StringKeys.ProductsInactive];

    /// <summary>The name written for a product that has no category.</summary>
    public string UncategorizedText => _strings[StringKeys.ProductsUncategorized];

    /// <summary>True when the catalogue holds nothing at all.</summary>
    public bool IsEmpty => !IsBusy && TotalCount == 0 && string.IsNullOrWhiteSpace(SearchText);

    /// <summary>True when a search found nothing.</summary>
    public bool HasNoMatches => !IsBusy && TotalCount == 0 && !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>True when the search box holds nothing, so the placeholder should show.</summary>
    public bool HasNoSearchText => string.IsNullOrEmpty(SearchText);

    /// <summary>The number of products the current filter matches.</summary>
    public string CountText => TotalCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Loads the products the current filter matches.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var filter = new ProductFilter(SearchText, null, IncludeInactive);

            Result<ProductPage> result = await _searchHandler.HandleAsync(
                new SearchProductsQuery(filter, 0, PageSize),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            Products.Clear();

            foreach (ProductListItem product in result.Value.Items)
            {
                Products.Add(product);
            }

            TotalCount = result.Value.TotalCount;
            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            RaiseEmptyStateChanged();
        }
    }

    /// <summary>Creates a new product.</summary>
    [RelayCommand]
    private async Task NewProductAsync(CancellationToken cancellationToken)
    {
        bool saved = await _dialogs.ShowProductEditorAsync(null, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Opens the product that is selected.</summary>
    [RelayCommand]
    private async Task EditSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedProduct is not { } product)
        {
            return;
        }

        bool saved = await _dialogs.ShowProductEditorAsync(product.Id, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Activates or deactivates the product that is selected (PRD PRD-01).</summary>
    [RelayCommand]
    private async Task ToggleActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedProduct is not { } product)
        {
            return;
        }

        bool target = !product.IsActive;

        Result result = await _setActiveHandler.HandleAsync(
            new SetProductActiveCommand(product.Id, target),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        StatusMessage = target
            ? _strings.Format(StringKeys.ProductsActivated, product.Name)
            : _strings.Format(StringKeys.ProductsDeactivated, product.Name);

        await RefreshAsync(cancellationToken);
    }

    /// <summary>Imports products from a CSV file.</summary>
    [RelayCommand]
    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        bool imported = await _dialogs.ShowCsvImportAsync(cancellationToken);

        if (imported)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Writes the filtered product list to a CSV file.</summary>
    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = "products.csv",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var filter = new ProductFilter(SearchText, null, IncludeInactive);

        Result<int> result = await _exportHandler.HandleAsync(
            new ExportProductsCommand(dialog.FileName, filter, BuildHeaders()),
            cancellationToken);

        StatusMessage = result.IsFailure
            ? CatalogErrorMessages.Resolve(_strings, result.Error.Code)
            : _strings.Format(StringKeys.ProductsExported, result.Value);
    }

    /// <summary>
    /// The headings used for the exported file. The same headings are offered to the importer, so a
    /// file this application wrote can be read straight back.
    /// </summary>
    /// <returns>The heading of each field.</returns>
    public IReadOnlyDictionary<string, string> BuildHeaders() => new Dictionary<string, string>
    {
        [ProductCsvMapper.FieldSku] = _strings[StringKeys.ProductsSku],
        [ProductCsvMapper.FieldName] = _strings[StringKeys.ProductsName],
        [ProductCsvMapper.FieldNameAr] = _strings[StringKeys.ProductsNameAr],
        [ProductCsvMapper.FieldCategory] = _strings[StringKeys.ProductsCategory],
        [ProductCsvMapper.FieldUnit] = _strings[StringKeys.ProductsUnit],
        [ProductCsvMapper.FieldTaxCategory] = _strings[StringKeys.ProductEditorTaxCategory],
        [ProductCsvMapper.FieldSalePrice] = _strings[StringKeys.ProductsSalePrice],
        [ProductCsvMapper.FieldCostPrice] = _strings[StringKeys.ProductsCostPrice],
        [ProductCsvMapper.FieldReorderLevel] = _strings[StringKeys.ProductEditorReorderLevel],
        [ProductCsvMapper.FieldBarcode] = _strings[StringKeys.ProductsBarcode],
        [ProductCsvMapper.FieldAdditionalBarcodes] = _strings[StringKeys.ProductEditorAdditionalBarcodes],
        [ProductCsvMapper.FieldDescription] = _strings[StringKeys.ProductEditorDescription],
        [ProductCsvMapper.FieldIsActive] = _strings[StringKeys.CommonStatus],
    };

    partial void OnSearchTextChanged(string value)
    {
        RaiseEmptyStateChanged();
        ScheduleSearch();
    }

    partial void OnIncludeInactiveChanged(bool value) => ScheduleSearch();

    partial void OnSelectedProductChanged(ProductListItem? value) =>
        OnPropertyChanged(nameof(CanEditSelected));

    /// <summary>True when a product is selected and can be opened.</summary>
    public bool CanEditSelected => SelectedProduct is not null;

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

    private void RaiseEmptyStateChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HasNoSearchText));
        OnPropertyChanged(nameof(CountText));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(string.Empty);
}
