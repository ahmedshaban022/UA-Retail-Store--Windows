namespace StoreFlow.Application.Abstractions;

using StoreFlow.Application.Common;

/// <summary>The documents the application can print.</summary>
public enum PrintDocumentKind
{
    /// <summary>A 58 mm thermal receipt.</summary>
    Receipt58 = 0,

    /// <summary>An 80 mm thermal receipt.</summary>
    Receipt80 = 1,

    /// <summary>An A4 invoice.</summary>
    InvoiceA4 = 2,

    /// <summary>An A4 report with the active filter summary.</summary>
    ReportA4 = 3,

    /// <summary>An A4 stock count sheet.</summary>
    StockCountA4 = 4,

    /// <summary>An A4 purchase order.</summary>
    PurchaseOrderA4 = 5,

    /// <summary>A printable shelf label.</summary>
    ShelfLabel = 6,
}

/// <summary>A printer the application can send a job to.</summary>
/// <param name="Name">The Windows printer name.</param>
/// <param name="IsDefault">True for the system default printer.</param>
/// <param name="PortName">The printer port, when known.</param>
/// <param name="SupportsA4">True when the printer reports an A4-capable paper size.</param>
public sealed record PrinterInfo(string Name, bool IsDefault, string? PortName, bool SupportsA4);

/// <summary>One line of printed text.</summary>
/// <param name="Text">The text to print.</param>
/// <param name="Style">An optional style key such as <c>bold</c> or <c>total</c>.</param>
/// <param name="Alignment">An optional alignment key: <c>left</c>, <c>center</c> or <c>right</c>.</param>
public sealed record PrintLine(string Text, string? Style = null, string? Alignment = null);

/// <summary>A column in a printed table.</summary>
/// <param name="Header">The column heading.</param>
/// <param name="Width">The relative column width.</param>
/// <param name="Alignment">The alignment key for the column.</param>
public sealed record PrintColumn(string Header, double Width = 1d, string? Alignment = null);

/// <summary>A printed table.</summary>
/// <param name="Columns">The column definitions.</param>
/// <param name="Rows">The row cells, in column order.</param>
public sealed record PrintTable(IReadOnlyList<PrintColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// The content of a printable document. Deliberately primitive — a list of lines and an optional
/// table — so the renderer stays independent of the domain model.
/// </summary>
/// <param name="Title">The document title.</param>
/// <param name="HeaderLines">Header content, printed above the body.</param>
/// <param name="Lines">Body lines.</param>
/// <param name="Table">An optional table.</param>
/// <param name="FooterLines">Footer content, printed below the body.</param>
/// <param name="IsCopy">True for a reprint, which is watermarked as a copy.</param>
public sealed record PrintDocumentData(
    string Title,
    IReadOnlyList<PrintLine> HeaderLines,
    IReadOnlyList<PrintLine> Lines,
    PrintTable? Table = null,
    IReadOnlyList<PrintLine>? FooterLines = null,
    bool IsCopy = false);

/// <summary>
/// Renders and prints documents. The same rendered output drives the on-screen preview and the
/// printed page, so what the user sees is what the paper shows (PRD §5.1 GEN-05).
/// </summary>
public interface IPrintService
{
    /// <summary>Lists the printers installed on this machine.</summary>
    Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default);

    /// <summary>Prints a document to the given printer.</summary>
    Task<Result> PrintAsync(
        PrinterInfo printer,
        PrintDocumentKind kind,
        PrintDocumentData data,
        CancellationToken cancellationToken = default);

    /// <summary>Prints a short test page, used by the first-run wizard.</summary>
    Task<Result> TestPrintAsync(PrinterInfo printer, CancellationToken cancellationToken = default);
}
