namespace StoreFlow.Application.Abstractions;

/// <summary>A file that already exists on a backup target.</summary>
/// <param name="RemoteId">The provider's identifier for the file.</param>
/// <param name="Name">The file name.</param>
/// <param name="SizeBytes">The file size.</param>
/// <param name="CreatedUtc">When the file was written.</param>
public sealed record RemoteBackupFile(string RemoteId, string Name, long SizeBytes, DateTimeOffset CreatedUtc);

/// <summary>The result of uploading a backup artifact.</summary>
/// <param name="RemoteId">The provider's identifier for the uploaded file.</param>
/// <param name="Uri">A human-readable location, when the provider exposes one.</param>
public sealed record BackupUploadResult(string RemoteId, string? Uri);

/// <summary>The health of a configured backup target.</summary>
/// <param name="IsReachable">True when the target answered.</param>
/// <param name="Message">A short description of the outcome, suitable for a status line.</param>
/// <param name="Detail">Optional longer detail, for example the error text.</param>
public sealed record BackupTargetStatus(bool IsReachable, string Message, string? Detail = null);

/// <summary>
/// A place backups can be written to. Implementations exist for local folders and USB media,
/// Google Drive, OneDrive, Dropbox, S3-compatible object stores, WebDAV servers and generic REST
/// endpoints.
/// </summary>
public interface IBackupTarget
{
    /// <summary>A stable identifier for the target kind, for example <c>GoogleDrive</c>.</summary>
    string Kind { get; }

    /// <summary>The name shown in the interface.</summary>
    string DisplayName { get; }

    /// <summary>Checks whether the target is reachable and correctly configured.</summary>
    Task<BackupTargetStatus> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Uploads a backup artifact.</summary>
    Task<BackupUploadResult> UploadAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the artifacts already stored on the target.</summary>
    Task<IReadOnlyList<RemoteBackupFile>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads an artifact from the target.</summary>
    Task DownloadAsync(string remoteId, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>Deletes an artifact from the target.</summary>
    Task DeleteAsync(string remoteId, CancellationToken cancellationToken = default);
}
