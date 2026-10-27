using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Catalog;

/// <summary>
/// Reads the audit trail. The trail is append-only and read-only here: nothing in the application
/// edits or removes an entry.
/// </summary>
public sealed class ListAuditEntriesQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryView>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AuditEntryView>>> HandleAsync(
        ListAuditEntriesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.AuditView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<AuditEntryView>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<AuditLog> logs = context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            logs = logs.Where(entry => entry.EntityType == query.EntityType);
        }

        string? search = query.Search?.Trim();

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = SearchProductsQueryHandler.LikePattern(search);

            logs = logs.Where(entry =>
                EF.Functions.Like(entry.Action, pattern, SearchProductsQueryHandler.LikeEscapeCharacter)
                || (entry.AfterSummary != null
                    && EF.Functions.Like(entry.AfterSummary, pattern, SearchProductsQueryHandler.LikeEscapeCharacter))
                || (entry.EntityId != null
                    && EF.Functions.Like(entry.EntityId, pattern, SearchProductsQueryHandler.LikeEscapeCharacter)));
        }

        List<AuditEntryView> result = await logs
            // The trail is append-only, so the identifier is the order the entries were written in.
            // SQLite cannot sort by a date-and-time column, and sorting by the identifier gives the
            // same order without asking the database to do something it cannot.
            .OrderByDescending(entry => entry.Id)
            .Select(entry => new AuditEntryView(
                entry.Id,
                entry.TimestampUtc,
                entry.ActorUser == null ? string.Empty : entry.ActorUser.DisplayName,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.AfterSummary))
            .Take(Math.Clamp(query.Take, 1, 1000))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AuditEntryView>>.Success(result);
    }
}

/// <summary>
/// Reads the audit trail of one product, so the owner can see what happened to it without leaving
/// the editor.
/// </summary>
public sealed class GetProductAuditQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<GetProductAuditQuery, Result<IReadOnlyList<AuditEntryView>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AuditEntryView>>> HandleAsync(
        GetProductAuditQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.CatalogView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<AuditEntryView>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        string entityId = query.ProductId.ToString();

        List<AuditEntryView> entries = await context.AuditLogs
            .AsNoTracking()
            .Where(entry => entry.EntityType == nameof(Domain.Entities.Product) && entry.EntityId == entityId)
            .OrderByDescending(entry => entry.Id)
            .Select(entry => new AuditEntryView(
                entry.Id,
                entry.TimestampUtc,
                entry.ActorUser == null ? string.Empty : entry.ActorUser.DisplayName,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.AfterSummary))
            .Take(Math.Clamp(query.Take, 1, 200))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AuditEntryView>>.Success(entries);
    }
}
