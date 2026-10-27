namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

/// <summary>
/// An entry in the authoritative stock ledger. Movements are append-only: a posted movement is never
/// updated or deleted, and a correction is a new referenced movement (PRD §5.4 invariants).
/// </summary>
public sealed class StockMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public StockMovementType MovementType { get; set; }

    /// <summary>Whether the movement affects the sellable quantity or the damaged bucket.</summary>
    public StockDisposition Disposition { get; set; } = StockDisposition.Sellable;

    /// <summary>The document that caused this movement, when it has one.</summary>
    public int? DocumentId { get; set; }

    /// <summary>The document line that caused this movement, when it has one.</summary>
    public int? DocumentLineId { get; set; }

    /// <summary>The signed change in quantity, scaled by the product unit's precision.</summary>
    public long QuantityDeltaScaled { get; set; }

    public int QuantityScale { get; set; }

    /// <summary>The unit cost carried by the movement.</summary>
    public Money UnitCost { get; set; }

    /// <summary>
    /// The sellable balance immediately after this movement was applied. Storing it makes the ledger
    /// self-verifying without summing a million rows.
    /// </summary>
    public long RunningBalanceScaled { get; set; }

    /// <summary>A human-readable reason, required for adjustments and counts.</summary>
    public string? Reason { get; set; }

    public int? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The authoritative UTC posting timestamp.</summary>
    public DateTimeOffset PostedUtc { get; set; }

    /// <summary>The user-facing document date.</summary>
    public DateOnly BusinessDate { get; set; }
}
