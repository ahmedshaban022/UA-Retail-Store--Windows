namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Money;

/// <summary>
/// A cached view of a product's stock position, kept consistent with the ledger inside the same
/// transaction and reconciled periodically against it (PRD §5.4 invariant 1).
/// </summary>
public sealed class StockBalance
{
    /// <summary>The product this balance belongs to; it is also the primary key.</summary>
    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>The sellable quantity on hand, scaled by the product unit's precision.</summary>
    public long QuantityOnHandScaled { get; set; }

    /// <summary>Damaged stock, tracked separately so it never inflates the sellable quantity.</summary>
    public long DamagedQuantityScaled { get; set; }

    /// <summary>The moving weighted average cost per base unit.</summary>
    public Money AverageUnitCost { get; set; }

    public int QuantityScale { get; set; }

    /// <summary>Optimistic concurrency token, incremented on every balance update.</summary>
    public long Version { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
