using StoreFlow.Application.Backup;

namespace StoreFlow.Application.Abstractions;

/// <summary>What a capture produced.</summary>
/// <param name="ArchivePath">Where the archive was written.</param>
/// <param name="SizeBytes">The size of the archive.</param>
/// <param name="Sha256">The SHA-256 of the archive.</param>
/// <param name="Manifest">The manifest the archive carries.</param>
public sealed record BackupCaptureResult(
    string ArchivePath,
    long SizeBytes,
    string Sha256,
    BackupManifest Manifest);

/// <summary>
/// Captures the live shop into a <c>.sfb</c> archive. The capture is safe: the database is copied
/// with SQLite's own online backup, and the copy is checked for damage before the archive is
/// written (PRD §8.4).
/// </summary>
public interface IBackupCaptureService
{
    /// <summary>
    /// Captures the store into an archive at the given path.
    /// </summary>
    /// <param name="archivePath">Where the archive is written.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the capture produced.</returns>
    Task<BackupCaptureResult> CaptureAsync(string archivePath, CancellationToken cancellationToken = default);
}
