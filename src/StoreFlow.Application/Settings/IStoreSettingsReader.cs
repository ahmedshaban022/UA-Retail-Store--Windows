namespace StoreFlow.Application.Settings;

/// <summary>
/// Reads the store's behavioural settings. The till needs them — the discount limit and the scan
/// suffix are policies the shopkeeper sets — and they live in the database, so reading them is a
/// port rather than a constant.
/// </summary>
public interface IStoreSettingsReader
{
    /// <summary>The settings the shop is trading by, or the defaults when none have been stored.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The settings.</returns>
    Task<StoreSettings> GetAsync(CancellationToken cancellationToken = default);
}
