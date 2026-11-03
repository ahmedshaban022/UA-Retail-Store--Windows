using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Settings;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// Reads and writes the settings that belong to the backup screen: the shop's behavioural
/// settings, the scheduler's own settings, and the cloud target's address.
/// </summary>
public sealed class BackupSettingsStore(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IClock clock) :
    IBackupScheduleSettingsStore,
    IStoreSettingsWriter
{
    /// <summary>The settings key that holds the scheduler's settings.</summary>
    public const string BackupScheduleSettingKey = "store.backup.schedule";

    /// <summary>The settings key that holds the cloud target's address.</summary>
    public const string CloudTargetSettingKey = "store.backup.cloud";

    /// <summary>The provider the cloud secret is kept under in the token vault.</summary>
    public const string CloudSecretProvider = "S3";

    /// <summary>The key the cloud secret is kept under in the token vault.</summary>
    public const string CloudSecretKey = "secret";

    /// <summary>
    /// The names these records are written and read under.
    /// </summary>
    /// <remarks>
    /// The records are written with their own property names, not with camel-cased ones. A record
    /// whose values come from a constructor is matched to the JSON by the name of its parameter,
    /// so a renamed property would simply not be found and the record would come back empty.
    /// </remarks>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <inheritdoc />
    public async Task<BackupScheduleSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Setting? setting = await context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Key == BackupScheduleSettingKey, cancellationToken);

        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
        {
            return BackupScheduleSettings.Defaults;
        }

        try
        {
            return JsonSerializer.Deserialize<BackupScheduleSettings>(setting.Value)
                ?? BackupScheduleSettings.Defaults;
        }
        catch (JsonException)
        {
            return BackupScheduleSettings.Defaults;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(BackupScheduleSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await UpsertAsync(context, BackupScheduleSettingKey, JsonSerializer.Serialize(settings, Json), WritePoint.SaveBackupSchedule, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(StoreSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await UpsertAsync(
            context,
            DatabaseInitializer.StoreSettingsSettingKey,
            JsonSerializer.Serialize(settings, Json),
            WritePoint.SaveStoreSettings,
            cancellationToken);
    }

    /// <summary>
    /// The cloud target's address, as the shop set it up.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The address, or null when the cloud target has not been set up.</returns>
    public async Task<CloudTargetAddress?> GetCloudAddressAsync(CancellationToken cancellationToken = default)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Setting? setting = await context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Key == CloudTargetSettingKey, cancellationToken);

        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CloudTargetAddress>(setting.Value, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stores the cloud target's address. The secret is never stored here: it goes to the token
    /// vault.
    /// </summary>
    /// <param name="address">The address to store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task.</returns>
    public async Task SaveCloudAddressAsync(CloudTargetAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await UpsertAsync(
            context,
            CloudTargetSettingKey,
            JsonSerializer.Serialize(address, Json),
            WritePoint.SaveCloudAddress,
            cancellationToken);
    }

    private async Task UpsertAsync(
        StoreFlowDbContext context,
        string key,
        string value,
        WritePoint point,
        CancellationToken cancellationToken)
    {
        Setting? existing = await context.Settings
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken);

        if (existing is null)
        {
            context.Settings.Add(new Setting
            {
                Key = key,
                Value = value,
                UpdatedUtc = clock.UtcNow,
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedUtc = clock.UtcNow;
        }

        await context.BeforeWriteAsync(point, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Where the cloud target lives, without the secret.
/// </summary>
/// <param name="Endpoint">The service's address.</param>
/// <param name="Bucket">The bucket the backups are written to.</param>
/// <param name="Region">The region the bucket lives in.</param>
/// <param name="Prefix">The path inside the bucket.</param>
/// <param name="AccessKeyId">The key the shop signs its requests with.</param>
public sealed record CloudTargetAddress(
    string Endpoint,
    string Bucket,
    string Region,
    string Prefix,
    string AccessKeyId);
