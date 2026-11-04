namespace StoreFlow.Application.Abstractions;

/// <summary>
/// A write that is already open, and that an audit entry can be added to.
///
/// An entry added here is saved together with everything else in the same write, so the entry
/// and the change it describes are made permanent together or not at all. A command that changes
/// the shop's data and then writes its audit entry afterwards leaves a window in which the change
/// is permanent and the trail is empty, and a shop that finds that window has no answer to "who
/// did this".
/// </summary>
public interface IAuditTrail
{
    /// <summary>Adds the entry to the open write.</summary>
    /// <param name="entry">The entry to add.</param>
    /// <param name="actorUserId">The actor, or null for a system action.</param>
    /// <param name="timestampUtc">When the action happened.</param>
    void Append(AuditEntry entry, int? actorUserId, DateTimeOffset timestampUtc);
}
