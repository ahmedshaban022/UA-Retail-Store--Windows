namespace StoreFlow.Domain.Entities;

/// <summary>
/// A cart the cashier suspended to serve somebody else, and intends to come back to (PRD POS-09).
/// </summary>
/// <remarks>
/// <para>A suspended cart is a note, not a movement. It reserves no stock and deducts no stock: the
/// goods are still on the shelf and still sellable to the next customer, and the cart is only worth
/// something once somebody actually pays for it.</para>
/// <para>The lines are stored as JSON because a suspended cart is a snapshot of the till, not a
/// posted document. It carries no number of its own in the accounting sense, and nothing downstream
/// reads it: when the cart is resumed it is turned into ordinary sale lines again.</para>
/// </remarks>
public sealed class HeldCart
{
    public int Id { get; set; }

    /// <summary>The cart's own number, for example <c>HLD-2026-0001</c>.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>The name the cashier gave the cart.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The JSON-encoded lines and the discount the cart was given.</summary>
    public string ContentJson { get; set; } = "{}";

    /// <summary>The cashier who suspended the cart.</summary>
    public int? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>When the cart was suspended.</summary>
    public DateTimeOffset HeldUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
