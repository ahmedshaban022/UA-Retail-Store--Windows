namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

/// <summary>
/// A recorded payment. This is a declaration that the customer paid in this way; it never claims an
/// external processor authorised the transaction (PRD §5.5 POS-05).
/// </summary>
public sealed class Payment
{
    public int Id { get; set; }

    public int SalesInvoiceId { get; set; }

    public SalesInvoice? SalesInvoice { get; set; }

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
