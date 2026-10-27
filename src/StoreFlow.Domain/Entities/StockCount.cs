namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Enumerations;

/// <summary>
/// A stock count session. Counting never changes posted stock; the difference becomes an auditable
/// adjustment only after approval (PRD §4.5, INV-05).
/// </summary>
public sealed class StockCount
{
    public int Id { get; set; }

    /// <summary>Unique sequential count number.</summary>
    public string Number { get; set; } = string.Empty;

    public StockCountStatus Status { get; set; } = StockCountStatus.Draft;

    /// <summary>The user who started the count.</summary>
    public int? UserId { get; set; }

    public User? User { get; set; }

    public DateOnly BusinessDate { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? ApprovedUtc { get; set; }

    /// <summary>The authorised approver of the resulting adjustment.</summary>
    public int? ApprovedByUserId { get; set; }

    public User? ApprovedByUser { get; set; }

    /// <summary>The reason for the adjustment, required at approval.</summary>
    public string? Reason { get; set; }

    public ICollection<StockCountLine> Lines { get; } = new List<StockCountLine>();
}
