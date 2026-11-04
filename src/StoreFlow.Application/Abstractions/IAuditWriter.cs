namespace StoreFlow.Application.Abstractions;

/// <summary>An entry to append to the audit trail.</summary>
/// <param name="Action">The action performed, for example <c>product.update</c>.</param>
/// <param name="EntityType">The entity type the action applied to.</param>
/// <param name="EntityId">The entity identifier, as a string.</param>
/// <param name="BeforeHash">A hash of the previous state, when one was captured.</param>
/// <param name="AfterSummary">A short, non-sensitive summary of the new state.</param>
/// <param name="ActorUserId">The actor; null for system actions.</param>
public sealed record AuditEntry(
    string Action,
    string EntityType,
    string? EntityId = null,
    string? BeforeHash = null,
    string? AfterSummary = null,
    int? ActorUserId = null);

/// <summary>
/// Appends to the audit trail. Passwords, tokens and other secrets are never written through this
/// port (PRD §3).
/// </summary>
public interface IAuditWriter
{
    /// <summary>Appends an audit entry.</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Adds an audit entry to a write that is already open, so the entry is saved with it.</summary>
    /// <param name="trail">The open write the entry belongs to.</param>
    /// <param name="entry">The entry to add.</param>
    void AddTo(IAuditTrail trail, AuditEntry entry);
}
