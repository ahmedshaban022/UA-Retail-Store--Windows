using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The stock adjustment dialog: one product, one signed change, and the reason it was made. A
/// large adjustment is routed to a second approver instead of reaching the ledger straight away
/// (PRD §5.3, INV-05).
/// </summary>
public sealed partial class StockAdjustmentViewModel : ObservableObject
{
    private readonly ICommandHandler<PostStockAdjustmentCommand, Result<StockAdjustmentOutcome>> _postHandler;
    private readonly IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> _referenceHandler;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private int _productId;

    [ObservableProperty]
    private string _quantityText = string.Empty;

    [ObservableProperty]
    private string _unitCostText = string.Empty;

    [ObservableProperty]
    private bool _damaged;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the dialog.</summary>
    public StockAdjustmentViewModel(
        ICommandHandler<PostStockAdjustmentCommand, Result<StockAdjustmentOutcome>> postHandler,
        IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>> referenceHandler,
        IStrings strings,
        LanguageService languageService)
    {
        _postHandler = postHandler;
        _referenceHandler = referenceHandler;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The products the owner can pick.</summary>
    public ObservableCollection<InventoryOptionItem> Products { get; } = [];

    /// <summary>Raised when the dialog wants to close itself.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>The title of the dialog.</summary>
    public string Title => _strings[StringKeys.StockAdjustmentTitle];

    /// <summary>The label on the product picker.</summary>
    public string ProductLabel => _strings[StringKeys.StockAdjustmentProduct];

    /// <summary>The label on the quantity field.</summary>
    public string QuantityLabel => _strings[StringKeys.StockAdjustmentQuantity];

    /// <summary>The label on the unit cost field.</summary>
    public string UnitCostLabel => _strings[StringKeys.StockAdjustmentUnitCost];

    /// <summary>The label on the bucket picker.</summary>
    public string DispositionLabel => _strings[StringKeys.StockAdjustmentDisposition];

    /// <summary>The label of the sellable bucket.</summary>
    public string SellableText => _strings[StringKeys.StockAdjustmentSellable];

    /// <summary>The label of the damaged bucket.</summary>
    public string DamagedText => _strings[StringKeys.StockAdjustmentDamaged];

    /// <summary>The label on the reason field.</summary>
    public string ReasonLabel => _strings[StringKeys.StockAdjustmentReason];

    /// <summary>The label on the submit command.</summary>
    public string SubmitText => _strings[StringKeys.CommonOk];

    /// <summary>The label on the cancel command.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>Loads the products the owner can pick.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

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

    /// <summary>Posts the adjustment.</summary>
    [RelayCommand]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        StatusMessage = string.Empty;

        if (ProductId <= 0)
        {
            StatusMessage = _strings[StringKeys.ErrorInventoryRequired];
            return;
        }

        if (!InventoryInput.TryReadDecimal(QuantityText, out decimal quantity) || quantity == 0m)
        {
            StatusMessage = _strings[StringKeys.ErrorInventoryQuantity];
            return;
        }

        if (!InventoryInput.TryReadMoney(UnitCostText, out decimal unitCost))
        {
            StatusMessage = _strings[StringKeys.ErrorNegativePrice];
            return;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            StatusMessage = _strings[StringKeys.ErrorInventoryRequired];
            return;
        }

        var command = new PostStockAdjustmentCommand(
            ProductId,
            quantity,
            Damaged ? StockDisposition.Damaged : StockDisposition.Sellable,
            new Money(unitCost),
            Reason.Trim());

        Result<StockAdjustmentOutcome> result = await _postHandler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = InventoryErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        if (!result.Value.Posted)
        {
            StatusMessage = _strings[StringKeys.StockAdjustmentRequiresApproval];
            return;
        }

        CloseRequested?.Invoke(this, true);
    }

    /// <summary>Closes the dialog without posting.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    partial void OnProductIdChanged(int value)
    {
        InventoryOptionItem? product = Products.FirstOrDefault(candidate => candidate.Id == value);

        if (product is not null && string.IsNullOrWhiteSpace(UnitCostText))
        {
            UnitCostText = InventoryInput.WriteAmount(product.CostPrice);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
