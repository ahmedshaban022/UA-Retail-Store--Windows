using StoreFlow.Application.Abstractions;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Services;

/// <summary>
/// Writes to the audit trail.
///
/// An entry can be written on its own, or added to the write that is already open. A command that
/// changes the shop's data adds its entry to its own write, so the entry and the change it
/// describes are made permanent together or not at all. An entry that is written on its own
/// stands for something that did NOT change the shop's data — a refused sign-in, a refused
/// restore, a file that was exported — and has nothing to be atomic with.
/// </summary>
public sealed class EfAuditWriter(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IClock clock,
    IUserSession session) : IAuditWriter
{
    /// <inheritdoc />
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        AddEntry(context, entry, entry.ActorUserId ?? session.Current?.UserId, clock.UtcNow);

        await context.BeforeWriteAsync(WritePoint.WriteAuditEntry, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void AddTo(IAuditTrail trail, AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(trail);
        ArgumentNullException.ThrowIfNull(entry);

        AddEntry(trail, entry, entry.ActorUserId ?? session.Current?.UserId, clock.UtcNow);
    }

    /// <summary>
    /// Puts the entry on the trail. The trail is the write that is already open, so the entry is
    /// saved with everything else in that write.
    /// </summary>
    internal static void AddEntry(IAuditTrail trail, AuditEntry entry, int? actorUserId, DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(trail);
        ArgumentNullException.ThrowIfNull(entry);

        trail.Append(entry, actorUserId, timestampUtc);
    }
}
