using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Pos;
using StoreFlow.Application.Printing;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.Printing;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>Which paper the receipt is shown on.</summary>
public enum ReceiptPaper
{
    /// <summary>An 80 mm thermal receipt.</summary>
    Thermal = 0,

    /// <summary>An A4 invoice.</summary>
    A4 = 1,
}

/// <summary>
/// The receipt of a sale: what it says, what it looks like, and how it is printed again.
/// </summary>
/// <remarks>
/// Printing a receipt never sells anything. The sale was posted when the cashier confirmed it; a
/// reprint draws the invoice that is already on file, and says on its face that it is a copy
/// (PRD POS-08).
/// </remarks>
public sealed partial class ReceiptViewModel : ObservableObject
{
    private readonly IPrintService _printService;
    private readonly ReceiptBuilder _builder;
    private readonly IStrings _strings;
    private readonly IUserSession _session;

    [ObservableProperty]
    private ReceiptPaper _paper = ReceiptPaper.Thermal;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Creates the receipt.</summary>
    /// <param name="receipt">The sale the receipt is for.</param>
    /// <param name="isCopy">True when the receipt is a reprint.</param>
    /// <param name="printService">Sends the receipt to a printer.</param>
    /// <param name="builder">Builds the printable document.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="session">The signed-in cashier.</param>
    public ReceiptViewModel(
        SaleReceipt receipt,
        bool isCopy,
        IPrintService printService,
        ReceiptBuilder builder,
        IStrings strings,
        IUserSession session)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(printService);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(session);

        Receipt = receipt;
        IsCopy = isCopy;
        _printService = printService;
        _builder = builder;
        _strings = strings;
        _session = session;

        string cashier = session.Current?.DisplayName ?? string.Empty;

        ThermalDocument = builder.Build(receipt, cashier, wide: false, isCopy);
        A4Document = builder.Build(receipt, cashier, wide: true, isCopy);
    }

    /// <summary>The sale the receipt is for.</summary>
    public SaleReceipt Receipt { get; }

    /// <summary>True when the receipt is a reprint rather than the first copy.</summary>
    public bool IsCopy { get; }

    /// <summary>The document for the 80 mm receipt.</summary>
    public PrintDocumentData ThermalDocument { get; }

    /// <summary>The document for the A4 invoice.</summary>
    public PrintDocumentData A4Document { get; }

    /// <summary>The title of the window.</summary>
    public string Title => _strings[StringKeys.PosReceipt];

    /// <summary>Raised when the receipt should be closed.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The label of the thermal print button.</summary>
    public string PrintThermalLabel => _strings[StringKeys.PosPrintThermal];

    /// <summary>The label of the A4 print button.</summary>
    public string PrintA4Label => _strings[StringKeys.PosPrintA4];

    /// <summary>The label of the close button.</summary>
    public string CloseLabel => _strings[StringKeys.PosClose];

    /// <summary>The document for the paper currently shown.</summary>
    public PrintDocumentData CurrentDocument => Paper == ReceiptPaper.A4 ? A4Document : ThermalDocument;

    /// <summary>The width of the paper currently shown, in points.</summary>
    public double CurrentContentWidth =>
        Paper == ReceiptPaper.A4 ? ReceiptBuilder.A4ContentWidth : ReceiptBuilder.ThermalContentWidth;

    /// <summary>Prints the receipt on an 80 mm roll.</summary>
    [RelayCommand]
    private async Task PrintThermalAsync() => await PrintAsync(ReceiptPaper.Thermal);

    /// <summary>Prints the receipt on an A4 sheet.</summary>
    [RelayCommand]
    private async Task PrintA4Async() => await PrintAsync(ReceiptPaper.A4);

    /// <summary>Closes the receipt.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnPaperChanged(ReceiptPaper value)
    {
        OnPropertyChanged(nameof(CurrentDocument));
        OnPropertyChanged(nameof(CurrentContentWidth));
    }

    private async Task PrintAsync(ReceiptPaper paper)
    {
        IReadOnlyList<PrinterInfo> printers = await _printService.GetPrintersAsync();

        if (printers.Count == 0)
        {
            StatusMessage = "There is no printer on this machine.";
            return;
        }

        PrinterInfo printer = printers.FirstOrDefault(candidate => candidate.IsDefault) ?? printers[0];

        PrintDocumentKind kind = paper == ReceiptPaper.A4
            ? PrintDocumentKind.InvoiceA4
            : PrintDocumentKind.Receipt80;

        PrintDocumentData document = paper == ReceiptPaper.A4 ? A4Document : ThermalDocument;

        StoreFlow.Application.Common.Result result = await _printService.PrintAsync(printer, kind, document);

        StatusMessage = result.IsSuccess ? string.Empty : result.Error.Message;
    }
}
