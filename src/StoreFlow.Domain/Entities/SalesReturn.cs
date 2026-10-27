namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

/// <summary>
/// A return against a posted invoice. A return never deletes the original sale; it is a linked
/// document that reverses stock and records the refund (PRD §4.4, §5.7).
/// </summary>
public sealed class SalesReturn
{
    public int Id { get; set; }

    /// <summary>Unique sequential return number.</summary>
    public string Number { get; set; } = string.Empty;

    public int OriginalInvoiceId { get; set; }

    public SalesInvoice? OriginalInvoice { get; set; }

    /// <summary>The reason for the return, captured for the audit trail.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The total amount refunded.</summary>
    public Money RefundTotal { get; set; }

    /// <summary>How the refund was made.</summary>
    public PaymentMethod RefundMethod { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public DateOnly BusinessDate { get; set; }

    public DateTimeOffset PostedUtc { get; set; }

    public string? Notes { get; set; }

    public ICollection<SalesReturnLine> Lines { get; } = new List<SalesReturnLine>();
}
