using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// One counted product inside a stock count session.
/// </summary>
public sealed partial class StockCountLineRow : ObservableObject
{
    [ObservableProperty]
    private int _productId;

    [ObservableProperty]
    private string _countedQuantity = "0";

    /// <summary>The name of the chosen product.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>The unit the quantity is counted in.</summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>True when the owner has chosen a product on this line.</summary>
    public bool HasProduct => ProductId > 0;

    /// <summary>Raised whenever the owner changes something on the line.</summary>
    public event EventHandler? Changed;

    partial void OnProductIdChanged(int value)
    {
        OnPropertyChanged(nameof(HasProduct));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCountedQuantityChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// The stock count dialog: the owner counts what is on the shelf, and the difference waits for an
/// approval before it touches posted stock (PRD INV-05).
/// </summary>
public sealed partial class StockCountViewModel : ObservableObject
{
    private readonly ICommandHandler<SaveStockCountCommand, Result<int>> _saveHandler;
    private readonly ICommandHandler<SubmitStockCountCommand, Result> _submitHandler;
    private readonly IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> _referenceHandler;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private string _businessDateText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the dialog.</summary>
    public StockCountViewModel(
        ICommandHandler<SaveStockCountCommand, Result<int>> saveHandler,
        ICommandHandler<SubmitStockCountCommand, Result> submitHandler,
        IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> referenceHandler,
        IStrings strings,
        LanguageService languageService)
    {
        _saveHandler = saveHandler;
        _submitHandler = submitHandler;
        _referenceHandler = referenceHandler;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The counted products.</summary>
    public ObservableCollection<StockCountLineRow> Lines { get; } = [];

    /// <summary>The products the owner can pick.</summary>
    public ObservableCollection<InventoryOptionItem> Products { get; } = [];

    /// <summary>Raised when the dialog wants to close itself.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>The title of the dialog.</summary>
    public string Title => _strings[StringKeys.StockCountTitle];

    /// <summary>The label on the date field.</summary>
    public string DateLabel => _strings[StringKeys.StockCountDate];

    /// <summary>The label on the product picker.</summary>
    public string ProductLabel => _strings[StringKeys.StockCountProduct];

    /// <summary>The label on the counted quantity column.</summary>
    public string CountedLabel => _strings[StringKeys.StockCountCounted];

    /// <summary>The label on the add-line command.</summary>
    public string AddLineText => _strings[StringKeys.StockCountAddLine];

    /// <summary>The label on the remove-line command.</summary>
    public string RemoveLineText => _strings[StringKeys.CommonRemove];

    /// <summary>The label on the save command.</summary>
    public string SaveText => _strings[StringKeys.StockCountSave];

    /// <summary>The label on the submit command.</summary>
    public string SubmitText => _strings[StringKeys.StockCountSubmit];

    /// <summary>The label on the cancel command.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>Loads the products the owner can count.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        BusinessDateText = InventoryInput.WriteDate(DateOnly.FromDateTime(DateTime.Today));

        try
        {
            Result<InventoryReferenceData> reference = await _referenceHandler.HandleAsync(
                new GetInventoryReferenceDataQuery(),
                cancellationToken);

            if (reference.IsFailure)
            {
                StatusMessage = InventoryErrorMessages.Resolve(_strings, reference.Error.Code);
                return;
            }

            Products.Clear();

            foreach (InventoryReferenceProduct product in reference.Value.Products)
            {
                string name = _languageService.CurrentLanguage == "ar" && !string.IsNullOrWhiteSpace(product.NameAr)
                    ? product.NameAr
                    : product.Name;

                Products.Add(new InventoryOptionItem(product.Id, product.Sku + " - " + name, product.QuantityScale, product.CostPrice.Amount, product.UnitName));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Adds an empty counted product.</summary>
    [RelayCommand]
    private void AddLine()
    {
        var row = new StockCountLineRow();
        row.Changed += OnLineChanged;

        Lines.Add(row);
    }

    /// <summary>Removes a counted product.</summary>
    /// <param name="row">The line to remove.</param>
    [RelayCommand]
    private void RemoveLine(StockCountLineRow? row)
    {
        if (row is null)
        {
            return;
        }

        row.Changed -= OnLineChanged;

        Lines.Remove(row);
    }

    /// <summary>Saves the count as a draft.</summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        int? saved = await PersistAsync(cancellationToken);

        if (saved is not null)
        {
            CloseRequested?.Invoke(this, true);
        }
    }

    /// <summary>Saves the count and sends it for approval.</summary>
    [RelayCommand]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        int? saved = await PersistAsync(cancellationToken);

        if (saved is null)
        {
            return;
        }

        Result result = await _submitHandler.HandleAsync(new SubmitStockCountCommand(saved.Value), cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = InventoryErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        CloseRequested?.Invoke(this, true);
    }

    /// <summary>Closes the dialog without saving.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private async Task<int?> PersistAsync(CancellationToken cancellationToken)
    {
        StatusMessage = string.Empty;

        if (!InventoryInput.TryReadDate(BusinessDateText, out DateOnly businessDate))
        {
            StatusMessage = _strings[StringKeys.ErrorInventoryQuantity];
            return null;
        }

        var lines = new List<StockCountLineInput>();

        foreach (StockCountLineRow row in Lines)
        {
            if (row.ProductId <= 0)
            {
                StatusMessage = _strings[StringKeys.ErrorInventoryRequired];
                return null;
            }

            if (!InventoryInput.TryReadDecimal(row.CountedQuantity, out decimal counted) || counted < 0m)
            {
                StatusMessage = _strings[StringKeys.ErrorInventoryQuantity];
                return null;
            }

            lines.Add(new StockCountLineInput(row.ProductId, counted));
        }

        if (lines.Count == 0)
        {
            StatusMessage = _strings[StringKeys.StockCountEmpty];
            return null;
        }

        Result<int> result = await _saveHandler.HandleAsync(
            new SaveStockCountCommand(null, businessDate, lines),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = InventoryErrorMessages.Resolve(_strings, result.Error.Code);
            return null;
        }

        return result.Value;
    }

    private void OnLineChanged(object? sender, EventArgs e)
    {
        if (sender is StockCountLineRow row)
        {
            InventoryOptionItem? product = Products.FirstOrDefault(candidate => candidate.Id == row.ProductId);

            row.ProductName = product?.DisplayName ?? string.Empty;
            row.UnitName = product?.UnitName ?? string.Empty;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
