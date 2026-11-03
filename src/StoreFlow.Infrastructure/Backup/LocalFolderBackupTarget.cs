using StoreFlow.Application.Abstractions;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// A backup target that is a folder on this computer or on removable media.
/// </summary>
/// <remarks>
/// The folder is chosen by the owner, so it can be a USB stick, a network share, or the shop's own
/// drive. It is the target that always works: it needs no account, no key and no network.
/// </remarks>
public sealed class LocalFolderBackupTarget : IBackupTarget
{
    /// <summary>The kind this target answers to.</summary>
    public const string KindName = "LocalFolder";

    /// <summary>Creates the target.</summary>
    /// <param name="directory">The folder the backups are written to.</param>
    public LocalFolderBackupTarget(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Folder = directory;
    }

    /// <inheritdoc />
    public string Kind => KindName;

    /// <inheritdoc />
    public string DisplayName => "Local folder";

    /// <summary>The folder the backups are written to.</summary>
    public string Folder { get; }

    /// <inheritdoc />
    public Task<BackupTargetStatus> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            System.IO.Directory.CreateDirectory(Folder);

            // The folder may exist and still be read-only, so the folder is written to and the
            // file is taken away again. That is the only honest way to know.
            string probe = Path.Combine(Folder, $"storeflow-probe-{Guid.NewGuid():N}.tmp");

            File.WriteAllText(probe, "storeflow");
            File.Delete(probe);
        }
        catch (IOException ex)
        {
            return Task.FromResult(new BackupTargetStatus(false, "The folder cannot be written to.", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(new BackupTargetStatus(false, "The folder cannot be written to.", ex.Message));
        }

        return Task.FromResult(new BackupTargetStatus(true, $"Ready. Backups are written to {Folder}."));
    }

    /// <inheritdoc />
    public async Task<BackupUploadResult> UploadAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        System.IO.Directory.CreateDirectory(Folder);

        string target = Path.Combine(Folder, Path.GetFileName(fileName));

        await using (FileStream file = new(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return new BackupUploadResult(target, target);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RemoteBackupFile>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!System.IO.Directory.Exists(Folder))
        {
            return Task.FromResult<IReadOnlyList<RemoteBackupFile>>([]);
        }

        var files = new List<RemoteBackupFile>();

        foreach (string path in System.IO.Directory.GetFiles(Folder, "*.sfb", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info = new(path);

            files.Add(new RemoteBackupFile(
                path,
                info.Name,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
        }

        files.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        return Task.FromResult<IReadOnlyList<RemoteBackupFile>>(files);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(string remoteId, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);
        ArgumentNullException.ThrowIfNull(destination);

        await using FileStream file = new(remoteId, FileMode.Open, FileAccess.Read, FileShare.Read);

        await file.CopyToAsync(destination, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);

        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(remoteId))
        {
            File.Delete(remoteId);
        }

        return Task.CompletedTask;
    }
}
