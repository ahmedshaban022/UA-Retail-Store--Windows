using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Runtime.InteropServices;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Application.Printing;

namespace StoreFlow.Desktop.Services;

/// <summary>
/// Prints documents through the printers installed on this machine.
/// </summary>
/// <remarks>
/// <para>The page is laid out by <see cref="PrintLayout"/>, which is the same layout the on-screen
/// preview is drawn from. What the cashier saw on the screen is what comes out of the printer
/// (PRD §5.1 GEN-05).</para>
/// <para>The Arabic face ships inside the application, so the receipt prints in Arabic on a shop
/// PC that has never had a font installed for it. The faces are added to the printer as private
/// fonts, which is why they do not appear in the list of installed fonts.</para>
/// <para>The spooler is given the document on a thread of its own. Printing blocks until the
/// spooler has taken the page, and a till that waits for that cannot be used while the paper is
/// coming out.</para>
/// </remarks>
public sealed class WindowsPrintService : IPrintService
{
    /// <summary>
    /// The private collection that holds the faces shipped with the application. It is held for the
    /// lifetime of the process, because a face taken out of a collection that has been collected
    /// stops being drawable.
    /// </summary>
    private static readonly PrivateFontCollection Faces = new();

    /// <summary>
    /// The face the receipt is printed in. It is the Arabic face that ships with the application
    /// when that face can be read, and a face Windows is certain to have when it cannot.
    /// </summary>
    private static readonly Lazy<FontFamily> Face = new(
        ResolveFace,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Sends a document to the printer.</summary>
    private readonly Action<PrinterInfo, PrintDocumentKind, PrintDocumentData> _sendToPrinter;

    /// <summary>Creates the service, which sends documents to the printer itself.</summary>
    public WindowsPrintService()
        : this(static (printer, kind, data) => PrintNow(printer, kind, data))
    {
    }

    /// <summary>
    /// Creates the service that sends documents through the given action.
    /// </summary>
    /// <param name="sendToPrinter">Sends the document on.</param>
    private WindowsPrintService(Action<PrinterInfo, PrintDocumentKind, PrintDocumentData> sendToPrinter)
    {
        _sendToPrinter = sendToPrinter;
    }

    /// <summary>
    /// Creates the service that sends documents through the given action instead of to a printer.
    /// </summary>
    /// <param name="sendToPrinter">
    /// Sends the document on. The application always uses the printer itself; this is the way a
    /// test puts a slow printer in its place and shows that the till is not held up while the
    /// paper comes out.
    /// </param>
    /// <returns>The service.</returns>
    public static WindowsPrintService SendingThrough(
        Action<PrinterInfo, PrintDocumentKind, PrintDocumentData> sendToPrinter)
    {
        ArgumentNullException.ThrowIfNull(sendToPrinter);

        return new WindowsPrintService(sendToPrinter);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var printers = new List<PrinterInfo>();
        string defaultName = new PrinterSettings().PrinterName;

        foreach (string? name in PrinterSettings.InstalledPrinters)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var printerSettings = new PrinterSettings { PrinterName = name };

            printers.Add(new PrinterInfo(
                name,
                string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase),
                null,
                SupportsA4(printerSettings)));
        }

        return Task.FromResult<IReadOnlyList<PrinterInfo>>(printers);
    }

    /// <inheritdoc />
    public Task<Result> PrintAsync(
        PrinterInfo printer,
        PrintDocumentKind kind,
        PrintDocumentData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(data);

        cancellationToken.ThrowIfCancellationRequested();

        // The spooler takes as long as it takes, and the till must not wait for it. The print runs
        // on a thread of its own, so the window keeps answering the cashier while the paper is
        // coming out.
        return Task.Run(() => Print(printer, kind, data, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Hands the document to the printer and waits for the spooler to take it.
    /// </summary>
    /// <param name="printer">The printer the document goes to.</param>
    /// <param name="kind">The shape of the document.</param>
    /// <param name="data">The document itself.</param>
    /// <param name="cancellationToken">The token that gives the print up.</param>
    /// <returns>The outcome of the print.</returns>
    private Result Print(
        PrinterInfo printer,
        PrintDocumentKind kind,
        PrintDocumentData data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _sendToPrinter(printer, kind, data);

            return Result.Success();
        }
#pragma warning disable CA1031 // A printer that is switched off must not take the till down.
        catch (Exception ex)
        {
            return Result.Failure("print.failed", ex.Message);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Lays the document out on the paper and sends it to the printer.
    /// </summary>
    /// <param name="printer">The printer the document goes to.</param>
    /// <param name="kind">The shape of the document.</param>
    /// <param name="data">The document itself.</param>
    private static void PrintNow(PrinterInfo printer, PrintDocumentKind kind, PrintDocumentData data)
    {
        using var document = new PrintDocument
        {
            DocumentName = data.Title,
            PrinterSettings = { PrinterName = printer.Name },
        };

        document.DefaultPageSettings.Landscape = false;
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        if (kind is PrintDocumentKind.Receipt58 or PrintDocumentKind.Receipt80)
        {
            document.DefaultPageSettings.PaperSize = new PaperSize(
                kind == PrintDocumentKind.Receipt58 ? "58 mm" : "80 mm",
                kind == PrintDocumentKind.Receipt58 ? 163 : 315,
                1200);
        }
        else
        {
            document.DefaultPageSettings.PaperSize = new PaperSize("A4", 595, 842);
        }

        double contentWidth = kind is PrintDocumentKind.Receipt58 or PrintDocumentKind.Receipt80
            ? (kind == PrintDocumentKind.Receipt58 ? 145d : 204d)
            : 481d;

        IReadOnlyList<PrintTextLine> layout = PrintLayout.Build(data, contentWidth);

        document.PrintPage += (_, args) =>
        {
            if (args.Graphics is { } graphics)
            {
                Draw(graphics, layout, contentWidth);
            }
        };

        document.Print();
    }

    /// <inheritdoc />
    public Task<Result> TestPrintAsync(PrinterInfo printer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printer);

        var data = new PrintDocumentData(
            "StoreFlow",
            [new PrintLine(printer.Name)],
            [new PrintLine("This page is a test. If you can read it, the printer works.")],
            null,
            [new PrintLine("StoreFlow", "bold", "center")]);

        return PrintAsync(printer, PrintDocumentKind.InvoiceA4, data, cancellationToken);
    }

    /// <summary>Draws the laid-out lines on the page.</summary>
    private static void Draw(Graphics graphics, IReadOnlyList<PrintTextLine> layout, double contentWidth)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(layout);

        GraphicsState state = graphics.Save();

        try
        {
            graphics.PageUnit = GraphicsUnit.Point;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var regular = new Font(Face.Value, (float)PrintLayout.BodyFontSize, FontStyle.Regular, GraphicsUnit.Point);
            using var bold = new Font(Face.Value, (float)PrintLayout.BodyFontSize, FontStyle.Bold, GraphicsUnit.Point);

            foreach (PrintTextLine line in layout)
            {
                if (line.IsRule)
                {
                    continue;
                }

                using var brush = new SolidBrush(Color.Black);
                using var layoutFormat = new StringFormat
                {
                    Alignment = line.Alignment switch
                    {
                        PrintTextAlignment.Center => StringAlignment.Center,
                        PrintTextAlignment.Right => StringAlignment.Far,
                        _ => StringAlignment.Near,
                    },
                    FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
                };

                float width = (float)Math.Min(line.Width, contentWidth - line.Left);

                graphics.DrawString(
                    line.Text,
                    line.IsBold ? bold : regular,
                    brush,
                    new RectangleF((float)line.Left, (float)line.Top, width, (float)line.Height),
                    layoutFormat);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    /// <summary>True when the printer can take an A4 sheet.</summary>
    private static bool SupportsA4(PrinterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            foreach (PaperSize size in settings.PaperSizes)
            {
                if (size.Kind == PaperKind.A4)
                {
                    return true;
                }
            }
        }
        catch (InvalidPrinterException)
        {
            // A printer that will not say what paper it takes is treated as a thermal printer.
            return false;
        }

        return false;
    }

    /// <summary>
    /// Reads the Arabic faces that ship with the application into the private collection, so they
    /// can be used for printing without being installed on the machine.
    /// </summary>
    /// <returns>The family to print in.</returns>
    private static FontFamily ResolveFace()
    {
        var resources = new ResourceManager("StoreFlow.Desktop.g", typeof(WindowsPrintService).Assembly);

        AddFace(resources, "fonts/tajawal-regular.ttf");
        AddFace(resources, "fonts/tajawal-bold.ttf");

        if (Faces.Families.Length > 0)
        {
            return Faces.Families[0];
        }

        // The faces could not be read. The printer falls back to a face Windows always has, which
        // carries the Arabic script as well as the Latin one.
        return new FontFamily("Tahoma");
    }

    /// <summary>Adds one embedded face to the private collection.</summary>
    /// <param name="resources">The application's own resources.</param>
    /// <param name="name">The name of the face inside them.</param>
    private static void AddFace(ResourceManager resources, string name)
    {
        using Stream? stream = resources.GetStream(name);

        if (stream is null)
        {
            return;
        }

        byte[] bytes;

        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        IntPtr memory = Marshal.AllocCoTaskMem(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            Faces.AddMemoryFont(memory, bytes.Length);
        }
        finally
        {
            Marshal.FreeCoTaskMem(memory);
        }
    }
}
