using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Settings;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Settings;

/// <summary>
/// Reads the store's behavioural settings from the settings table. A shop that has never saved
/// them is trading by the defaults, which is what a fresh installation does.
/// </summary>
public sealed class StoreSettingsReader(IDbContextFactory<StoreFlowDbContext> contextFactory)
    : IStoreSettingsReader
{
    /// <inheritdoc />
    public async Task<StoreSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Setting? setting = await context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Key == DatabaseInitializer.StoreSettingsSettingKey, cancellationToken);

        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
        {
            return StoreSettings.Defaults;
        }

        try
        {
            return JsonSerializer.Deserialize<StoreSettings>(setting.Value) ?? StoreSettings.Defaults;
        }
        catch (JsonException)
        {
            // A settings row that cannot be read is not a reason to stop the shop from opening the
            // till. The defaults are the safe answer: a discount limit of ten per cent is a limit.
            return StoreSettings.Defaults;
        }
    }
}
