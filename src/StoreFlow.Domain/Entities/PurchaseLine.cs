namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Money;

/// <summary>
/// A line on a purchase document. The acquisition cost on a posted line is immutable historical
/// record; editing the product's current cost never recalculates it (PRD §5.3 PUR-04, §6.1).
/// </summary>
public sealed class PurchaseLine
{
    public int Id { get; set; }

    public int PurchaseDocumentId { get; set; }

    public PurchaseDocument? PurchaseDocument { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public long QuantityScaled { get; set; }

    public int QuantityScale { get; set; }

    /// <summary>The actual acquisition cost per unit recorded on this line.</summary>
    public Money UnitCost { get; set; }

    public Money Tax { get; set; }

    /// <summary>The line total: quantity times unit cost, plus tax.</summary>
    public Money Total { get; set; }

    public string? Notes { get; set; }
}
