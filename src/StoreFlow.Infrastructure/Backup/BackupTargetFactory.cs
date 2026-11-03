using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Settings;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// Builds the backup target a piece of work names.
/// </summary>
/// <remarks>
/// This is the only place that knows which destinations exist. Adding Google Drive, OneDrive,
/// Dropbox or a WebDAV server later means writing one more implementation of the port and one
/// more line here; the backup engine does not change.
/// </remarks>
public sealed class BackupTargetFactory(
    AppPaths paths,
    IStoreSettingsReader settingsReader,
    ICloudBackupConfigurationProvider cloudConfiguration) : IBackupTargetFactory
{
    private readonly object _gate = new();
    private S3BackupTarget? _cloudTarget;

    /// <inheritdoc />
    public IReadOnlyList<string> GetKnownKinds() =>
    [
        LocalFolderBackupTarget.KindName,
        S3BackupTarget.KindName,
    ];

    /// <inheritdoc />
    public async Task<IBackupTarget> ResolveAsync(string kind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        if (string.Equals(kind, LocalFolderBackupTarget.KindName, StringComparison.OrdinalIgnoreCase))
        {
            StoreSettings settings = await settingsReader.GetAsync(cancellationToken);

            string directory = string.IsNullOrWhiteSpace(settings.BackupDirectory)
                ? paths.BackupsDirectory
                : settings.BackupDirectory;

            return new LocalFolderBackupTarget(directory);
        }

        if (string.Equals(kind, S3BackupTarget.KindName, StringComparison.OrdinalIgnoreCase))
        {
            return GetCloudTarget();
        }

        throw new ArgumentException($"'{kind}' is not a backup target this build knows.", nameof(kind));
    }

    private IBackupTarget GetCloudTarget()
    {
        lock (_gate)
        {
            _cloudTarget ??= new S3BackupTarget(cloudConfiguration);

            return _cloudTarget;
        }
    }
}
