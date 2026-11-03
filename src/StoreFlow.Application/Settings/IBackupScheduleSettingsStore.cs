namespace StoreFlow.Application.Settings;

/// <summary>
/// Reads and writes the scheduler's own settings. They live in their own settings row, apart from
/// the shop's general settings, so that a shop trading under an older build keeps its settings
/// when the scheduler gains a new field.
/// </summary>
public interface IBackupScheduleSettingsStore
{
    /// <summary>The scheduler's settings, or the defaults when none have been stored.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The settings.</returns>
    Task<BackupScheduleSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the scheduler's settings.</summary>
    /// <param name="settings">The settings to store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task.</returns>
    Task SaveAsync(BackupScheduleSettings settings, CancellationToken cancellationToken = default);
}
