using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The return dialog: which lines of an invoice are coming back, how many, whether they can be sold
/// again, how the money goes back and why the goods came back at all.
/// </summary>
/// <remarks>
/// The dialog never changes the money itself. It works out what the refund would be from the
/// figures the invoice already carries, so the operator sees the same number the ledger will post.
/// </remarks>
public sealed partial class ReturnDialogViewModel : ObservableObject
{
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private PaymentMethod _refundMethod = PaymentMethod.Cash;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Creates the dialog.</summary>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The service that says which language is running.</param>
    public ReturnDialogViewModel(IStrings strings, LanguageService languageService)
    {
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;

        BuildMethods();
    }

    /// <summary>Writes the ways the money can go back in the running language.</summary>
    private void BuildMethods()
    {
        _methods.Clear();

        _methods.Add(new PaymentMethodOption(PaymentMethod.Cash, _strings[StringKeys.PosCash]));
        _methods.Add(new PaymentMethodOption(PaymentMethod.Card, _strings[StringKeys.PosCard]));
        _methods.Add(new PaymentMethodOption(PaymentMethod.Other, _strings[StringKeys.PosOther]));
    }

    /// <summary>The lines of the invoice, as the operator sees them.</summary>
    public ObservableCollection<ReturnLineViewModel> Lines { get; } = [];

    /// <summary>Raised when the dialog wants to close itself.</summary>
    public event EventHandler<ReturnDialogResult?>? CloseRequested;

    /// <summary>The title of the dialog.</summary>
    public string Title => _strings.Format(StringKeys.SalesReturnTitle, InvoiceNumber);

    /// <summary>The number of the invoice being returned against.</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>The hint under the title.</summary>
    public string Hint => _strings[StringKeys.SalesReturnHint];

    /// <summary>The label of the column that holds the product.</summary>
    public string ProductHeader => _strings[StringKeys.SalesLineProduct];

    /// <summary>The label of the column that holds the quantity sold.</summary>
    public string QuantityHeader => _strings[StringKeys.SalesLineQuantity];

    /// <summary>The label of the column that holds what has already come back.</summary>
    public string ReturnedHeader => _strings[StringKeys.SalesLineReturned];

    /// <summary>The label of the column that holds what is still to be returned.</summary>
    public string RemainingHeader => _strings[StringKeys.SalesReturnRemaining];

    /// <summary>The label of the column that holds the quantity being typed in.</summary>
    public string ReturnQuantityHeader => _strings[StringKeys.SalesReturnQuantity];

    /// <summary>The label of the column that holds the condition of the goods.</summary>
    public string ConditionHeader => _strings[StringKeys.SalesReturnCondition];

    /// <summary>The label of the column that holds the refund of the line.</summary>
    public string RefundHeader => _strings[StringKeys.SalesReturnTotal];

    /// <summary>The label of the reason field.</summary>
    public string ReasonLabel => _strings[StringKeys.SalesReturnReason];

    /// <summary>The hint under the reason field.</summary>
    public string ReasonHint => _strings[StringKeys.SalesReturnReasonHint];

    /// <summary>The label of the refund method picker.</summary>
    public string MethodLabel => _strings[StringKeys.SalesReturnMethod];

    /// <summary>The label of the total refund.</summary>
    public string TotalLabel => _strings[StringKeys.SalesReturnTotal];

    /// <summary>The label of the condition of goods that can be sold again.</summary>
    public string ResellableText => _strings[StringKeys.SalesReturnResellable];

    /// <summary>The label of goods that cannot be sold again.</summary>
    public string DamagedText => _strings[StringKeys.SalesReturnDamaged];

    /// <summary>The label on the command that posts the return.</summary>
    public string PostText => _strings[StringKeys.SalesReturnPost];

    /// <summary>The label on the command that closes the dialog.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>The ways the money can go back.</summary>
    public IReadOnlyList<PaymentMethodOption> Methods => _methods;

    private readonly List<PaymentMethodOption> _methods = [];

    /// <summary>The money that goes back, as the operator has typed it so far.</summary>
    public Money RefundTotal
    {
        get
        {
            Money total = Money.Zero;

            foreach (ReturnLineViewModel line in Lines)
            {
                total += line.Refund;
            }

            return total;
        }
    }

    /// <summary>True when there is nothing left on the invoice to come back.</summary>
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>
    /// Fills the dialog with the lines of an invoice.
    /// </summary>
    /// <param name="invoice">The invoice being returned against.</param>
    public void Load(SalesInvoiceDetails invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        InvoiceNumber = invoice.Number;

        Lines.Clear();

        foreach (SalesLineDetails line in invoice.Lines)
        {
            long remaining = SalesRules.RemainingQuantity(line.QuantityScaled, line.ReturnedQuantityScaled);

            if (remaining <= 0)
            {
                continue;
            }

            Lines.Add(new ReturnLineViewModel(line, remaining, ResellableText, DamagedText, _strings));

            Lines[^1].Changed += OnLineChanged;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// Keeps the total in step with the lines, so the operator sees the refund as they type.
    /// </summary>
    /// <param name="sender">The line that changed.</param>
    /// <param name="e">Nothing.</param>
    private void OnLineChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(RefundTotal));

    /// <summary>Closes the dialog without posting anything.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);

    /// <summary>
    /// Posts the return, if the operator has said enough for it to be posted.
    /// </summary>
    [RelayCommand]
    private void Post()
    {
        StatusMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Reason))
        {
            StatusMessage = SalesErrorMessages.Resolve(_strings, SalesErrorCodes.ReasonRequired);
            OnPropertyChanged(nameof(StatusMessage));
            return;
        }

        List<ReturnDialogLine> chosen = [];

        foreach (ReturnLineViewModel line in Lines)
        {
            if (!line.TryGetQuantity(out decimal quantity))
            {
                StatusMessage = line.ErrorMessage;
                OnPropertyChanged(nameof(StatusMessage));
                return;
            }

            if (quantity <= 0)
            {
                continue;
            }

            chosen.Add(new ReturnDialogLine(line.SalesInvoiceLineId, quantity, line.IsResellable));
        }

        if (chosen.Count == 0)
        {
            StatusMessage = SalesErrorMessages.Resolve(_strings, SalesErrorCodes.NoLines);
            OnPropertyChanged(nameof(StatusMessage));
            return;
        }

        CloseRequested?.Invoke(this, new ReturnDialogResult(chosen, RefundMethod, Reason.Trim()));
    }

    partial void OnReasonChanged(string value) => OnPropertyChanged(nameof(StatusMessage));

    partial void OnRefundMethodChanged(PaymentMethod value) => OnPropertyChanged(nameof(StatusMessage));

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}

/// <summary>One way the money can go back.</summary>
/// <param name="Method">The way it goes back.</param>
/// <param name="DisplayName">The name of the way, in the running language.</param>
public sealed record PaymentMethodOption(PaymentMethod Method, string DisplayName)
{
    /// <summary>
    /// Writes the name of the way. The select shows the text of the record, so it must not show
    /// the record's generated <c>ToString</c>, which reads as
    /// <c>PaymentMethodOption { Method = Cash, DisplayName = ... }</c> instead of the name.
    /// </summary>
    public override string ToString() => DisplayName;
}

/// <summary>
/// One line of the invoice, as the operator changes it: how many are coming back and whether they
/// can be sold again.
/// </summary>
public sealed partial class ReturnLineViewModel : ObservableObject
{
    private readonly SalesLineDetails _line;
    private readonly long _remainingScaled;
    private readonly IStrings _strings;

    [ObservableProperty]
    private string _quantityText = "0";

    [ObservableProperty]
    private bool _isResellable = true;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>Creates the row.</summary>
    /// <param name="line">The line of the invoice.</param>
    /// <param name="remainingScaled">How much of it may still come back.</param>
    /// <param name="resellableText">The label of goods that can be sold again.</param>
    /// <param name="damagedText">The label of goods that cannot be sold again.</param>
    /// <param name="strings">The application's strings.</param>
    public ReturnLineViewModel(
        SalesLineDetails line,
        long remainingScaled,
        string resellableText,
        string damagedText,
        IStrings strings)
    {
        _line = line;
        _remainingScaled = remainingScaled;
        _strings = strings;

        ResellableText = resellableText;
        DamagedText = damagedText;
    }

    /// <summary>The label of goods that can be sold again.</summary>
    public string ResellableText { get; }

    /// <summary>The label of goods that cannot be sold again.</summary>
    public string DamagedText { get; }

    /// <summary>The line of the invoice this row belongs to.</summary>
    public int SalesInvoiceLineId => _line.Id;

    /// <summary>The name of the product, as it was written down at posting time.</summary>
    public string Product => _line.Name;

    /// <summary>The quantity that was sold.</summary>
    public decimal SoldQuantity => _line.Quantity;

    /// <summary>The quantity that has already come back.</summary>
    public decimal ReturnedQuantity => SalesRules.Unscale(_line.ReturnedQuantityScaled, _line.QuantityScale);

    /// <summary>The quantity that may still come back.</summary>
    public decimal RemainingQuantity => SalesRules.Unscale(_remainingScaled, _line.QuantityScale);

    /// <summary>The money that goes back for this line, as it has been typed so far.</summary>
    public Money Refund
    {
        get
        {
            if (ReadQuantity(out decimal quantity) is not null || quantity <= 0)
            {
                return Money.Zero;
            }

            return SalesRules.RefundForQuantity(
                _line.Total,
                SalesRules.Scale(quantity, _line.QuantityScale),
                _line.QuantityScaled);
        }
    }

    /// <summary>
    /// Reads the quantity the operator typed, and says so when it cannot be returned.
    /// </summary>
    /// <param name="quantity">The quantity that was typed.</param>
    /// <returns>True when the quantity may be returned.</returns>
    public bool TryGetQuantity(out decimal quantity)
    {
        string? code = ReadQuantity(out quantity);

        if (code is null)
        {
            ErrorMessage = string.Empty;
            return true;
        }

        ErrorMessage = SalesErrorMessages.Resolve(_strings, code);

        return false;
    }

    /// <summary>
    /// Reads the quantity without saying anything about it, so reading the refund does not wipe
    /// the message the operator needs to see.
    /// </summary>
    /// <param name="quantity">The quantity that was typed.</param>
    /// <returns>The error code when the quantity may not be returned, or null when it may.</returns>
    private string? ReadQuantity(out decimal quantity)
    {
        if (!decimal.TryParse(
                QuantityText,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out quantity)
            || quantity < 0)
        {
            quantity = 0;
            return SalesErrorCodes.InvalidQuantity;
        }

        if (!SalesRules.FitsScale(quantity, _line.QuantityScale))
        {
            return SalesErrorCodes.InvalidQuantity;
        }

        if (SalesRules.Scale(quantity, _line.QuantityScale) > _remainingScaled)
        {
            return SalesErrorCodes.QuantityExceedsRemaining;
        }

        return null;
    }

    partial void OnQuantityTextChanged(string value)
    {
        OnPropertyChanged(nameof(Refund));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsResellableChanged(bool value)
    {
        OnPropertyChanged(nameof(ConditionText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the operator changes this line.</summary>
    public event EventHandler? Changed;

    /// <summary>The condition of the goods, as the operator has it.</summary>
    public string ConditionText => IsResellable ? ResellableText : DamagedText;
}
