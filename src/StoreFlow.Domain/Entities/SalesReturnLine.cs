namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Money;

/// <summary>
/// A returned line. The original unit cost is preserved so the return can restore stock at the cost
/// it was sold at, and so gross profit is not rewritten by the return.
/// </summary>
public sealed class SalesReturnLine
{
    public int Id { get; set; }

    public int SalesReturnId { get; set; }

    public SalesReturn? SalesReturn { get; set; }

    /// <summary>The invoice line this return refers to.</summary>
    public int SalesInvoiceLineId { get; set; }

    public SalesInvoiceLine? SalesInvoiceLine { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public long QuantityScaled { get; set; }

    public int QuantityScale { get; set; }

    /// <summary>
    /// True when the item goes back on the shelf. Damaged items are recorded separately and never
    /// inflate the quantity available to sell (PRD INV-08).
    /// </summary>
    public bool IsResellable { get; set; }

    /// <summary>The amount refunded for this line.</summary>
    public Money RefundAmount { get; set; }

    /// <summary>The unit cost captured on the original invoice line.</summary>
    public Money OriginalUnitCost { get; set; }

    /// <summary>An optional line-level reason.</summary>
    public string? Reason { get; set; }
}
