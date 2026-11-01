using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Money;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// One product's opening quantity and cost.
/// </summary>
public sealed partial class OpeningStockLineRow : ObservableObject
{
    [ObservableProperty]
    private int _productId;

    [ObservableProperty]
    private string _quantity = "1";

    [ObservableProperty]
    private string _unitCost = string.Empty;

    /// <summary>The name of the chosen product.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>True when the owner has chosen a product on this line.</summary>
    public bool HasProduct => ProductId > 0;

    /// <summary>Raised whenever the owner changes something on the line.</summary>
    public event EventHandler? Changed;

    partial void OnProductIdChanged(int value)
    {
        OnPropertyChanged(nameof(HasProduct));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The opening stock dialog. Opening balances are recorded as auditable opening movements, never as
/// a direct edit of a balance (PRD INV-06).
/// </summary>
public sealed partial class OpeningStockViewModel : ObservableObject
{
    private readonly ICommandHandler<RecordOpeningStockCommand, Result<int>> _recordHandler;
    private readonly IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> _referenceHandler;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private string _businessDateText = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the dialog.</summary>
    public OpeningStockViewModel(
        ICommandHandler<RecordOpeningStockCommand, Result<int>> recordHandler,
        IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> referenceHandler,
        IStrings strings,
        LanguageService languageService)
    {
        _recordHandler = recordHandler;
        _referenceHandler = referenceHandler;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The lines the owner typed.</summary>
    public ObservableCollection<OpeningStockLineRow> Lines { get; } = [];

    /// <summary>The products the owner can pick.</summary>
    public ObservableCollection<InventoryOptionItem> Products { get; } = [];

    /// <summary>Raised when the dialog wants to close itself.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>The title of the dialog.</summary>
    public string Title => _strings[StringKeys.OpeningStockTitle];

    /// <summary>The label on the date field.</summary>
    public string DateLabel => _strings[StringKeys.OpeningStockDate];

    /// <summary>The label on the notes field.</summary>
    public string NotesLabel => _strings[StringKeys.OpeningStockNotes];

    /// <summary>The label on the product picker.</summary>
    public string ProductLabel => _strings[StringKeys.OpeningStockProduct];

    /// <summary>The label on the quantity column.</summary>
    public string QuantityLabel => _strings[StringKeys.OpeningStockQuantity];

    /// <summary>The label on the unit cost column.</summary>
    public string UnitCostLabel => _strings[StringKeys.OpeningStockUnitCost];

    /// <summary>The label on the add-line command.</summary>
    public string AddLineText => _strings[StringKeys.OpeningStockAddLine];

    /// <summary>The label on the remove-line command.</summary>
    public string RemoveLineText => _strings[StringKeys.CommonRemove];

    /// <summary>The label on the submit command.</summary>
    public string SubmitText => _strings[StringKeys.OpeningStockSubmit];

    /// <summary>The label on the cancel command.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>Loads the products the owner can pick.</summary>
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

    /// <summary>Adds an empty line.</summary>
    [RelayCommand]
    private void AddLine()
    {
        var row = new OpeningStockLineRow();
        row.Changed += OnLineChanged;

        Lines.Add(row);
    }

    /// <summary>Removes a line.</summary>
    /// <param name="row">The line to remove.</param>
    [RelayCommand]
    private void RemoveLine(OpeningStockLineRow? row)
    {
        if (row is null)
        {
            return;
        }

        row.Changed -= OnLineChanged;

        Lines.Remove(row);
    }

    /// <summary>Records the opening stock.</summary>
    [RelayCommand]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        StatusMessage = string.Empty;

        if (!InventoryInput.TryReadDate(BusinessDateText, out DateOnly businessDate))
        {
            StatusMessage = _strings[StringKeys.ErrorInventoryQuantity];
            return;
        }

        var lines = new List<OpeningStockLine>();

        foreach (OpeningStockLineRow row in Lines)
        {
            if (row.ProductId <= 0)
            {
                StatusMessage = _strings[StringKeys.ErrorInventoryRequired];
                return;
            }

            if (!InventoryInput.TryReadDecimal(row.Quantity, out decimal quantity) || quantity <= 0m)
            {
                StatusMessage = _strings[StringKeys.ErrorInventoryQuantity];
                return;
            }

            if (!InventoryInput.TryReadMoney(row.UnitCost, out decimal unitCost))
            {
                StatusMessage = _strings[StringKeys.ErrorNegativePrice];
                return;
            }

            lines.Add(new OpeningStockLine(row.ProductId, quantity, new Money(unitCost)));
        }

        if (lines.Count == 0)
        {
            StatusMessage = _strings[StringKeys.ErrorInventoryNoLines];
            return;
        }

        Result<int> result = await _recordHandler.HandleAsync(
            new RecordOpeningStockCommand(lines, businessDate, Notes),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = InventoryErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        CloseRequested?.Invoke(this, true);
    }

    /// <summary>Closes the dialog without recording anything.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private void OnLineChanged(object? sender, EventArgs e)
    {
        if (sender is OpeningStockLineRow row)
        {
            InventoryOptionItem? product = Products.FirstOrDefault(candidate => candidate.Id == row.ProductId);

            row.ProductName = product?.DisplayName ?? string.Empty;

            if (product is not null && string.IsNullOrWhiteSpace(row.UnitCost))
            {
                row.UnitCost = InventoryInput.WriteAmount(product.CostPrice);
            }
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
