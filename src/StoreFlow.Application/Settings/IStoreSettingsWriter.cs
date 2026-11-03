namespace StoreFlow.Application.Settings;

/// <summary>
/// Writes the shop's behavioural settings. The reader that already exists is kept as it is; this is
/// the other half of it.
/// </summary>
public interface IStoreSettingsWriter
{
    /// <summary>Stores the shop's behavioural settings.</summary>
    /// <param name="settings">The settings to store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task.</returns>
    Task SaveAsync(StoreSettings settings, CancellationToken cancellationToken = default);
}
