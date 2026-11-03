using StoreFlow.Application.Abstractions;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// Reads the cloud target's configuration: the address from the settings table, the secret from
/// the token vault, where it is encrypted with the Windows Data Protection API.
/// </summary>
public sealed class CloudBackupConfigurationProvider(
    BackupSettingsStore settingsStore,
    ITokenVault tokenVault) : ICloudBackupConfigurationProvider
{
    /// <inheritdoc />
    public async Task<CloudBackupConfiguration?> GetAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(tokenVault);

        CloudTargetAddress? address = await settingsStore.GetCloudAddressAsync(cancellationToken);

        if (address is null ||
            string.IsNullOrWhiteSpace(address.Endpoint) ||
            string.IsNullOrWhiteSpace(address.Bucket) ||
            string.IsNullOrWhiteSpace(address.AccessKeyId))
        {
            return null;
        }

        string? secret = await tokenVault.ReadAsync(
            BackupSettingsStore.CloudSecretProvider,
            BackupSettingsStore.CloudSecretKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        return new CloudBackupConfiguration(
            address.Endpoint,
            address.Bucket,
            address.Region ?? string.Empty,
            address.Prefix ?? string.Empty,
            address.AccessKeyId,
            secret);
    }
}
