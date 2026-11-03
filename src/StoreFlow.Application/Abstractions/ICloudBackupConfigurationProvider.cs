namespace StoreFlow.Application.Abstractions;

/// <summary>
/// Where a cloud backup target lives and how to talk to it.
/// </summary>
/// <param name="Endpoint">The service's address, for example <c>https://s3.example.com</c>.</param>
/// <param name="Bucket">The bucket, or container, the backups are written to.</param>
/// <param name="Region">The region the bucket lives in.</param>
/// <param name="Prefix">The path inside the bucket that the backups are written to.</param>
/// <param name="AccessKeyId">The key the shop signs its requests with.</param>
/// <param name="SecretAccessKey">The secret that goes with the key. It is never written to the database or to a log.</param>
public sealed record CloudBackupConfiguration(
    string Endpoint,
    string Bucket,
    string Region,
    string Prefix,
    string AccessKeyId,
    string SecretAccessKey);

/// <summary>
/// Reads the cloud target's configuration. The secret is kept in the token vault, encrypted with
/// the Windows Data Protection API, and never stored in the database.
/// </summary>
public interface ICloudBackupConfigurationProvider
{
    /// <summary>
    /// The configuration, or null when the shop has not set the cloud target up.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The configuration.</returns>
    Task<CloudBackupConfiguration?> GetAsync(CancellationToken cancellationToken = default);
}
