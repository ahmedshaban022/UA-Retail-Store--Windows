using StoreFlow.Application.Abstractions;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Services;

/// <summary>
/// Writes to the audit trail. It uses its own short-lived context so an audit entry never joins —
/// or accidentally rolls back with — the business transaction that produced it.
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

        context.AuditLogs.Add(new AuditLog
        {
            ActorUserId = entry.ActorUserId ?? session.Current?.UserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            BeforeHash = entry.BeforeHash,
            AfterSummary = entry.AfterSummary,
            TimestampUtc = clock.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);
    }
}
