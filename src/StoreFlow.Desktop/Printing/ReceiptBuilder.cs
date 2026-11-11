using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Pos;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

namespace StoreFlow.Desktop.Printing;

/// <summary>
/// Builds the printable document for a sale. The same document drives the on-screen preview and the
/// printed sheet, so what the cashier sees is what the customer is handed (PRD §5.1 GEN-05).
/// </summary>
public sealed class ReceiptBuilder(IStrings strings, IThemeService themeService)
{
    /// <summary>The width of an 80 mm receipt, in points, without a margin.</summary>
    public const double ThermalWidth = 226.77d;

    /// <summary>The width of an A4 sheet, in points, without a margin.</summary>
    public const double A4Width = 595.28d;

    /// <summary>The margin around the text, in points.</summary>
    public const double Margin = 10d;

    /// <summary>The width of the text on an 80 mm receipt, in points.</summary>
    public static double ThermalContentWidth => ThermalWidth - (2d * Margin);

    /// <summary>The width of the text on an A4 sheet, in points.</summary>
    public static double A4ContentWidth => A4Width - (2d * Margin);

    /// <summary>
    /// Builds the document for a sale, in the shape the paper wants.
    /// </summary>
    /// <param name="receipt">The sale to write down.</param>
    /// <param name="cashierName">The name of the cashier.</param>
    /// <param name="wide">True for the A4 invoice, false for the 80 mm receipt.</param>
    /// <param name="isCopy">True for a reprint, which is marked as a copy.</param>
    /// <returns>The printable document.</returns>
    public PrintDocumentData Build(SaleReceipt receipt, string cashierName, bool wide, bool isCopy)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var header = new List<PrintLine>
        {
            new($"{strings[StringKeys.PosReceiptNumber]}: {receipt.Number}"),
            new($"{strings[StringKeys.PosReceiptDate]}: {receipt.BusinessDate:yyyy-MM-dd}"),
        };

        if (!string.IsNullOrWhiteSpace(cashierName))
        {
            header.Add(new($"{strings[StringKeys.PosReceiptCashier]}: {cashierName}"));
        }

        if (isCopy)
        {
            // The stamp is printed on paper, so it is written in the language the shop reads.
            header.Add(new(strings[StringKeys.PosCopyStamp], "bold", "center"));
        }

        PrintTable table = wide
            ? BuildWideTable(receipt)
            : BuildThermalTable(receipt);

        var lines = new List<PrintLine>
        {
            new($"{strings[StringKeys.PosSubtotal]}: {Format(receipt.Totals.Subtotal)}", null, "right"),
            new($"{strings[StringKeys.PosDiscount]}: {Format(receipt.Totals.Discount)}", null, "right"),
            new($"{strings[StringKeys.PosTaxable]}: {Format(receipt.Totals.Total - receipt.Totals.Tax)}", null, "right"),
            new($"{strings[StringKeys.PosTax]}: {Format(receipt.Totals.Tax)}", null, "right"),
            new($"{strings[StringKeys.PosGrandTotal]}: {Format(receipt.Totals.Total)}", "total", "right"),
            new($"{strings[StringKeys.PosPaid]}: {Format(receipt.Totals.Paid)}", null, "right"),
        };

        // What the customer handed over is worth saying out loud, because it is not the same
        // figure as the amount that paid for the sale.
        PaymentSummary? cash = receipt.Payments.FirstOrDefault(payment => payment.Method == PaymentMethod.Cash);

        if (cash is not null)
        {
            lines.Add(new($"{strings[StringKeys.PosCashReceived]}: {Format(cash.CashReceived)}", null, "right"));
        }

        lines.Add(new($"{strings[StringKeys.PosChange]}: {Format(receipt.Totals.Change)}", null, "right"));

        var footer = new List<PrintLine>
        {
            new(themeService.Current.ProductName.Resolve("ar"), "bold", "center"),
            new(themeService.Current.ProductName.Resolve("en"), "bold", "center"),
        };

        return new PrintDocumentData(
            strings[StringKeys.PosReceipt],
            header,
            lines,
            table,
            footer,
            isCopy);
    }

    private PrintTable BuildWideTable(SaleReceipt receipt)
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (CartLine line in receipt.Lines)
        {
            rows.Add(
            [
                line.Name,
                line.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                Format(line.UnitPrice),
                Format(line.Total),
            ]);
        }

        return new PrintTable(
        [
            new PrintColumn(strings[StringKeys.PosColumnProduct], 3.5d),
            new PrintColumn(strings[StringKeys.PosColumnQuantity], 1d, "right"),
            new PrintColumn(strings[StringKeys.PosColumnPrice], 1.5d, "right"),
            new PrintColumn(strings[StringKeys.PosColumnTotal], 1.5d, "right"),
        ], rows);
    }

    private PrintTable BuildThermalTable(SaleReceipt receipt)
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (CartLine line in receipt.Lines)
        {
            rows.Add(
            [
                $"{line.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} x {line.Name}",
                Format(line.Total),
            ]);
        }

        return new PrintTable(
        [
            new PrintColumn(strings[StringKeys.PosReceiptItems], 3d),
            new PrintColumn(strings[StringKeys.PosColumnTotal], 1.4d, "right"),
        ], rows);
    }

    private string Format(Money amount) => themeService.FormatMoney(amount);
}
