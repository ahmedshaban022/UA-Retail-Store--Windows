using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;

namespace StoreFlow.Application.Pos;

/// <summary>
/// The shapes that cross the boundary of the point of sale, from the till to the database and back.
/// </summary>
/// <remarks>
/// The till sends intent, not numbers it worked out itself: a line says which product and how much
/// of it, and the price and the tax come from the catalogue. That is what keeps the shelf, the
/// invoice and the receipt telling the same story.
/// </remarks>

/// <summary>A line the cashier wants to sell.</summary>
/// <param name="ProductId">The product being sold.</param>
/// <param name="Quantity">The quantity, in the product's own selling unit.</param>
/// <param name="UnitPrice">The price the cashier is charging for one unit.</param>
/// <param name="LineDiscount">The discount granted on this line alone.</param>
public sealed record SaleLineInput(
    int ProductId,
    decimal Quantity,
    Money UnitPrice,
    DiscountSpec LineDiscount);

/// <summary>A payment the customer made.</summary>
/// <param name="Method">How the customer paid.</param>
/// <param name="Amount">The amount applied to the invoice.</param>
/// <param name="CashReceived">The cash handed over, when the method is cash.</param>
/// <param name="Reference">An optional reference, such as a card terminal transaction id.</param>
public sealed record PaymentInput(
    PaymentMethod Method,
    Money Amount,
    Money CashReceived,
    string? Reference);

/// <summary>
/// What the till needs to know about a product in order to price a line: its identity, its unit,
/// its price, its tax treatment and how much of it is on the shelf.
/// </summary>
/// <param name="Id">The product id.</param>
/// <param name="Sku">The stock-keeping code.</param>
/// <param name="Name">The name in the current language.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="Barcode">The primary barcode, when the product has one.</param>
/// <param name="UnitName">The selling unit's name.</param>
/// <param name="UnitScale">The precision of the selling unit.</param>
/// <param name="SalePrice">The catalogue price of one unit.</param>
/// <param name="TaxRateBasisPoints">The tax rate in basis points.</param>
/// <param name="PriceIncludesTax">True when the selling price already carries the tax.</param>
/// <param name="IsActive">False when the product has been deactivated.</param>
/// <param name="OnHandScaled">The sellable quantity on the shelf, scaled by the unit's precision.</param>
public sealed record PosProductFacts(
    int Id,
    string Sku,
    string Name,
    string NameAr,
    string? Barcode,
    string UnitName,
    int UnitScale,
    Money SalePrice,
    int TaxRateBasisPoints,
    bool PriceIncludesTax,
    bool IsActive,
    long OnHandScaled);

/// <summary>A priced line of the cart, ready to be shown and ready to be posted.</summary>
/// <param name="ProductId">The product being sold.</param>
/// <param name="Sku">The stock-keeping code, snapshotted for the invoice.</param>
/// <param name="Name">The name, snapshotted for the invoice.</param>
/// <param name="NameAr">The Arabic name, snapshotted for the invoice.</param>
/// <param name="Barcode">The barcode, snapshotted for the invoice.</param>
/// <param name="UnitName">The selling unit, snapshotted for the invoice.</param>
/// <param name="UnitScale">The precision of the selling unit.</param>
/// <param name="Quantity">The quantity, in the selling unit.</param>
/// <param name="QuantityScaled">The quantity in the persisted scaled form.</param>
/// <param name="UnitPrice">The price charged for one unit.</param>
/// <param name="Discount">The discount granted on the line.</param>
/// <param name="DiscountAmount">The discount in money.</param>
/// <param name="Gross">The line before any discount.</param>
/// <param name="Net">The line after the line discount, before the invoice discount.</param>
/// <param name="InvoiceDiscountShare">The part of the invoice discount carried by this line.</param>
/// <param name="TaxableBase">The amount the tax is computed on.</param>
/// <param name="Tax">The tax on the line.</param>
/// <param name="Total">The amount the customer pays for this line.</param>
public sealed record CartLine(
    int ProductId,
    string Sku,
    string Name,
    string NameAr,
    string? Barcode,
    string UnitName,
    int UnitScale,
    decimal Quantity,
    long QuantityScaled,
    Money UnitPrice,
    DiscountSpec Discount,
    Money DiscountAmount,
    Money Gross,
    Money Net,
    Money InvoiceDiscountShare,
    Money TaxableBase,
    Money Tax,
    Money Total);

/// <summary>The money of a cart, in the order the till shows it.</summary>
/// <param name="Subtotal">The sum of the lines before any discount.</param>
/// <param name="LineDiscount">The sum of the line discounts.</param>
/// <param name="InvoiceDiscount">The discount granted on the whole sale.</param>
/// <param name="Discount">Every discount given, line and invoice together.</param>
/// <param name="Tax">The tax on the whole sale.</param>
/// <param name="Total">What the customer owes.</param>
/// <param name="Paid">What the customer handed over.</param>
/// <param name="Change">What is given back.</param>
public sealed record PosTotals(
    Money Subtotal,
    Money LineDiscount,
    Money InvoiceDiscount,
    Money Discount,
    Money Tax,
    Money Total,
    Money Paid,
    Money Change);

/// <summary>A payment as it is written down on the invoice.</summary>
/// <param name="Method">How the customer paid.</param>
/// <param name="Amount">The amount applied to the invoice.</param>
/// <param name="CashReceived">The cash handed over, when the method is cash.</param>
/// <param name="Change">The change given back, when the method is cash.</param>
/// <param name="Reference">An optional reference, such as a card terminal transaction id.</param>
public sealed record PaymentSummary(
    PaymentMethod Method,
    Money Amount,
    Money CashReceived,
    Money Change,
    string? Reference);

/// <summary>A problem with the cart, with a stable code the interface can translate.</summary>
/// <param name="Code">The stable code, for example <c>pos.insufficient_stock</c>.</param>
/// <param name="Message">A message safe to show a user.</param>
/// <param name="ProductId">The product the problem is about, when it is about one.</param>
public sealed record PosIssue(string Code, string Message, int? ProductId = null);

/// <summary>The priced cart: its lines, its money, and anything that is wrong with it.</summary>
/// <param name="Lines">The priced lines.</param>
/// <param name="Totals">The money of the cart.</param>
/// <param name="Issues">The problems found, if any.</param>
public sealed record PosCart(IReadOnlyList<CartLine> Lines, PosTotals Totals, IReadOnlyList<PosIssue> Issues)
{
    /// <summary>True when the cart may be posted.</summary>
    public bool IsValid => Issues.Count == 0;
}

/// <summary>What a posted sale gives back to the till.</summary>
/// <param name="InvoiceId">The invoice's id.</param>
/// <param name="Number">The invoice's number.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="PostedUtc">The moment it was posted.</param>
/// <param name="Totals">The money of the sale.</param>
/// <param name="Lines">The priced lines, as they were written down.</param>
/// <param name="Payments">The payments, as they were written down.</param>
/// <param name="WasReplay">True when this sale had already been posted and the answer was repeated.</param>
public sealed record SaleReceipt(
    int InvoiceId,
    string Number,
    DateOnly BusinessDate,
    DateTimeOffset PostedUtc,
    PosTotals Totals,
    IReadOnlyList<CartLine> Lines,
    IReadOnlyList<PaymentSummary> Payments,
    bool WasReplay);

/// <summary>A suspended cart, as the till lists it.</summary>
/// <param name="Id">The held cart's id.</param>
/// <param name="Label">The name the cashier gave it.</param>
/// <param name="LineCount">How many lines it holds.</param>
/// <param name="TotalQuantity">The sum of the quantities on those lines.</param>
/// <param name="Total">What the cart is worth.</param>
/// <param name="HeldUtc">When it was suspended.</param>
public sealed record HeldCartSummary(
    int Id,
    string Label,
    int LineCount,
    decimal TotalQuantity,
    Money Total,
    DateTimeOffset HeldUtc);
