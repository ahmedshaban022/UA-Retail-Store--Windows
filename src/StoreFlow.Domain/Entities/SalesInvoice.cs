namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

/// <summary>
/// A posted sales invoice. Posted invoices are never edited or deleted; corrections happen through a
/// linked return or reversal (PRD §5.7).
/// </summary>
public sealed class SalesInvoice
{
    public int Id { get; set; }

    /// <summary>Unique sequential local invoice number.</summary>
    public string Number { get; set; } = string.Empty;

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    /// <summary>Sum of the line totals before any discount.</summary>
    public Money Subtotal { get; set; }

    /// <summary>Total discount applied across lines and the invoice.</summary>
    public Money Discount { get; set; }

    /// <summary>Total tax charged.</summary>
    public Money Tax { get; set; }

    /// <summary>The amount due: subtotal minus discount plus tax.</summary>
    public Money Total { get; set; }

    /// <summary>The amount the customer actually paid.</summary>
    public Money Paid { get; set; }

    /// <summary>Change given back.</summary>
    public Money Change { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The user-facing document date, separate from the authoritative posting timestamp.</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>The authoritative UTC posting timestamp.</summary>
    public DateTimeOffset PostedUtc { get; set; }

    /// <summary>
    /// The client-supplied idempotency key for this checkout. A repeated key returns the original
    /// invoice instead of creating a second one (PRD POS-07).
    /// </summary>
    public Guid ClientOperationId { get; set; }

    /// <summary>
    /// The invoice this one reverses, when this document is the reversal of a posted invoice
    /// (PRD §5.7). A posted invoice is never edited or deleted; it is undone by a linked document.
    /// </summary>
    public int? ReversesDocumentId { get; set; }

    public SalesInvoice? ReversesDocument { get; set; }

    /// <summary>The invoices that reverse this one. A posted invoice may be reversed only once.</summary>
    public ICollection<SalesInvoice> ReversedBy { get; } = new List<SalesInvoice>();

    public string? Notes { get; set; }

    public ICollection<SalesInvoiceLine> Lines { get; } = new List<SalesInvoiceLine>();

    public ICollection<Payment> Payments { get; } = new List<Payment>();
}
