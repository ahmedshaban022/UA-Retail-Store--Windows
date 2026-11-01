using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StoreFlow.Application.Common;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The till: the scanner, the cart, the money and the receipt.
/// </summary>
/// <remarks>
/// <para>The cart is the cashier's, not the database's. Nothing here writes to the shelf until the
/// sale is confirmed, and the totals move the moment a line changes, because a cashier who has to
/// wait to see the total has already lost the queue (PRD POS-02).</para>
/// <para>The money on screen is worked out by the same code that will work it out again when the
/// sale is posted. The till does not have a second opinion about the arithmetic.</para>
/// </remarks>
public sealed partial class PosViewModel : ObservableObject
{
    /// <summary>How long the scan chip stays on the line it belongs to, in milliseconds.</summary>
    private const int ScanChipMilliseconds = 900;

    private readonly IQueryHandler<LookupProductByBarcodeQuery, Result<PosProductFacts>> _lookupHandler;
    private readonly IQueryHandler<SearchSellableProductsQuery, Result<IReadOnlyList<PosProductFacts>>> _searchHandler;
    private readonly IQueryHandler<ListHeldCartsQuery, Result<IReadOnlyList<HeldCartSummary>>> _heldCartsHandler;
    private readonly ICommandHandler<PostSaleCommand, Result<SaleReceipt>> _postSaleHandler;
    private readonly ICommandHandler<HoldCartCommand, Result<HeldCartSummary>> _holdCartHandler;
    private readonly ICommandHandler<ResumeHeldCartCommand, Result<HeldCartDetails>> _resumeCartHandler;
    private readonly ICommandHandler<ClearHeldCartCommand, Result> _clearCartHandler;
    private readonly IPosDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly IThemeService _themeService;
    private readonly LanguageService _languageService;

    private readonly Dictionary<int, PosProductFacts> _facts = [];

    private System.Windows.Threading.DispatcherTimer? _scanChipTimer;

    /// <summary>
    /// True when the message on the screen is one of the cart's complaints, so that it can be
    /// taken away again when the complaint is answered.
    /// </summary>
    private bool _issueOnScreen;

    /// <summary>
    /// The log the till writes to. The time a scan takes is written there, because a shopkeeper
    /// wants to know that the till keeps up, and the number has to come from the till itself.
    /// </summary>
    private readonly ILogger<PosViewModel>? _logger;

    [ObservableProperty]
    private string _scanText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _cashReceivedText = string.Empty;

    [ObservableProperty]
    private string _invoiceDiscountText = string.Empty;

    [ObservableProperty]
    private PaymentMethod _paymentMethod = PaymentMethod.Cash;

    [ObservableProperty]
    private string _lastReceiptNumber = string.Empty;

    /// <summary>Creates the till.</summary>
    public PosViewModel(
        IQueryHandler<LookupProductByBarcodeQuery, Result<PosProductFacts>> lookupHandler,
        IQueryHandler<SearchSellableProductsQuery, Result<IReadOnlyList<PosProductFacts>>> searchHandler,
        IQueryHandler<ListHeldCartsQuery, Result<IReadOnlyList<HeldCartSummary>>> heldCartsHandler,
        ICommandHandler<PostSaleCommand, Result<SaleReceipt>> postSaleHandler,
        ICommandHandler<HoldCartCommand, Result<HeldCartSummary>> holdCartHandler,
        ICommandHandler<ResumeHeldCartCommand, Result<HeldCartDetails>> resumeCartHandler,
        ICommandHandler<ClearHeldCartCommand, Result> clearCartHandler,
        IPosDialogs dialogs,
        IStrings strings,
        IThemeService themeService,
        LanguageService languageService,
        ILogger<PosViewModel>? logger = null)
    {
        _lookupHandler = lookupHandler;
        _searchHandler = searchHandler;
        _heldCartsHandler = heldCartsHandler;
        _postSaleHandler = postSaleHandler;
        _holdCartHandler = holdCartHandler;
        _resumeCartHandler = resumeCartHandler;
        _clearCartHandler = clearCartHandler;
        _dialogs = dialogs;
        _strings = strings;
        _themeService = themeService;
        _languageService = languageService;
        _logger = logger;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The lines of the cart.</summary>
    public ObservableCollection<PosLineViewModel> Lines { get; } = [];

    /// <summary>
    /// Writes down how long a scan took. The number comes from the till itself, because a
    /// shopkeeper wants to know that the till keeps up with the scanner.
    /// </summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="barcode">The barcode that was scanned.</param>
    /// <param name="milliseconds">How long the till took to answer.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "The scan of {Barcode} was answered in {Milliseconds} ms.")]
    private static partial void LogScanAnswered(ILogger logger, string barcode, long milliseconds);

    /// <summary>The products a search found.</summary>
    public ObservableCollection<PosProductFacts> SearchResults { get; } = [];

    /// <summary>The carts waiting to be picked up.</summary>
    public ObservableCollection<HeldCartSummary> HeldCarts { get; } = [];

    /// <summary>The title of the screen.</summary>
    public string Title => _strings[StringKeys.PosTitle];

    /// <summary>The label of the scan field.</summary>
    public string ScanLabel => _strings[StringKeys.PosScan];

    /// <summary>The hint inside the scan field.</summary>
    public string ScanPlaceholder => _strings[StringKeys.PosScanPlaceholder];

    /// <summary>The heading of the search results.</summary>
    public string SearchResultsLabel => _strings[StringKeys.PosSearchResults];

    /// <summary>The message shown when a search finds nothing.</summary>
    public string NoResultsLabel => _strings[StringKeys.PosNoResults];

    /// <summary>The heading of the cart.</summary>
    public string CartLabel => _strings[StringKeys.PosCart];

    /// <summary>The message shown when the cart is empty.</summary>
    public string EmptyCartLabel => _strings[StringKeys.PosCartEmpty];

    /// <summary>The heading of the product column.</summary>
    public string ProductColumnLabel => _strings[StringKeys.PosColumnProduct];

    /// <summary>The heading of the quantity column.</summary>
    public string QuantityColumnLabel => _strings[StringKeys.PosColumnQuantity];

    /// <summary>The heading of the price column.</summary>
    public string PriceColumnLabel => _strings[StringKeys.PosColumnPrice];

    /// <summary>The heading of the discount column.</summary>
    public string DiscountColumnLabel => _strings[StringKeys.PosColumnDiscount];

    /// <summary>The heading of the total column.</summary>
    public string TotalColumnLabel => _strings[StringKeys.PosColumnTotal];

    /// <summary>The label of the subtotal.</summary>
    public string SubtotalLabel => _strings[StringKeys.PosSubtotal];

    /// <summary>The label of the discount.</summary>
    public string DiscountLabel => _strings[StringKeys.PosDiscount];

    /// <summary>The label of the tax.</summary>
    public string TaxLabel => _strings[StringKeys.PosTax];

    /// <summary>The label of the taxable amount.</summary>
    public string TaxableLabel => _strings[StringKeys.PosTaxable];

    /// <summary>The label of the grand total.</summary>
    public string GrandTotalLabel => _strings[StringKeys.PosGrandTotal];

    /// <summary>The label of the amount paid.</summary>
    public string PaidLabel => _strings[StringKeys.PosPaid];

    /// <summary>The label of the change.</summary>
    public string ChangeLabel => _strings[StringKeys.PosChange];

    /// <summary>The label of the cash button.</summary>
    public string CashLabel => _strings[StringKeys.PosCash];

    /// <summary>The label of the card button.</summary>
    public string CardLabel => _strings[StringKeys.PosCard];

    /// <summary>The label of the other button.</summary>
    public string OtherLabel => _strings[StringKeys.PosOther];

    /// <summary>The label of the cash received field.</summary>
    public string CashReceivedLabel => _strings[StringKeys.PosCashReceived];

    /// <summary>The label of the confirm button.</summary>
    public string ConfirmSaleLabel => _strings[StringKeys.PosConfirmSale];

    /// <summary>The label of the put-aside button.</summary>
    public string HoldLabel => _strings[StringKeys.PosHold];

    /// <summary>The label of the take-back button.</summary>
    public string ResumeLabel => _strings[StringKeys.PosResume];

    /// <summary>The label of the empty-cart button.</summary>
    public string ClearCartLabel => _strings[StringKeys.PosClearCart];

    /// <summary>The heading of the carts waiting to be picked up.</summary>
    public string HeldCartsLabel => _strings[StringKeys.PosHeldCarts];

    /// <summary>The message shown when no cart is waiting.</summary>
    public string HeldCartsEmptyLabel => _strings[StringKeys.PosHeldCartsEmpty];

    /// <summary>The label of the whole-sale discount field.</summary>
    public string InvoiceDiscountLabel => _strings[StringKeys.PosInvoiceDiscount];

    /// <summary>True when the cart has nothing on it.</summary>
    public bool IsCartEmpty => Lines.Count == 0;

    /// <summary>True when the cart has something on it.</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary>True when the cart has nothing on it.</summary>
    public bool HasNoLines => Lines.Count == 0;

    /// <summary>True when the scan field holds something.</summary>
    public bool HasScanText => !string.IsNullOrWhiteSpace(ScanText);

    /// <summary>True when a search found something.</summary>
    public bool HasSearchResults => SearchResults.Count > 0;

    /// <summary>True when some carts are waiting.</summary>
    public bool HasHeldCarts => HeldCarts.Count > 0;

    /// <summary>True when no cart is waiting.</summary>
    public bool HasNoHeldCarts => HeldCarts.Count == 0;

    /// <summary>True when there is something to say to the cashier.</summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>The line the cashier has picked.</summary>
    [ObservableProperty]
    private PosLineViewModel? _selectedLine;

    /// <summary>True when a search has run and found nothing.</summary>
    public bool HasNoResults => SearchResults.Count == 0 && HasScanText;

    /// <summary>The subtotal, as the screen shows it.</summary>
    public string SubtotalText => Format(Totals.Subtotal);

    /// <summary>The discount, as the screen shows it.</summary>
    public string DiscountText => Format(Totals.Discount);

    /// <summary>The tax, as the screen shows it.</summary>
    public string TaxText => Format(Totals.Tax);

    /// <summary>
    /// The taxable amount, as the screen shows it: what the tax is charged on. It is the total less
    /// the tax on it, which is the same figure whether the price carried the tax or did not, and it
    /// is always true that the taxable amount plus the tax is the total.
    /// </summary>
    public string TaxableText => Format(Totals.Total - Totals.Tax);

    /// <summary>The grand total, as the screen shows it.</summary>
    public string TotalText => Format(Totals.Total);

    /// <summary>The amount paid, as the screen shows it.</summary>
    public string PaidText => Format(Totals.Total);

    /// <summary>
    /// The change, as the screen shows it. It is worked out from the cash the cashier has typed, so
    /// the customer is told what is coming back before the sale is confirmed.
    /// </summary>
    public string ChangeText => Format(ChangeOwed);

    /// <summary>The money of the cart, as the cart priced it.</summary>
    public PosTotals Totals { get; private set; } = new(
        Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero);

    /// <summary>
    /// The change owed to the customer, worked out the same way the sale will work it out: the cash
    /// that was handed over, less the cash that is paying for the invoice.
    /// </summary>
    private Money ChangeOwed
    {
        get
        {
            if (PaymentMethod != PaymentMethod.Cash)
            {
                return Money.Zero;
            }

            decimal received = InventoryInput.TryReadDecimal(CashReceivedText, out decimal value)
                ? value
                : 0m;

            if (received <= Totals.Total.Amount)
            {
                return Money.Zero;
            }

            return new Money(received - Totals.Total.Amount);
        }
    }

    /// <summary>The receipt of the last sale, so it can be printed again without selling again.</summary>
    public SaleReceipt? LastReceipt { get; private set; }

    /// <summary>
    /// Handles the scan field being finished with. A code that names a product is a scan; anything
    /// else is a search.
    /// </summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        string text = ScanText.Trim();

        if (text.Length == 0)
        {
            return;
        }

        IsBusy = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Result<PosProductFacts> lookup = await _lookupHandler.HandleAsync(
                new LookupProductByBarcodeQuery(text), CancellationToken.None);

            if (lookup.IsSuccess)
            {
                AddProduct(lookup.Value);
                ScanText = string.Empty;
                SearchResults.Clear();
                OnPropertyChanged(nameof(HasNoResults));
                OnPropertyChanged(nameof(HasSearchResults));

                watch.Stop();

                if (_logger is not null)
                {
                    LogScanAnswered(_logger, text, watch.ElapsedMilliseconds);
                }

                return;
            }

            Result<IReadOnlyList<PosProductFacts>> search = await _searchHandler.HandleAsync(
                new SearchSellableProductsQuery(text), CancellationToken.None);

            SearchResults.Clear();

            if (search.IsSuccess)
            {
                foreach (PosProductFacts product in search.Value)
                {
                    SearchResults.Add(product);
                }
            }
            else
            {
                StatusMessage = PosErrorMessages.Resolve(_strings, search.Error.Code);
            }

            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(HasSearchResults));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Adds a product from the search results to the cart.</summary>
    /// <param name="product">The product to add.</param>
    [RelayCommand]
    private void AddFromSearch(PosProductFacts product) => AddProduct(product);

    /// <summary>Takes a line off the cart.</summary>
    /// <param name="line">The line to remove.</param>
    [RelayCommand]
    private void RemoveLine(PosLineViewModel line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Lines.Remove(line);
        Recalculate();
    }

    /// <summary>Adds one more of a line.</summary>
    /// <param name="line">The line to change.</param>
    [RelayCommand]
    private void IncreaseQuantity(PosLineViewModel line)
    {
        ArgumentNullException.ThrowIfNull(line);

        decimal quantity = line.Quantity + 1m;

        line.QuantityText = quantity.ToString("0.####", CultureInfo.CurrentCulture);
        Recalculate();
    }

    /// <summary>Takes one off a line.</summary>
    /// <param name="line">The line to change.</param>
    [RelayCommand]
    private void DecreaseQuantity(PosLineViewModel line)
    {
        ArgumentNullException.ThrowIfNull(line);

        decimal quantity = line.Quantity - 1m;

        line.QuantityText = quantity <= 0m
            ? "0"
            : quantity.ToString("0.####", CultureInfo.CurrentCulture);

        Recalculate();
    }

    /// <summary>Works the money out again after the cart has changed.</summary>
    [RelayCommand]
    private void Recalculate()
    {
        var inputs = new List<SaleLineInput>(Lines.Count);

        foreach (PosLineViewModel line in Lines)
        {
            inputs.Add(line.ToInput());
        }

        DiscountSpec invoiceDiscount = PosLineViewModel.ParseDiscount(InvoiceDiscountText);

        PosCart cart = PosCartBuilder.Build(inputs, _facts, invoiceDiscount);

        foreach (CartLine priced in cart.Lines)
        {
            PosLineViewModel? line = Lines.FirstOrDefault(candidate => candidate.Product.Id == priced.ProductId);

            if (line is not null)
            {
                line.Total = priced.Total;
                line.Tax = priced.Tax;
            }
        }

        Totals = cart.Totals;

        // The cart's problems are the cashier's problems. A line that was refused is not quietly
        // left out of the total: the reason is put on the screen, where the cashier can read it.
        // An empty cart is not a problem worth saying out loud.
        if (cart.Issues.Count > 0 && Lines.Count > 0)
        {
            StatusMessage = PosErrorMessages.Resolve(_strings, cart.Issues[0].Code);
            _issueOnScreen = true;
        }
        else if (_issueOnScreen)
        {
            _issueOnScreen = false;
            StatusMessage = string.Empty;
        }

        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(DiscountText));
        OnPropertyChanged(nameof(TaxableText));
        OnPropertyChanged(nameof(TaxText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(PaidText));
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(IsCartEmpty));
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(HasNoLines));
    }

    /// <summary>Sets the way the customer is paying to cash.</summary>
    [RelayCommand]
    private void ChooseCash() => ChoosePaymentMethod(PaymentMethod.Cash);

    /// <summary>Sets the way the customer is paying to a card.</summary>
    [RelayCommand]
    private void ChooseCard() => ChoosePaymentMethod(PaymentMethod.Card);

    /// <summary>Sets the way the customer is paying to something else.</summary>
    [RelayCommand]
    private void ChooseOther() => ChoosePaymentMethod(PaymentMethod.Other);

    /// <summary>Sets the way the customer is paying.</summary>
    /// <param name="method">How the customer is paying.</param>
    private void ChoosePaymentMethod(PaymentMethod method)
    {
        PaymentMethod = method;

        if (method == PaymentMethod.Cash)
        {
            return;
        }

        CashReceivedText = Totals.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        Recalculate();
    }

    /// <summary>
    /// Posts the sale. The same key is used for every attempt at this checkout, so a double click
    /// cannot make two sales.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmSaleAsync()
    {
        if (Lines.Count == 0)
        {
            StatusMessage = PosErrorMessages.Resolve(_strings, PosErrorCodes.EmptyCart);
            return;
        }

        IsBusy = true;

        try
        {
            var command = new PostSaleCommand(
                Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.Now),
                Lines.Select(line => line.ToInput()).ToList(),
                PosLineViewModel.ParseDiscount(InvoiceDiscountText),
                BuildPayments());

            Result<SaleReceipt> result = await _postSaleHandler.HandleAsync(command, CancellationToken.None);

            if (result.IsFailure)
            {
                StatusMessage = PosErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            LastReceipt = result.Value;
            LastReceiptNumber = result.Value.Number;

            ClearCart();

            // The message is set after the cart is emptied, so that the number of the invoice the
            // customer was given is still on the screen when the receipt is closed.
            StatusMessage = _strings.Format(StringKeys.PosSalePosted, result.Value.Number);

            await _dialogs.ShowReceiptAsync(result.Value, isCopy: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Prints the receipt of the last sale again. It does not sell anything.</summary>
    [RelayCommand]
    private async Task PrintLastReceiptAsync()
    {
        if (LastReceipt is null)
        {
            return;
        }

        await _dialogs.ShowReceiptAsync(LastReceipt, isCopy: true);
    }

    /// <summary>Puts the cart aside so the next customer can be served.</summary>
    [RelayCommand]
    private async Task HoldCartAsync()
    {
        if (Lines.Count == 0)
        {
            StatusMessage = PosErrorMessages.Resolve(_strings, PosErrorCodes.EmptyCart);
            return;
        }

        string? label = await _dialogs.AskForLabelAsync(_strings[StringKeys.PosHold], _strings[StringKeys.PosHoldLabel]);

        if (label is null)
        {
            return;
        }

        Result<HeldCartSummary> result = await _holdCartHandler.HandleAsync(
            new HoldCartCommand(
                label,
                Lines.Select(line => line.ToInput()).ToList(),
                PosLineViewModel.ParseDiscount(InvoiceDiscountText)),
            CancellationToken.None);

        if (result.IsFailure)
        {
            StatusMessage = PosErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        ClearCart();
        await RefreshHeldCartsAsync();
    }

    /// <summary>Takes a waiting cart back onto the till.</summary>
    /// <param name="cart">The cart to take back.</param>
    [RelayCommand]
    private async Task ResumeCartAsync(HeldCartSummary cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        Result<HeldCartDetails> result = await _resumeCartHandler.HandleAsync(
            new ResumeHeldCartCommand(cart.Id), CancellationToken.None);

        if (result.IsFailure)
        {
            StatusMessage = PosErrorMessages.Resolve(_strings, result.Error.Code);
            await RefreshHeldCartsAsync();
            return;
        }

        ClearCart();

        foreach (SaleLineInput input in result.Value.Lines)
        {
            PosProductFacts product = _facts.TryGetValue(input.ProductId, out PosProductFacts? known)
                ? known
                : new PosProductFacts(
                    input.ProductId,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    string.Empty,
                    0,
                    input.UnitPrice,
                    0,
                    false,
                    true,
                    long.MaxValue);

            _facts[product.Id] = product;

            PosLineViewModel resumed = new(product, Format);

            resumed.Changed += OnLineChanged;

            Lines.Add(resumed);
        }

        if (result.Value.InvoiceDiscount.Kind == DiscountKind.Percent)
        {
            InvoiceDiscountText = result.Value.InvoiceDiscount.Value.ToString("0.##", CultureInfo.CurrentCulture) + "%";
        }
        else
        {
            InvoiceDiscountText = result.Value.InvoiceDiscount.Value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        Recalculate();
        await RefreshHeldCartsAsync();
    }

    /// <summary>Throws a waiting cart away without selling it.</summary>
    /// <param name="cart">The cart to discard.</param>
    [RelayCommand]
    private async Task DiscardCartAsync(HeldCartSummary cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        Result result = await _clearCartHandler.HandleAsync(
            new ClearHeldCartCommand(cart.Id), CancellationToken.None);

        if (result.IsFailure)
        {
            StatusMessage = PosErrorMessages.Resolve(_strings, result.Error.Code);
        }

        await RefreshHeldCartsAsync();
    }

    /// <summary>Reloads the carts that are waiting.</summary>
    [RelayCommand]
    private async Task RefreshHeldCartsAsync()
    {
        Result<IReadOnlyList<HeldCartSummary>> result = await _heldCartsHandler.HandleAsync(
            new ListHeldCartsQuery(), CancellationToken.None);

        HeldCarts.Clear();

        if (result.IsSuccess)
        {
            foreach (HeldCartSummary cart in result.Value)
            {
                HeldCarts.Add(cart);
            }

            OnPropertyChanged(nameof(HasHeldCarts));
            OnPropertyChanged(nameof(HasNoHeldCarts));
        }
    }

    /// <summary>Empties the cart.</summary>
    [RelayCommand]
    private void ClearCart()
    {
        Lines.Clear();
        _facts.Clear();
        InvoiceDiscountText = string.Empty;
        CashReceivedText = string.Empty;
        StatusMessage = string.Empty;
        Recalculate();
    }

    partial void OnScanTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasScanText));

        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(HasSearchResults));
        }
    }

    partial void OnInvoiceDiscountTextChanged(string value) => Recalculate();

    partial void OnCashReceivedTextChanged(string value) => Recalculate();

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    private void AddProduct(PosProductFacts product)
    {
        ArgumentNullException.ThrowIfNull(product);

        _facts[product.Id] = product;
        PosLineViewModel? existing = Lines.FirstOrDefault(line => line.Product.Id == product.Id);

        if (existing is null)
        {
            var line = new PosLineViewModel(product, Format);

            line.Changed += OnLineChanged;

            Lines.Add(line);
            Recalculate();
            ShowScanChip(line);
            return;
        }

        // A second scan of the same product adds to the line that is already on the cart.
        decimal quantity = existing.Quantity + 1m;

        existing.QuantityText = quantity.ToString("0.####", CultureInfo.CurrentCulture);
        Recalculate();
        ShowScanChip(existing);
    }

    /// <summary>
    /// Works the money out again when the cashier changed a line. The line tells the cart; the cart
    /// does not have to guess that something moved.
    /// </summary>
    /// <param name="sender">The line that changed.</param>
    /// <param name="e">The event arguments.</param>
    private void OnLineChanged(object? sender, EventArgs e) => Recalculate();

    /// <summary>
    /// Puts the chip on the line that was just scanned, and takes it away again a moment later.
    /// The chip is what tells the cashier the scan was read.
    /// </summary>
    private void ShowScanChip(PosLineViewModel line)
    {
        line.ScanChipText = $"{line.Name}  ·  {Format(line.UnitPrice)}";
        line.IsScanChipVisible = true;

        _scanChipTimer?.Stop();

        _scanChipTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ScanChipMilliseconds),
        };

        _scanChipTimer.Tick += (_, _) =>
        {
            line.IsScanChipVisible = false;
            _scanChipTimer?.Stop();
        };

        _scanChipTimer.Start();
    }

    /// <summary>Builds the payments from what the cashier has entered.</summary>
    private IReadOnlyList<PaymentInput> BuildPayments()
    {
        if (PaymentMethod != PaymentMethod.Cash)
        {
            return
            [
                new PaymentInput(PaymentMethod, Totals.Total, Money.Zero, null),
            ];
        }

        decimal received = InventoryInput.TryReadDecimal(CashReceivedText, out decimal value)
            ? value
            : 0m;

        return
        [
            new PaymentInput(PaymentMethod, Totals.Total, new Money(received), null),
        ];
    }

    private string Format(Money amount) => _themeService.FormatMoney(amount);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ScanLabel));
        OnPropertyChanged(nameof(ScanPlaceholder));
        OnPropertyChanged(nameof(CartLabel));
        OnPropertyChanged(nameof(EmptyCartLabel));
        OnPropertyChanged(nameof(SearchResultsLabel));
        OnPropertyChanged(nameof(NoResultsLabel));
        OnPropertyChanged(nameof(ProductColumnLabel));
        OnPropertyChanged(nameof(QuantityColumnLabel));
        OnPropertyChanged(nameof(PriceColumnLabel));
        OnPropertyChanged(nameof(DiscountColumnLabel));
        OnPropertyChanged(nameof(TotalColumnLabel));
        OnPropertyChanged(nameof(SubtotalLabel));
        OnPropertyChanged(nameof(DiscountLabel));
        OnPropertyChanged(nameof(TaxLabel));
        OnPropertyChanged(nameof(GrandTotalLabel));
        OnPropertyChanged(nameof(PaidLabel));
        OnPropertyChanged(nameof(ChangeLabel));
        OnPropertyChanged(nameof(CashLabel));
        OnPropertyChanged(nameof(CardLabel));
        OnPropertyChanged(nameof(OtherLabel));
        OnPropertyChanged(nameof(CashReceivedLabel));
        OnPropertyChanged(nameof(ConfirmSaleLabel));
        OnPropertyChanged(nameof(HoldLabel));
        OnPropertyChanged(nameof(ResumeLabel));
        OnPropertyChanged(nameof(ClearCartLabel));
        OnPropertyChanged(nameof(HeldCartsLabel));
        OnPropertyChanged(nameof(HeldCartsEmptyLabel));
        OnPropertyChanged(nameof(InvoiceDiscountLabel));
    }
}
