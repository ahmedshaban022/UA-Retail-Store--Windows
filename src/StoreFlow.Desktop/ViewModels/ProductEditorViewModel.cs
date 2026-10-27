using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Money;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>One entry in a picker, with the name already written for the current language.</summary>
/// <param name="Id">The identifier of the record.</param>
/// <param name="DisplayName">The name to show.</param>
/// <param name="Precision">The number of decimal places a quantity in this unit may carry.</param>
public sealed record CatalogOptionItem(int Id, string DisplayName, int Precision = 0)
{
    /// <summary>
    /// The name a select shows for this entry.
    /// </summary>
    /// <remarks>
    /// A select shows the selected item's own text. Without this override the closed control would
    /// show the record's generated <c>ToString</c>, which reads as
    /// <c>CatalogOptionItem { Id = 0, DisplayName = ... }</c> instead of the name of the record.
    /// </remarks>
    public override string ToString() => DisplayName;
}

/// <summary>
/// The product editor: everything about one product, and the record of what was done to it.
/// </summary>
public sealed partial class ProductEditorViewModel : ObservableObject
{
    /// <summary>The identifier that means "no category".</summary>
    public const int NoCategoryId = 0;

    private readonly ICommandHandler<SaveProductCommand, Result<int>> _saveHandler;
    private readonly IQueryHandler<GetProductQuery, Result<ProductDetails>> _getHandler;
    private readonly IQueryHandler<GetCatalogReferenceDataQuery, Result<CatalogReferenceData>> _referenceHandler;
    private readonly IQueryHandler<GetProductAuditQuery, Result<IReadOnlyList<AuditEntryView>>> _auditHandler;
    private readonly IProductImageStore _imageStore;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    private string? _originalImagePath;
    private string? _pendingImagePath;

    [ObservableProperty]
    private string _sku = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _nameAr = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private int _categoryId = NoCategoryId;

    [ObservableProperty]
    private int _unitId;

    [ObservableProperty]
    private int _taxCategoryId;

    [ObservableProperty]
    private string _salePrice = string.Empty;

    [ObservableProperty]
    private string _costPrice = string.Empty;

    [ObservableProperty]
    private string _reorderLevel = "0";

    [ObservableProperty]
    private string _barcode = string.Empty;

    [ObservableProperty]
    private string _newBarcode = string.Empty;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private string? _imagePath;

    [ObservableProperty]
    private BitmapSource? _imagePreview;

    [ObservableProperty]
    private string _skuError = string.Empty;

    [ObservableProperty]
    private string _nameError = string.Empty;

    [ObservableProperty]
    private string _nameArError = string.Empty;

    [ObservableProperty]
    private string _descriptionError = string.Empty;

    [ObservableProperty]
    private string _salePriceError = string.Empty;

    [ObservableProperty]
    private string _costPriceError = string.Empty;

    [ObservableProperty]
    private string _reorderLevelError = string.Empty;

    [ObservableProperty]
    private string _barcodeError = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _wasSaved;

    /// <summary>Raised when the editor should close. The argument is true when the product was saved.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>Creates the editor.</summary>
    /// <param name="saveHandler">Saves the product.</param>
    /// <param name="getHandler">Reads the product.</param>
    /// <param name="referenceHandler">Reads the reference data the pickers need.</param>
    /// <param name="auditHandler">Reads the product's history.</param>
    /// <param name="imageStore">Stores the product's picture.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public ProductEditorViewModel(
        ICommandHandler<SaveProductCommand, Result<int>> saveHandler,
        IQueryHandler<GetProductQuery, Result<ProductDetails>> getHandler,
        IQueryHandler<GetCatalogReferenceDataQuery, Result<CatalogReferenceData>> referenceHandler,
        IQueryHandler<GetProductAuditQuery, Result<IReadOnlyList<AuditEntryView>>> auditHandler,
        IProductImageStore imageStore,
        IStrings strings,
        LanguageService languageService)
    {
        _saveHandler = saveHandler;
        _getHandler = getHandler;
        _referenceHandler = referenceHandler;
        _auditHandler = auditHandler;
        _imageStore = imageStore;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The identifier of the product being edited, or null for a new one.</summary>
    public int? ProductId { get; private set; }

    /// <summary>The window title.</summary>
    public string Title => ProductId is null
        ? _strings[StringKeys.ProductEditorTitleNew]
        : _strings[StringKeys.ProductEditorTitleEdit];

    /// <summary>The label on the save command.</summary>
    public string SaveText => _strings[StringKeys.CommonSave];

    /// <summary>The label on the cancel command.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>The label of the code field.</summary>
    public string SkuLabel => _strings[StringKeys.ProductEditorSku];

    /// <summary>The label of the name field.</summary>
    public string NameLabel => _strings[StringKeys.ProductEditorName];

    /// <summary>The label of the Arabic name field.</summary>
    public string NameArLabel => _strings[StringKeys.ProductEditorNameAr];

    /// <summary>The label of the description field.</summary>
    public string DescriptionLabel => _strings[StringKeys.ProductEditorDescription];

    /// <summary>The label of the category field.</summary>
    public string CategoryLabel => _strings[StringKeys.ProductEditorCategory];

    /// <summary>The label of the unit field.</summary>
    public string UnitLabel => _strings[StringKeys.ProductEditorUnit];

    /// <summary>The label of the tax category field.</summary>
    public string TaxCategoryLabel => _strings[StringKeys.ProductEditorTaxCategory];

    /// <summary>The label of the selling price field.</summary>
    public string SalePriceLabel => _strings[StringKeys.ProductEditorSalePrice];

    /// <summary>The label of the cost price field.</summary>
    public string CostPriceLabel => _strings[StringKeys.ProductEditorCostPrice];

    /// <summary>The label of the reorder level field.</summary>
    public string ReorderLevelLabel => _strings[StringKeys.ProductEditorReorderLevel];

    /// <summary>The label of the barcode field.</summary>
    public string BarcodeLabel => _strings[StringKeys.ProductEditorBarcode];

    /// <summary>The label of the extra barcodes field.</summary>
    public string AdditionalBarcodesLabel => _strings[StringKeys.ProductEditorAdditionalBarcodes];

    /// <summary>The label of the add-barcode command.</summary>
    public string AddBarcodeText => _strings[StringKeys.ProductEditorAddBarcode];

    /// <summary>The label of the remove-barcode command.</summary>
    public string RemoveText => _strings[StringKeys.CommonRemove];

    /// <summary>The label of the picture field.</summary>
    public string ImageLabel => _strings[StringKeys.ProductEditorImage];

    /// <summary>The label of the choose-picture command.</summary>
    public string ChooseImageText => _strings[StringKeys.ProductEditorChooseImage];

    /// <summary>The label of the remove-picture command.</summary>
    public string RemoveImageText => _strings[StringKeys.ProductEditorRemoveImage];

    /// <summary>The label of the active switch.</summary>
    public string ActiveLabel => _strings[StringKeys.ProductEditorActive];

    /// <summary>The label of the history section.</summary>
    public string HistoryLabel => _strings[StringKeys.ProductEditorHistory];

    /// <summary>The text shown when a product has no history yet.</summary>
    public string HistoryEmptyText => _strings[StringKeys.ProductEditorHistoryEmpty];

    /// <summary>The name written for a product that has no category.</summary>
    public string UncategorizedText => _strings[StringKeys.ProductsUncategorized];

    /// <summary>The categories the owner can pick from.</summary>
    public ObservableCollection<CatalogOptionItem> Categories { get; } = [];

    /// <summary>The units the owner can pick from.</summary>
    public ObservableCollection<CatalogOptionItem> Units { get; } = [];

    /// <summary>The tax categories the owner can pick from.</summary>
    public ObservableCollection<CatalogOptionItem> TaxCategories { get; } = [];

    /// <summary>The product's other barcodes.</summary>
    public ObservableCollection<string> AdditionalBarcodes { get; } = [];

    /// <summary>The product's history, newest first.</summary>
    public ObservableCollection<AuditEntryView> History { get; } = [];

    /// <summary>True when the product has no history yet.</summary>
    public bool HasNoHistory => History.Count == 0;

    /// <summary>Loads the reference data and, when editing, the product itself.</summary>
    /// <param name="productId">The product to edit, or null for a new one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [RelayCommand]
    public async Task LoadAsync(int? productId, CancellationToken cancellationToken)
    {
        ProductId = productId;
        IsBusy = true;

        try
        {
            Result<CatalogReferenceData> reference = await _referenceHandler.HandleAsync(
                new GetCatalogReferenceDataQuery(),
                cancellationToken);

            if (reference.IsSuccess)
            {
                ApplyReferenceData(reference.Value);
            }

            if (productId is int id)
            {
                Result<ProductDetails> product = await _getHandler.HandleAsync(
                    new GetProductQuery(id),
                    cancellationToken);

                if (product.IsFailure)
                {
                    StatusMessage = CatalogErrorMessages.Resolve(_strings, product.Error.Code);
                    return;
                }

                ApplyProduct(product.Value);
                await LoadHistoryAsync(id, cancellationToken);
            }
            else
            {
                IsActive = true;
                OnPropertyChanged(nameof(Title));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Adds another barcode to the product.</summary>
    [RelayCommand]
    private void AddBarcode()
    {
        string candidate = NewBarcode.Trim();

        if (candidate.Length == 0)
        {
            return;
        }

        bool already = AdditionalBarcodes.Any(existing =>
            string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase));

        if (already)
        {
            BarcodeError = _strings.Format(StringKeys.ErrorDuplicateBarcode, BidiText.Isolate(candidate));
            return;
        }

        AdditionalBarcodes.Add(candidate);
        NewBarcode = string.Empty;
        BarcodeError = string.Empty;
    }

    /// <summary>Removes one of the product's other barcodes.</summary>
    /// <param name="barcode">The barcode to remove.</param>
    [RelayCommand]
    private void RemoveBarcode(string? barcode)
    {
        if (barcode is null)
        {
            return;
        }

        AdditionalBarcodes.Remove(barcode);
    }

    /// <summary>Chooses a picture for the product and copies it into the store's media folder.</summary>
    [RelayCommand]
    private async Task ChooseImageAsync(CancellationToken cancellationToken)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Pictures (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Result<string> saved = await _imageStore.SaveAsync(dialog.FileName, cancellationToken);

        if (saved.IsFailure)
        {
            StatusMessage = CatalogErrorMessages.Resolve(_strings, saved.Error.Code);
            return;
        }

        // A picture chosen earlier in this session is now replaced, so it is no longer needed.
        await DiscardPendingImageAsync(cancellationToken);

        _pendingImagePath = saved.Value;
        ImagePath = saved.Value;
        ImagePreview = LoadPreview(saved.Value);

        StatusMessage = string.Empty;
    }

    /// <summary>Removes the product's picture.</summary>
    [RelayCommand]
    private async Task RemoveImageAsync(CancellationToken cancellationToken)
    {
        if (ImagePath is null)
        {
            return;
        }

        await DiscardPendingImageAsync(cancellationToken);

        ImagePath = null;
        ImagePreview = null;
    }

    /// <summary>Saves the product.</summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        ClearErrors();

        if (!TryReadMoney(SalePrice, out decimal salePrice))
        {
            SalePriceError = _strings[StringKeys.ErrorNegativePrice];
            return;
        }

        if (!TryReadMoney(CostPrice, out decimal costPrice))
        {
            CostPriceError = _strings[StringKeys.ErrorNegativePrice];
            return;
        }

        if (!TryReadDecimal(ReorderLevel, out decimal reorderLevel))
        {
            ReorderLevelError = _strings[StringKeys.ErrorNegativeQuantity];
            return;
        }

        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateProduct(
            Sku,
            Name,
            NameAr,
            Description,
            new Money(salePrice),
            new Money(costPrice),
            reorderLevel,
            Barcode,
            AdditionalBarcodes,
            PrecisionForUnit());

        if (issues.Count > 0)
        {
            ShowIssues(issues);
            return;
        }

        var command = new SaveProductCommand(
            ProductId,
            Sku,
            Name,
            NameAr,
            Description,
            CategoryId == NoCategoryId ? null : CategoryId,
            UnitId,
            TaxCategoryId,
            new Money(salePrice),
            new Money(costPrice),
            reorderLevel,
            Barcode,
            AdditionalBarcodes.ToList(),
            ImagePath,
            IsActive);

        Result<int> result = await _saveHandler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            ShowFailure(result.Error.Code);
            return;
        }

        // The picture is now referenced by a saved product, so it must survive the editor closing.
        _pendingImagePath = null;

        WasSaved = true;
        CloseRequested?.Invoke(this, true);
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Closes the editor without saving, and removes a picture that was never used.</summary>
    [RelayCommand]
    private async Task CancelAsync(CancellationToken cancellationToken)
    {
        await DiscardPendingImageAsync(cancellationToken);

        WasSaved = false;
        CloseRequested?.Invoke(this, false);
    }

    private void ApplyReferenceData(CatalogReferenceData data)
    {
        Categories.Clear();
        Categories.Add(new CatalogOptionItem(NoCategoryId, UncategorizedText));

        foreach (CatalogOption option in data.Categories)
        {
            Categories.Add(new CatalogOptionItem(option.Id, DisplayName(option)));
        }

        Units.Clear();

        foreach (CatalogOption option in data.Units)
        {
            Units.Add(new CatalogOptionItem(option.Id, DisplayName(option), option.Precision));
        }

        TaxCategories.Clear();

        foreach (CatalogOption option in data.TaxCategories)
        {
            TaxCategories.Add(new CatalogOptionItem(option.Id, DisplayName(option)));
        }

        if (UnitId == 0 && data.DefaultUnitId != 0)
        {
            UnitId = data.DefaultUnitId;
        }

        if (TaxCategoryId == 0 && data.DefaultTaxCategoryId != 0)
        {
            TaxCategoryId = data.DefaultTaxCategoryId;
        }
    }

    private void ApplyProduct(ProductDetails product)
    {
        Sku = product.Sku;
        Name = product.Name;
        NameAr = product.NameAr ?? string.Empty;
        Description = product.Description ?? string.Empty;
        CategoryId = product.CategoryId ?? NoCategoryId;
        UnitId = product.UnitId;
        TaxCategoryId = product.TaxCategoryId;
        SalePrice = product.SalePrice.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        CostPrice = product.CostPrice.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        ReorderLevel = product.ReorderLevel.ToString("0.################", CultureInfo.InvariantCulture);
        Barcode = product.Barcode ?? string.Empty;
        IsActive = product.IsActive;

        AdditionalBarcodes.Clear();

        foreach (string barcode in product.AdditionalBarcodes)
        {
            AdditionalBarcodes.Add(barcode);
        }

        _originalImagePath = product.ImagePath;
        ImagePath = product.ImagePath;
        ImagePreview = product.ImagePath is null ? null : LoadPreview(product.ImagePath);
    }

    private async Task LoadHistoryAsync(int productId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AuditEntryView>> result = await _auditHandler.HandleAsync(
            new GetProductAuditQuery(productId, 20),
            cancellationToken);

        History.Clear();

        if (result.IsSuccess)
        {
            foreach (AuditEntryView entry in result.Value)
            {
                History.Add(entry);
            }
        }

        OnPropertyChanged(nameof(HasNoHistory));
    }

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues)
    {
        foreach (ValidationIssue issue in issues)
        {
            string message = CatalogErrorMessages.Resolve(_strings, issue.Code, LabelFor(issue.Field), MaximumFor(issue.Field));

            switch (issue.Field)
            {
                case "sku":
                    SkuError = message;
                    break;
                case "name":
                    NameError = message;
                    break;
                case "nameAr":
                    NameArError = message;
                    break;
                case "description":
                    DescriptionError = message;
                    break;
                case "salePrice":
                    SalePriceError = message;
                    break;
                case "costPrice":
                    CostPriceError = message;
                    break;
                case "reorderLevel":
                    ReorderLevelError = message;
                    break;
                case "barcode":
                    BarcodeError = message;
                    break;
            }
        }
    }

    private void ShowFailure(string code)
    {
        string message = code switch
        {
            CatalogValidation.DuplicateSkuCode => _strings.Format(StringKeys.ErrorDuplicateSku, BidiText.Isolate(Sku.Trim())),
            CatalogValidation.DuplicateBarcodeCode => _strings.Format(StringKeys.ErrorDuplicateBarcode, BidiText.Isolate(Barcode.Trim())),
            _ => CatalogErrorMessages.Resolve(_strings, code),
        };

        StatusMessage = message;

        if (code == CatalogValidation.DuplicateSkuCode)
        {
            SkuError = message;
        }
        else if (code == CatalogValidation.DuplicateBarcodeCode)
        {
            BarcodeError = message;
        }
    }

    private void ClearErrors()
    {
        SkuError = string.Empty;
        NameError = string.Empty;
        NameArError = string.Empty;
        DescriptionError = string.Empty;
        SalePriceError = string.Empty;
        CostPriceError = string.Empty;
        ReorderLevelError = string.Empty;
        BarcodeError = string.Empty;
        StatusMessage = string.Empty;
    }

    private string LabelFor(string field) => field switch
    {
        "sku" => SkuLabel,
        "name" => NameLabel,
        "nameAr" => NameArLabel,
        "description" => DescriptionLabel,
        "salePrice" => SalePriceLabel,
        "costPrice" => CostPriceLabel,
        "reorderLevel" => ReorderLevelLabel,
        "barcode" => BarcodeLabel,
        _ => field,
    };

    private static int MaximumFor(string field) => field switch
    {
        "sku" => CatalogValidation.MaxSkuLength,
        "name" => CatalogValidation.MaxNameLength,
        "nameAr" => CatalogValidation.MaxArabicNameLength,
        "description" => CatalogValidation.MaxDescriptionLength,
        _ => 0,
    };

    private int PrecisionForUnit()
    {
        foreach (CatalogOptionItem option in Units)
        {
            if (option.Id == UnitId)
            {
                return option.Precision;
            }
        }

        return 0;
    }

    private string DisplayName(CatalogOption option)
    {
        bool arabic = _languageService.CurrentLanguage.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        if (arabic && !string.IsNullOrWhiteSpace(option.NameAr))
        {
            return option.NameAr;
        }

        return option.Name;
    }

    private BitmapSource? LoadPreview(string relativePath)
    {
        try
        {
            string absolute = _imageStore.ResolvePath(relativePath);

            if (!File.Exists(absolute))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(absolute, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task DiscardPendingImageAsync(CancellationToken cancellationToken)
    {
        if (_pendingImagePath is null || string.Equals(_pendingImagePath, _originalImagePath, StringComparison.Ordinal))
        {
            _pendingImagePath = null;
            return;
        }

        string path = _pendingImagePath;
        _pendingImagePath = null;

        await _imageStore.DeleteAsync(path, cancellationToken);
    }

    private static bool TryReadDecimal(string? text, out decimal value) =>
        decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);

    private static bool TryReadMoney(string? text, out decimal value)
    {
        if (!TryReadDecimal(text, out value))
        {
            value = 0m;
            return false;
        }

        return value >= 0m;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(string.Empty);
}
