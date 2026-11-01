using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The reversal dialog: why an invoice is being undone, how the money goes back, and what becomes
/// of the goods.
/// </summary>
/// <remarks>
/// A reversal undoes the whole invoice in one go, so there is no line to choose and no quantity to
/// type. The reason is the one thing that must be said, because a reversal is a correction and a
/// correction without a reason is not an audit trail.
/// </remarks>
public sealed partial class ReverseDialogViewModel : ObservableObject
{
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private PaymentMethod _refundMethod = PaymentMethod.Cash;

    [ObservableProperty]
    private bool _isResellable = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Creates the dialog.</summary>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The service that says which language is running.</param>
    public ReverseDialogViewModel(IStrings strings, LanguageService languageService)
    {
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;

        BuildMethods();
    }

    /// <summary>Raised when the dialog wants to close itself.</summary>
    public event EventHandler<ReverseDialogResult?>? CloseRequested;

    /// <summary>The ways the money can go back.</summary>
    public IReadOnlyList<PaymentMethodOption> Methods => _methods;

    private readonly List<PaymentMethodOption> _methods = [];

    /// <summary>The title of the dialog.</summary>
    public string Title => _strings.Format(StringKeys.SalesReverseTitle, InvoiceNumber);

    /// <summary>The number of the invoice being reversed.</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>The hint under the title.</summary>
    public string Hint => _strings[StringKeys.SalesReverseHint];

    /// <summary>The label of the reason field.</summary>
    public string ReasonLabel => _strings[StringKeys.SalesReverseReason];

    /// <summary>The hint under the reason field.</summary>
    public string ReasonHint => _strings[StringKeys.SalesReverseReasonHint];

    /// <summary>The label of the refund method picker.</summary>
    public string MethodLabel => _strings[StringKeys.SalesReverseMethod];

    /// <summary>The label of the condition of the goods.</summary>
    public string ConditionLabel => _strings[StringKeys.SalesReverseCondition];

    /// <summary>The label of goods that can be sold again.</summary>
    public string ResellableText => _strings[StringKeys.SalesReturnResellable];

    /// <summary>The label of goods that cannot be sold again.</summary>
    public string DamagedText => _strings[StringKeys.SalesReturnDamaged];

    /// <summary>The label on the command that reverses the invoice.</summary>
    public string PostText => _strings[StringKeys.SalesReversePost];

    /// <summary>The label on the command that closes the dialog.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>
    /// Fills the dialog with the invoice being reversed.
    /// </summary>
    /// <param name="invoice">The invoice being reversed.</param>
    public void Load(SalesInvoiceDetails invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        InvoiceNumber = invoice.Number;

        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Closes the dialog without reversing anything.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);

    /// <summary>
    /// Reverses the invoice, if the operator has said enough for it to be reversed.
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

        CloseRequested?.Invoke(this, new ReverseDialogResult(RefundMethod, Reason.Trim(), IsResellable));
    }

    /// <summary>Writes the ways the money can go back in the running language.</summary>
    private void BuildMethods()
    {
        _methods.Clear();

        _methods.Add(new PaymentMethodOption(PaymentMethod.Cash, _strings[StringKeys.PosCash]));
        _methods.Add(new PaymentMethodOption(PaymentMethod.Card, _strings[StringKeys.PosCard]));
        _methods.Add(new PaymentMethodOption(PaymentMethod.Other, _strings[StringKeys.PosOther]));
    }

    partial void OnReasonChanged(string value) => OnPropertyChanged(nameof(StatusMessage));

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
