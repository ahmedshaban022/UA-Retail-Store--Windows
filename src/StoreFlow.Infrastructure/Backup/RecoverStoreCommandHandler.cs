using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Entities;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// Puts a backup file back when the shop's own data cannot be read, so that the shop can be
/// started again.
/// </summary>
/// <remarks>
/// <para>This is the one way into the shop that does not need a signed-in administrator. It is
/// open only while the shop's database cannot be read: if the data can be read, the owner signs
/// in and uses the restore wizard, which is authorised and audited against a named person.</para>
/// <para>The rule is checked here, in the application layer, rather than by hiding a button. The
/// situation this exists for is one in which nobody can sign in, because there is no shop to sign
/// in to.</para>
/// </remarks>
public sealed class RecoverStoreCommandHandler(
    AppPaths paths,
    IBackupRestoreService restoreService,
    IAuditWriter auditWriter) : ICommandHandler<RecoverStoreCommand, Result<RestoreOutcome>>
{
    /// <inheritdoc />
    public async Task<Result<RestoreOutcome>> HandleAsync(
        RecoverStoreCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IsShopUnreadable())
        {
            return Result<RestoreOutcome>.Failure(
                BackupErrorCodes.RestoreFailed,
                "The shop's data can still be read, so sign in and use the restore wizard instead.");
        }

        RestoreOutcome outcome = await restoreService.RestoreAsync(
            command.ArchivePath,
            createRestorePoint: true,
            cancellationToken);

        // The audit entry stands on its own rather than joining the restore's transaction, so a
        // failure to write it must not swallow the answer the owner is waiting for. The shop's
        // database may still be the damaged one, and the trail is not worth crashing over.
#pragma warning disable CA1031 // The restore outcome matters more than the audit entry.
        try
        {
            await auditWriter.WriteAsync(
                new AuditEntry(
                    outcome.IsSucceeded ? BackupAudit.Restored : BackupAudit.RestoreRefused,
                    nameof(BackupLog),
                    command.ArchivePath,
                    null,
                    outcome.Message,
                    null),
                cancellationToken);
        }
        catch (Exception)
        {
            // The trail could not be written. The restore still happened, or still did not.
        }
#pragma warning restore CA1031

        return outcome.IsSucceeded
            ? Result<RestoreOutcome>.Success(outcome)
            : Result<RestoreOutcome>.Failure(BackupErrorCodes.RestoreFailed, outcome.Message);
    }

    /// <summary>
    /// True when the shop's database is gone or cannot be read.
    /// </summary>
    /// <remarks>
    /// The health check answers rather than throwing: a file that is not a SQLite database, or one
    /// so damaged that SQLite cannot read its header, is simply not healthy.
    /// </remarks>
    private bool IsShopUnreadable()
    {
        if (!File.Exists(paths.DatabasePath))
        {
            return true;
        }

        return !SqliteSnapshot.IsHealthy(paths.DatabasePath, out _);
    }
}
