using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Settings;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>The actions the backup flow writes to the audit trail.</summary>
public static class BackupAudit
{
    /// <summary>A backup was written and checked.</summary>
    public const string Created = "backup.created";

    /// <summary>A backup could not be written.</summary>
    public const string Failed = "backup.failed";

    /// <summary>A backup was put back.</summary>
    public const string Restored = "backup.restored";

    /// <summary>A restore was refused.</summary>
    public const string RestoreRefused = "backup.restore_refused";

    /// <summary>The scheduler's settings were changed.</summary>
    public const string ScheduleChanged = "backup.schedule_changed";

    /// <summary>A backup was taken before a migration that could change the data's shape.</summary>
    public const string PreMigration = "backup.pre_migration";
}

/// <summary>
/// Takes a backup, checks it, writes it to the target, and keeps the backup log.
/// </summary>
/// <remarks>
/// The work is in one place so that a backup taken by the owner, a backup taken by the scheduler
/// and a backup taken before a migration are the same backup with the same checks.
/// </remarks>
public sealed class BackupRunner(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IBackupCaptureService captureService,
    IBackupTargetFactory targetFactory,
    IStoreSettingsReader settingsReader,
    IAuditWriter auditWriter,
    IClock clock)
{
    /// <summary>
    /// Takes a backup and writes it to a target.
    /// </summary>
    /// <param name="targetKind">The kind of target to write to.</param>
    /// <param name="kind">Why the backup was taken.</param>
    /// <param name="actorUserId">Who asked for it, or null for the system's own backups.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the backup did.</returns>
    public async Task<BackupRunResult> RunAsync(
        string targetKind,
        BackupScheduleKind kind,
        int? actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKind);

        IBackupTarget target;

        try
        {
            target = await targetFactory.ResolveAsync(targetKind, cancellationToken);
        }
        catch (ArgumentException)
        {
            const string message = "That backup target is not one this build knows.";

            await WriteLogAsync(targetKind, null, null, 0, BackupResult.Failed, message, cancellationToken);

            return new BackupRunResult(false, targetKind, string.Empty, null, 0, message, BackupResult.Failed);
        }

        BackupTargetStatus status = await target.TestConnectionAsync(cancellationToken);

        if (!status.IsReachable)
        {
            await WriteLogAsync(target.Kind, null, null, 0, BackupResult.Failed, status.Message, cancellationToken);

            return new BackupRunResult(
                false,
                target.Kind,
                string.Empty,
                null,
                0,
                status.Message,
                BackupResult.Failed);
        }

        string fileName = BackupFileNaming.BuildName(kind, clock.UtcNow);
        string staging = Path.Combine(
            Path.GetTempPath(),
            $"storeflow-staging-{Guid.NewGuid():N}{BackupArchive.FileExtension}");

        try
        {
            BackupCaptureResult capture = await captureService.CaptureAsync(staging, cancellationToken);

            BackupUploadResult upload;

            await using (FileStream content = File.OpenRead(staging))
            {
                upload = await target.UploadAsync(fileName, content, cancellationToken);
            }

            string path = string.IsNullOrWhiteSpace(upload.Uri) ? upload.RemoteId : upload.Uri;

            string? problem = await VerifyAsync(target, upload.RemoteId, capture.Sha256, cancellationToken);

            if (problem is not null)
            {
                await WriteLogAsync(
                    target.Kind,
                    path,
                    capture.Sha256,
                    capture.SizeBytes,
                    BackupResult.Failed,
                    problem,
                    cancellationToken);

                await auditWriter.WriteAsync(
                    new AuditEntry(BackupAudit.Failed, nameof(BackupLog), path, null, problem, actorUserId),
                    cancellationToken);

                return new BackupRunResult(false, target.Kind, path, capture.Sha256, capture.SizeBytes, problem, BackupResult.Failed);
            }

            await WriteLogAsync(
                target.Kind,
                path,
                capture.Sha256,
                capture.SizeBytes,
                BackupResult.Succeeded,
                $"Backup written and verified: {fileName}",
                cancellationToken);

            await auditWriter.WriteAsync(
                new AuditEntry(
                    BackupAudit.Created,
                    nameof(BackupLog),
                    path,
                    null,
                    $"{fileName}, {capture.SizeBytes} bytes, sha256 {capture.Sha256}",
                    actorUserId),
                cancellationToken);

            await ApplyRetentionAsync(target, cancellationToken);

            return new BackupRunResult(
                true,
                target.Kind,
                path,
                capture.Sha256,
                capture.SizeBytes,
                $"The backup was written and checked: {fileName}",
                BackupResult.Succeeded);
        }
        catch (BackupCaptureException ex)
        {
            await WriteLogAsync(target.Kind, null, null, 0, BackupResult.Failed, ex.Message, cancellationToken);

            await auditWriter.WriteAsync(
                new AuditEntry(BackupAudit.Failed, nameof(BackupLog), fileName, null, ex.Message, actorUserId),
                cancellationToken);

            return new BackupRunResult(false, target.Kind, string.Empty, null, 0, ex.Message, BackupResult.Failed);
        }
        catch (IOException ex)
        {
            const string message = "The backup could not be written.";

            await WriteLogAsync(target.Kind, null, null, 0, BackupResult.Failed, message, cancellationToken);

            return new BackupRunResult(false, target.Kind, string.Empty, null, 0, $"{message} {ex.Message}", BackupResult.Failed);
        }
        catch (UnauthorizedAccessException ex)
        {
            const string message = "The backup folder could not be written to.";

            await WriteLogAsync(target.Kind, null, null, 0, BackupResult.Failed, message, cancellationToken);

            return new BackupRunResult(false, target.Kind, string.Empty, null, 0, $"{message} {ex.Message}", BackupResult.Failed);
        }
        finally
        {
            if (File.Exists(staging))
            {
                File.Delete(staging);
            }
        }
    }

    /// <summary>
    /// Reads the backup back from the target and checks that it is the same file that was written.
    /// A target that quietly kept a different file is a failed backup, not a successful one.
    /// </summary>
    private static async Task<string?> VerifyAsync(
        IBackupTarget target,
        string remoteId,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        string verification = Path.Combine(
            Path.GetTempPath(),
            $"storeflow-verify-{Guid.NewGuid():N}{BackupArchive.FileExtension}");

        try
        {
            await using (FileStream file = new(verification, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await target.DownloadAsync(remoteId, file, cancellationToken);
            }

            string actual = BackupArchive.ComputeFileSha256(verification);

            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase)
                ? null
                : "The backup that reached the target is not the backup that was written, so it was not accepted.";
        }
        catch (IOException ex)
        {
            return $"The backup could not be read back from the target to check it. {ex.Message}";
        }
        catch (Amazon.S3.AmazonS3Exception ex)
        {
            return $"The backup could not be read back from the target to check it. {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"The backup could not be read back from the target to check it. {ex.Message}";
        }
        finally
        {
            if (File.Exists(verification))
            {
                File.Delete(verification);
            }
        }
    }

    /// <summary>
    /// Removes the copies that have grown past the number the shop keeps. The newest copy is never
    /// removed, and copies the owner took by hand are never removed.
    /// </summary>
    private async Task ApplyRetentionAsync(IBackupTarget target, CancellationToken cancellationToken)
    {
        StoreSettings settings = await settingsReader.GetAsync(cancellationToken);

        IReadOnlyList<RemoteBackupFile> files = await target.ListAsync(cancellationToken);

        var artifacts = new List<BackupArtifactInfo>(files.Count);

        foreach (RemoteBackupFile file in files)
        {
            artifacts.Add(new BackupArtifactInfo(
                file.RemoteId,
                file.Name,
                file.SizeBytes,
                file.CreatedUtc,
                BackupFileNaming.Classify(file.Name) ?? BackupScheduleKind.Manual));
        }

        IReadOnlyList<BackupArtifactInfo> expired = BackupRetentionPolicy.SelectExpired(
            artifacts,
            settings.DailyBackupRetention,
            settings.WeeklyBackupRetention);

        foreach (BackupArtifactInfo artifact in expired)
        {
            try
            {
                await target.DeleteAsync(artifact.Path, cancellationToken);
            }
            catch (IOException)
            {
                // A copy that will not delete is left where it is; it costs disk, not correctness.
            }
            catch (UnauthorizedAccessException)
            {
                // The same: a copy that cannot be deleted is left alone.
            }
        }
    }

    private async Task WriteLogAsync(
        string targetKind,
        string? path,
        string? sha256,
        long sizeBytes,
        BackupResult result,
        string message,
        CancellationToken cancellationToken)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.BackupLogs.Add(new BackupLog
        {
            TargetKind = targetKind.Length > 60 ? targetKind[..60] : targetKind,
            Path = path is not null && path.Length > 1000 ? path[..1000] : path,
            CreatedUtc = clock.UtcNow,
            Sha256 = sha256,
            SizeBytes = sizeBytes,
            Result = result,
            Message = message.Length > 2000 ? message[..2000] : message,
        });

        await context.BeforeWriteAsync(WritePoint.WriteBackupLog, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}
