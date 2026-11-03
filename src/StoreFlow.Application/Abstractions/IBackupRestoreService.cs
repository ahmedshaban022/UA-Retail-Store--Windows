using StoreFlow.Application.Backup;

namespace StoreFlow.Application.Abstractions;

/// <summary>
/// Puts a backup archive back where the application reads it. The implementation checks the
/// archive, keeps a restore point of what it replaces, and replaces the shop's data only when it
/// is safe to do so (PRD §8.5).
/// </summary>
public interface IBackupRestoreService
{
    /// <summary>
    /// Restores an archive over the live shop.
    /// </summary>
    /// <param name="archivePath">The archive to restore.</param>
    /// <param name="createRestorePoint">
    /// True to keep a copy of the database that is being replaced, so that the restore can be
    /// undone.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the restore did.</returns>
    Task<RestoreOutcome> RestoreAsync(
        string archivePath,
        bool createRestorePoint,
        CancellationToken cancellationToken = default);
}
