using System.Text.Json;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Application.Settings;
using StoreFlow.Application.Setup;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Setup;

/// <summary>Reports whether the installation has been set up, and the store details it recorded.</summary>
public sealed class SetupStatusQueryHandler(IDbContextFactory<StoreFlowDbContext> contextFactory)
    : IQueryHandler<SetupStatusQuery, Result<SetupStatus>>
{
    /// <inheritdoc />
    public async Task<Result<SetupStatus>> HandleAsync(
        SetupStatusQuery query,
        CancellationToken cancellationToken)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        bool hasUsers = await context.Users.AnyAsync(cancellationToken);

        StoreDetails? details = null;

        Setting? setting = await context.Settings
            .FirstOrDefaultAsync(candidate => candidate.Key == DatabaseInitializer.StoreDetailsSettingKey, cancellationToken);

        if (setting is not null)
        {
            details = JsonSerializer.Deserialize<StoreDetails>(setting.Value);
        }

        return Result<SetupStatus>.Success(new SetupStatus(hasUsers, hasUsers, details));
    }
}
