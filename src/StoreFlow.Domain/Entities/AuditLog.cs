namespace StoreFlow.Domain.Entities;

/// <summary>
/// An audit trail entry. Passwords, tokens and secrets are never written here (PRD §3, §9).
/// </summary>
public sealed class AuditLog
{
    public long Id { get; set; }

    /// <summary>The stable actor id. Display names are never used as audit identifiers.</summary>
    public int? ActorUserId { get; set; }

    public User? ActorUser { get; set; }

    /// <summary>The action performed, for example <c>product.update</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The entity type the action applied to.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The entity's identifier, as a string so any entity type fits.</summary>
    public string? EntityId { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>A hash of the previous state, so a change can be detected without storing the secret.</summary>
    public string? BeforeHash { get; set; }

    /// <summary>A short, non-sensitive summary of the new state.</summary>
    public string? AfterSummary { get; set; }
}
