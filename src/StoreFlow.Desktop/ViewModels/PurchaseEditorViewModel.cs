using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>One entry in an inventory picker, with the name already written for the current language.</summary>
/// <param name="Id">The identifier of the record.</param>
/// <param name="DisplayName">The name to show.</param>
/// <param name="Precision">The number of decimal places a quantity in this unit may carry.</param>
/// <param name="CostPrice">The current cost price, used as the default cost on a new line.</param>
/// <param name="UnitName">The name of the selling unit.</param>
/// <param name="QuantityOnHand">How much of the product the books say is on the shelf.</param>
public sealed record InventoryOptionItem(
    int Id,
    string DisplayName,
    int Precision = 0,
    decimal CostPrice = 0m,
    string UnitName = "",
    decimal QuantityOnHand = 0m)
{
    /// <summary>The name a select shows for this entry.</summary>
    public override string ToString() => DisplayName;
}

/// <summary>
/// One line of a purchase document, as the owner types it.
/// </summary>
public sealed partial class PurchaseLineRow : ObservableObject
{
    [ObservableProperty]
    private int _productId;

    [ObservableProperty]
    private string _quantity = "1";

    [ObservableProperty]
    private string _unitCost = string.Empty;

    [ObservableProperty]
    private string _tax = "0.00";

    /// <summary>The name of the chosen product.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>The precision of the chosen product's unit.</summary>
    public int Precision { get; set; }

    /// <summary>True when the owner has chosen a product on this line.</summary>
    public bool HasProduct => ProductId > 0;

    /// <summary>The total of the line: quantity times unit cost, plus tax.</summary>
    public decimal LineTotal
    {
        get
        {
            if (!InventoryInput.TryReadDecimal(Quantity, out decimal quantity)
                || !InventoryInput.TryReadMoney(UnitCost, out decimal unitCost)
                || !InventoryInput.TryReadMoney(Tax, out decimal tax))
            {
                return 0m;
            }

            return (quantity * unitCost) + tax;
        }
    }

    /// <summary>Raised whenever the owner changes something on the line.</summary>
    public event EventHandler? Changed;

    partial void OnProductIdChanged(int value)
    {
        OnPropertyChanged(nameof(HasProduct));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnQuantityChanged(string value)
    {
        OnPropertyChanged(nameof(LineTotal));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnUnitCostChanged(string value)
    {
        OnPropertyChanged(nameof(LineTotal));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnTaxChanged(string value)
    {
        OnPropertyChanged(nameof(LineTotal));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The purchase editor: one document, its lines, and the totals that follow from them.
/// </summary>
public sealed partial class PurchaseEditorViewModel : ObservableObject
{
    private readonly ICommandHandler<SavePurchaseCommand, Result<int>> _saveHandler;
    private readonly ICommandHandler<PostPurchaseCommand, Result<int>> _postHandler;
    private readonly IQueryHandler<GetPurchaseQuery, Result<PurchaseDetails>> _getHandler;
    private readonly IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> _referenceHandler;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private int? _purchaseId;

    [ObservableProperty]
    private int _supplierId;

    [ObservableProperty]
    private string _businessDateText = string.Empty;

    [ObservableProperty]
    private string _discountText = "0.00";

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPosted;

    /// <summary>Creates the editor.</summary>
    public PurchaseEditorViewModel(
        ICommandHandler<SavePurchaseCommand, Result<int>> saveHandler,
        ICommandHandler<PostPurchaseCommand, Result<int>> postHandler,
        IQueryHandler<GetPurchaseQuery, Result<PurchaseDetails>> getHandler,
        IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> referenceHandler,
        IStrings strings,
        LanguageService languageService)
    {
        _saveHandler = saveHandler;
        _postHandler = postHandler;
        _getHandler = getHandler;
        _referenceHandler = referenceHandler;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// The total follows the discount as the owner types it, the way it follows the lines.
    /// </summary>
    /// <param name="value">The new text of the discount field.</param>
    partial void OnDiscountTextChanged(string value) => OnPropertyChanged(nameof(TotalAmount));

    /// <summary>The lines on the document.</summary>
    public ObservableCollection<PurchaseLineRow> Lines { get; } = [];

    /// <summary>The products the owner can pick on a line.</summary>
    public ObservableCollection<InventoryOptionItem> Products { get; } = [];

    /// <summary>The suppliers the owner can pick on the document.</summary>
    public ObservableCollection<InventoryOptionItem> Suppliers { get; } = [];

    /// <summary>Raised when the editor wants to close itself.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>The title of the editor.</summary>
    public string Title => PurchaseId is null
        ? _strings[StringKeys.PurchaseEditorTitleNew]
        : _strings[StringKeys.PurchaseEditorTitleEdit];

    /// <summary>The label on the supplier picker.</summary>
    public string SupplierLabel => _strings[StringKeys.PurchaseEditorSupplier];

    /// <summary>The label on the date field.</summary>
    public string DateLabel => _strings[StringKeys.PurchaseEditorDate];

    /// <summary>The label on the discount field.</summary>
    public string DiscountLabel => _strings[StringKeys.PurchaseEditorDiscount];

    /// <summary>The label on the notes field.</summary>
    public string NotesLabel => _strings[StringKeys.PurchaseEditorNotes];

    /// <summary>The label on the product picker.</summary>
    public string ProductLabel => _strings[StringKeys.PurchaseEditorProduct];

    /// <summary>The label on the quantity column.</summary>
    public string QuantityLabel => _strings[StringKeys.PurchaseEditorQuantity];

    /// <summary>The label on the unit cost column.</summary>
    public string UnitCostLabel => _strings[StringKeys.PurchaseEditorUnitCost];

    /// <summary>The label on the tax column.</summary>
    public string TaxLabel => _strings[StringKeys.PurchaseEditorTax];

    /// <summary>The label on the line total column.</summary>
    public string LineTotalLabel => _strings[StringKeys.PurchaseEditorLineTotal];

    /// <summary>The label on the add-line command.</summary>
    public string AddLineText => _strings[StringKeys.PurchaseEditorAddLine];

    /// <summary>The label on the remove-line command.</summary>
    public string RemoveLineText => _strings[StringKeys.PurchaseEditorRemoveLine];

    /// <summary>The label on the subtotal.</summary>
    public string SubtotalLabel => _strings[StringKeys.PurchaseEditorSubtotal];

    /// <summary>The label on the total.</summary>
    public string TotalLabel => _strings[StringKeys.PurchaseEditorTotal];

    /// <summary>The label on the save command.</summary>
    public string SaveText => _strings[StringKeys.PurchaseEditorSaveDraft];

    /// <summary>The label on the save-and-post command.</summary>
    public string SaveAndPostText => _strings[StringKeys.PurchaseEditorSaveAndPost];

    /// <summary>The label on the cancel command.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>The label on the close command.</summary>
    public string CloseText => _strings[StringKeys.CommonClose];

    /// <summary>The label on the status column.</summary>
    public string StatusLabel => _strings[StringKeys.CommonStatus];

    /// <summary>The label on the posted column.</summary>
    public string PostedLabel => _strings[StringKeys.InventoryPosted];

    /// <summary>The value of the subtotal.</summary>
    public decimal SubtotalAmount => Lines.Sum(line => line.LineTotal - TaxOf(line));

    /// <summary>The value of the total.</summary>
    public decimal TotalAmount
    {
        get
        {
            decimal discount = InventoryInput.TryReadMoney(DiscountText, out decimal value) ? value : 0m;

            return Lines.Sum(line => line.LineTotal) - discount;
        }
    }

    /// <summary>True when the document carries no lines at all.</summary>
    public bool HasNoLines => Lines.Count == 0;

    /// <summary>The text shown when the document carries no lines.</summary>
    public string NoLinesText => _strings[StringKeys.PurchaseEditorNoLines];

    /// <summary>True when the document has already been posted and cannot be edited.</summary>
    public bool IsReadOnly => IsPosted;

    /// <summary>Loads the reference data and, when editing, the document itself.</summary>
    /// <param name="purchaseId">The document to edit, or null to create one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [RelayCommand]
    public async Task LoadAsync(int? purchaseId, CancellationToken cancellationToken)
    {
        PurchaseId = purchaseId;
        IsBusy = true;

        try
        {
            Result<InventoryReferenceData> reference = await _referenceHandler.HandleAsync(
                new GetInventoryReferenceDataQuery(),
                cancellationToken);

            if (reference.IsSuccess)
            {
                ApplyReferenceData(reference.Value);
            }

            if (purchaseId is int id)
            {
                Result<PurchaseDetails> document = await _getHandler.HandleAsync(
                    new GetPurchaseQuery(id),
                    cancellationToken);

                if (document.IsFailure)
                {
                    StatusMessage = InventoryErrorMessages.Resolve(_strings, document.Error.Code);
                    return;
                }

                ApplyDocument(document.Value);
            }
            else
            {
                BusinessDateText = InventoryInput.WriteDate(DateOnly.FromDateTime(DateTime.Today));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Adds an empty line to the document.</summary>
    [RelayCommand]
    private void AddLine()
    {
        var row = new PurchaseLineRow();
        row.Changed += OnLineChanged;

        Lines.Add(row);

        RaiseLineStateChanged();
    }

    /// <summary>Removes a line from the document.</summary>
    /// <param name="row">The line to remove.</param>
    [RelayCommand]
    private void RemoveLine(PurchaseLineRow? row)
    {
        if (row is null)
        {
            return;
        }

        row.Changed -= OnLineChanged;

        Lines.Remove(row);

        RaiseLineStateChanged();
    }

    /// <summary>Saves the document as a draft.</summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await SaveAsync(cancellationToken, post: false);
    }

    /// <summary>Saves the document and posts it, receiving the goods.</summary>
    [RelayCommand]
    private async Task SaveAndPostAsync(CancellationToken cancellationToken)
    {
        await SaveAsync(cancellationToken, post: true);
    }

    /// <summary>Closes the editor without saving.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private async Task SaveAsync(CancellationToken cancellationToken, bool post)
    {
        StatusMessage = string.Empty;

        if (!InventoryInput.TryReadDate(BusinessDateText, out DateOnly businessDate))
        {
            StatusMessage = _strings[StringKeys.ErrorInventoryQuantity];
            return;
        }

        if (!InventoryInput.TryReadMoney(DiscountText, out decimal discount))
        {
            StatusMessage = _strings[StringKeys.ErrorNegativePrice];
            return;
        }

        var inputs = new List<PurchaseLineInput>();

        foreach (PurchaseLineRow row in Lines)
        {
            if (row.ProductId <= 0)
            {
                StatusMessage = _strings[StringKeys.PurchaseEditorNoLines];
                return;
            }

            if (!InventoryInput.TryReadDecimal(row.Quantity, out decimal quantity))
            {
                StatusMessage = _strings[StringKeys.ErrorInventoryQuantity];
                return;
            }

            if (!InventoryInput.TryReadMoney(row.UnitCost, out decimal unitCost))
            {
                StatusMessage = _strings[StringKeys.ErrorNegativePrice];
                return;
            }

            if (!InventoryInput.TryReadMoney(row.Tax, out decimal tax))
            {
                StatusMessage = _strings[StringKeys.ErrorNegativePrice];
                return;
            }

            inputs.Add(new PurchaseLineInput(row.ProductId, quantity, new Money(unitCost), new Money(tax)));
        }

        if (inputs.Count == 0)
        {
            StatusMessage = _strings[StringKeys.PurchaseEditorNoLines];
            return;
        }

        var command = new SavePurchaseCommand(
            PurchaseId,
            SupplierId == 0 ? null : SupplierId,
            businessDate,
            inputs,
            new Money(discount),
            Notes);

        Result<int> saved = await _saveHandler.HandleAsync(command, cancellationToken);

        if (saved.IsFailure)
        {
            StatusMessage = InventoryErrorMessages.Resolve(_strings, saved.Error.Code);
            return;
        }

        if (!post)
        {
            CloseRequested?.Invoke(this, true);
            return;
        }

        Result<int> posted = await _postHandler.HandleAsync(new PostPurchaseCommand(saved.Value), cancellationToken);

        if (posted.IsFailure)
        {
            StatusMessage = InventoryErrorMessages.Resolve(_strings, posted.Error.Code);
            return;
        }

        CloseRequested?.Invoke(this, true);
    }

    private void ApplyReferenceData(InventoryReferenceData data)
    {
        Products.Clear();

        foreach (InventoryReferenceProduct product in data.Products)
        {
            Products.Add(new InventoryOptionItem(
                product.Id,
                $"{product.Sku} - {DisplayName(product.Name, product.NameAr)}",
                product.QuantityScale,
                product.CostPrice.Amount, product.UnitName));
        }

        Suppliers.Clear();

        foreach (InventoryReferenceSupplier supplier in data.Suppliers)
        {
            Suppliers.Add(new InventoryOptionItem(supplier.Id, DisplayName(supplier.Name, supplier.NameAr)));
        }
    }

    private void ApplyDocument(PurchaseDetails document)
    {
        SupplierId = document.SupplierId ?? 0;
        BusinessDateText = InventoryInput.WriteDate(document.BusinessDate);
        DiscountText = InventoryInput.WriteAmount(document.Discount.Amount);
        Notes = document.Notes ?? string.Empty;
        IsPosted = document.Status is DocumentStatus.Posted or DocumentStatus.Reversed;

        Lines.Clear();

        foreach (PurchaseLineView line in document.Lines)
        {
            var row = new PurchaseLineRow
            {
                ProductId = line.ProductId,
                Quantity = InventoryInput.WriteNumber(line.Quantity),
                UnitCost = InventoryInput.WriteAmount(line.UnitCost.Amount),
                Tax = InventoryInput.WriteAmount(line.Tax.Amount),
                ProductName = line.ProductName,
            };

            row.Changed += OnLineChanged;

            Lines.Add(row);
        }

        RaiseLineStateChanged();
    }

    private void OnLineChanged(object? sender, EventArgs e)
    {
        if (sender is PurchaseLineRow row)
        {
            InventoryOptionItem? product = Products.FirstOrDefault(candidate => candidate.Id == row.ProductId);

            row.ProductName = product?.DisplayName ?? string.Empty;
            row.Precision = product?.Precision ?? 0;

            if (product is not null && string.IsNullOrWhiteSpace(row.UnitCost))
            {
                row.UnitCost = InventoryInput.WriteAmount(product.CostPrice);
            }
        }

        OnPropertyChanged(nameof(SubtotalAmount));
        OnPropertyChanged(nameof(TotalAmount));
    }

    private void RaiseLineStateChanged()
    {
        OnPropertyChanged(nameof(HasNoLines));
        OnPropertyChanged(nameof(SubtotalAmount));
        OnPropertyChanged(nameof(TotalAmount));
    }

    private static decimal TaxOf(PurchaseLineRow row) =>
        InventoryInput.TryReadMoney(row.Tax, out decimal tax) ? tax : 0m;

    private string DisplayName(string name, string? nameAr) =>
        _languageService.CurrentLanguage == "ar" && !string.IsNullOrWhiteSpace(nameAr) ? nameAr : name;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
