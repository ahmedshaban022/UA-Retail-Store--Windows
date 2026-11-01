using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

namespace StoreFlow.Application.Sales;

/// <summary>
/// The shapes that cross the boundary of the sales register and the returns, from the database to the
/// screen and back.
/// </summary>
/// <remarks>
/// A return carries no price of its own: the operator says which lines come back and how many, and
/// the money comes from the snapshots on the original invoice. That is what stops a return from
/// becoming a second, easier way to change the price of a sale that has already happened.
/// </remarks>

/// <summary>The kind of document a row of the sales register is.</summary>
public enum SalesDocumentKind
{
    /// <summary>A posted sale.</summary>
    Invoice = 0,

    /// <summary>A return against a posted sale.</summary>
    Return = 1,

    /// <summary>The reversal of a posted sale.</summary>
    Reversal = 2,
}

/// <summary>A line the operator wants to give back.</summary>
/// <param name="SalesInvoiceLineId">The line of the original invoice.</param>
/// <param name="Quantity">The quantity coming back, in the product's own selling unit.</param>
/// <param name="IsResellable">True when the goods go back on the shelf; false when they are damaged.</param>
/// <param name="Reason">An optional line-level reason.</param>
public sealed record ReturnLineInput(
    int SalesInvoiceLineId,
    decimal Quantity,
    bool IsResellable,
    string? Reason = null);

/// <summary>One row of the sales register.</summary>
/// <param name="Id">The document's id.</param>
/// <param name="Number">The document's number.</param>
/// <param name="Kind">What kind of document this is.</param>
/// <param name="Status">The document's status.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="PostedUtc">The authoritative posting timestamp.</param>
/// <param name="CashierName">The name of the person who posted it.</param>
/// <param name="Total">
/// The amount of the document, signed the way the register shows it: a sale is positive, a return
/// and a reversal are negative, because in both of those money is going back to the customer.
/// </param>
/// <param name="ReturnedAmount">How much of this invoice has already been given back.</param>
/// <param name="LineCount">How many lines the document holds.</param>
/// <param name="RelatedId">The invoice this document belongs to, for a return or a reversal.</param>
/// <param name="RelatedNumber">The number of the invoice this document belongs to.</param>
public sealed record SalesRegisterRow(
    int Id,
    string Number,
    SalesDocumentKind Kind,
    DocumentStatus Status,
    DateOnly BusinessDate,
    DateTimeOffset PostedUtc,
    string CashierName,
    Money Total,
    Money ReturnedAmount,
    int LineCount,
    int RelatedId,
    string? RelatedNumber);

/// <summary>One line of an invoice, as it was written down at posting time.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="ProductId">The product the line is for.</param>
/// <param name="Name">The product name at posting time.</param>
/// <param name="Sku">The stock-keeping code at posting time.</param>
/// <param name="UnitName">The selling unit at posting time.</param>
/// <param name="Quantity">The quantity sold, in the selling unit.</param>
/// <param name="QuantityScaled">The quantity sold, in the persisted scaled form.</param>
/// <param name="QuantityScale">The precision of the selling unit.</param>
/// <param name="ReturnedQuantityScaled">How much of it has already come back.</param>
/// <param name="UnitPrice">The price charged for one unit.</param>
/// <param name="Discount">The discount granted on the line.</param>
/// <param name="Tax">The tax charged on the line.</param>
/// <param name="Total">The amount the customer paid for the line.</param>
/// <param name="UnitCost">The cost of goods sold per unit, snapshotted at posting time.</param>
public sealed record SalesLineDetails(
    int Id,
    int ProductId,
    string Name,
    string Sku,
    string UnitName,
    decimal Quantity,
    long QuantityScaled,
    int QuantityScale,
    long ReturnedQuantityScaled,
    Money UnitPrice,
    Money Discount,
    Money Tax,
    Money Total,
    Money UnitCost);

/// <summary>A return recorded against an invoice.</summary>
/// <param name="Id">The return's id.</param>
/// <param name="Number">The return's number.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="PostedUtc">The authoritative posting timestamp.</param>
/// <param name="CashierName">The name of the person who posted it.</param>
/// <param name="RefundTotal">The amount that went back to the customer.</param>
/// <param name="RefundMethod">How the money went back.</param>
/// <param name="Reason">Why the goods came back.</param>
/// <param name="IsFullReturn">True when every unit on the invoice came back.</param>
public sealed record SalesReturnSummary(
    int Id,
    string Number,
    DateOnly BusinessDate,
    DateTimeOffset PostedUtc,
    string CashierName,
    Money RefundTotal,
    PaymentMethod RefundMethod,
    string Reason,
    bool IsFullReturn);

/// <summary>Everything the detail screen shows about one invoice.</summary>
/// <param name="Id">The invoice's id.</param>
/// <param name="Number">The invoice's number.</param>
/// <param name="Status">The invoice's status.</param>
/// <param name="Kind">Whether this document is a sale or the reversal of one.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="PostedUtc">The authoritative posting timestamp.</param>
/// <param name="CashierName">The name of the person who posted it.</param>
/// <param name="PaymentMethod">How the customer paid.</param>
/// <param name="Subtotal">The sum of the line totals before any discount.</param>
/// <param name="Discount">Every discount given, line and invoice together.</param>
/// <param name="Tax">The tax charged.</param>
/// <param name="Total">The amount the customer paid.</param>
/// <param name="Paid">What the customer handed over.</param>
/// <param name="Change">What was given back.</param>
/// <param name="ReturnedAmount">How much of this invoice has been given back.</param>
/// <param name="Lines">The lines of the invoice.</param>
/// <param name="Returns">The returns recorded against the invoice.</param>
/// <param name="ReversesNumber">The invoice this document reverses, when it is a reversal.</param>
/// <param name="ReversedByNumber">The reversal of this invoice, when it has been reversed.</param>
/// <param name="Notes">The free-text note on the invoice.</param>
public sealed record SalesInvoiceDetails(
    int Id,
    string Number,
    DocumentStatus Status,
    SalesDocumentKind Kind,
    DateOnly BusinessDate,
    DateTimeOffset PostedUtc,
    string CashierName,
    PaymentMethod PaymentMethod,
    Money Subtotal,
    Money Discount,
    Money Tax,
    Money Total,
    Money Paid,
    Money Change,
    Money ReturnedAmount,
    IReadOnlyList<SalesLineDetails> Lines,
    IReadOnlyList<SalesReturnSummary> Returns,
    string? ReversesNumber,
    string? ReversedByNumber,
    string? Notes);

/// <summary>One line of a posted return or reversal.</summary>
/// <param name="SalesInvoiceLineId">The line of the original invoice.</param>
/// <param name="Name">The product name, snapshotted from the original invoice.</param>
/// <param name="Quantity">The quantity that came back.</param>
/// <param name="IsResellable">True when the goods went back on the shelf.</param>
/// <param name="RefundAmount">The amount refunded for this line.</param>
public sealed record ReturnLineSummary(
    int SalesInvoiceLineId,
    string Name,
    decimal Quantity,
    bool IsResellable,
    Money RefundAmount);

/// <summary>What a posted return or reversal gives back to the screen.</summary>
/// <param name="Kind">Whether this was a return or a reversal.</param>
/// <param name="DocumentId">The id of the document that was posted.</param>
/// <param name="Number">The number of the document that was posted.</param>
/// <param name="OriginalInvoiceId">The invoice that was corrected.</param>
/// <param name="OriginalInvoiceNumber">The number of the invoice that was corrected.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="PostedUtc">The authoritative posting timestamp.</param>
/// <param name="RefundTotal">The amount that went back to the customer.</param>
/// <param name="RefundMethod">How the money went back.</param>
/// <param name="Reason">Why the invoice was corrected.</param>
/// <param name="Lines">The lines that came back.</param>
public sealed record RefundReceipt(
    SalesDocumentKind Kind,
    int DocumentId,
    string Number,
    int OriginalInvoiceId,
    string OriginalInvoiceNumber,
    DateOnly BusinessDate,
    DateTimeOffset PostedUtc,
    Money RefundTotal,
    PaymentMethod RefundMethod,
    string Reason,
    IReadOnlyList<ReturnLineSummary> Lines);

/// <summary>A cashier the register can be filtered by.</summary>
/// <param name="Id">The user's id.</param>
/// <param name="DisplayName">The name the register shows.</param>
public sealed record SalesCashierOption(int Id, string DisplayName)
{
    /// <summary>
    /// Writes the name of the cashier. The select shows the text of the record, so it must not
    /// show the record's generated <c>ToString</c>, which reads as
    /// <c>SalesCashierOption { Id = 1, DisplayName = Owner }</c> instead of the name of the cashier.
    /// </summary>
    public override string ToString() => DisplayName;
}

/// <summary>Everything the detail screen shows about one return.</summary>
/// <param name="Id">The return's id.</param>
/// <param name="Number">The return's number.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="PostedUtc">The authoritative posting timestamp.</param>
/// <param name="CashierName">The name of the person who posted it.</param>
/// <param name="OriginalInvoiceId">The invoice the goods came back from.</param>
/// <param name="OriginalInvoiceNumber">The number of the invoice the goods came back from.</param>
/// <param name="Reason">Why the goods came back.</param>
/// <param name="RefundTotal">The amount that went back to the customer.</param>
/// <param name="RefundTax">The tax that came back with the refund.</param>
/// <param name="RefundMethod">How the money went back.</param>
/// <param name="Lines">The lines that came back.</param>
/// <param name="Notes">The free-text note on the return.</param>
public sealed record SalesReturnDetails(
    int Id,
    string Number,
    DateOnly BusinessDate,
    DateTimeOffset PostedUtc,
    string CashierName,
    int OriginalInvoiceId,
    string OriginalInvoiceNumber,
    string Reason,
    Money RefundTotal,
    Money RefundTax,
    PaymentMethod RefundMethod,
    IReadOnlyList<ReturnLineSummary> Lines,
    string? Notes);
