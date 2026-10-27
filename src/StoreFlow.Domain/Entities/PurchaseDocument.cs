namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

/// <summary>
/// A purchase document. Saving a draft changes nothing in stock; posting it atomically increases
/// stock, updates the moving average and records the document number, user and timestamp
/// (PRD §4.2, §5.3).
/// </summary>
public sealed class PurchaseDocument
{
    public int Id { get; set; }

    /// <summary>Unique sequential document number.</summary>
    public string Number { get; set; } = string.Empty;

    public int? SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public Money Subtotal { get; set; }

    public Money Discount { get; set; }

    public Money Tax { get; set; }

    public Money Total { get; set; }

    public DateOnly BusinessDate { get; set; }

    /// <summary>The authoritative UTC posting timestamp; null while the document is a draft.</summary>
    public DateTimeOffset? PostedUtc { get; set; }

    public int? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Set when this document reverses a previously posted purchase (PRD PUR-06).</summary>
    public int? ReversesDocumentId { get; set; }

    public PurchaseDocument? ReversesDocument { get; set; }

    public string? Notes { get; set; }

    public ICollection<PurchaseLine> Lines { get; } = new List<PurchaseLine>();
}
