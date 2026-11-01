namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

/// <summary>
/// A recorded payment. This is a declaration that the customer paid in this way; it never claims an
/// external processor authorised the transaction (PRD §5.5 POS-05).
/// </summary>
/// <remarks>
/// A row is either money coming in (a sale) or money going back out (a refund). A refund belongs to
/// a return or to the reversal of an invoice, and it is marked by <see cref="IsRefund"/> so the
/// register can show it on the other side of the column from the sale it undoes.
/// </remarks>
public sealed class Payment
{
    public int Id { get; set; }

    /// <summary>
    /// The invoice the payment settles. Null when the row is a refund against a return, because a
    /// return is its own document and not an invoice.
    /// </summary>
    public int? SalesInvoiceId { get; set; }

    public SalesInvoice? SalesInvoice { get; set; }

    /// <summary>The return the refund belongs to, when this row is a refund of a return.</summary>
    public int? SalesReturnId { get; set; }

    public SalesReturn? SalesReturn { get; set; }

    /// <summary>True when the money on this row is going back to the customer.</summary>
    public bool IsRefund { get; set; }

    public PaymentMethod Method { get; set; }

    /// <summary>The amount applied to the invoice.</summary>
    public Money Amount { get; set; }

    /// <summary>The cash handed over, when the method is cash.</summary>
    public Money CashReceived { get; set; }

    /// <summary>The change given back.</summary>
    public Money Change { get; set; }

    /// <summary>An optional reference, such as a card terminal transaction id.</summary>
    public string? Reference { get; set; }
}
