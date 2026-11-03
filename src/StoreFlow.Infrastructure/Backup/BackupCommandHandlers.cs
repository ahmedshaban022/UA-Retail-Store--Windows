using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;
using StoreFlow.Application.Pos;
using StoreFlow.Application.Settings;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// Takes a backup now, at the owner's request.
/// </summary>
public sealed class CreateBackupCommandHandler(
    IAuthorizationService authorization,
    BackupRunner runner,
    IUserSession session) : ICommandHandler<CreateBackupCommand, Result<BackupRunResult>>
{
    /// <inheritdoc />
    public async Task<Result<BackupRunResult>> HandleAsync(
        CreateBackupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.BackupCreate, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<BackupRunResult>.Failure(permitted.Error);
        }

        BackupRunResult outcome = await runner.RunAsync(
            command.TargetKind,
            command.Kind,
            session.Current?.UserId,
            cancellationToken);

        return outcome.IsSucceeded
            ? Result<BackupRunResult>.Success(outcome)
            : Result<BackupRunResult>.Failure(BackupErrorCodes.CaptureFailed, outcome.Message);
    }
}

/// <summary>
/// Lists the destinations the shop can write a backup to.
/// </summary>
public sealed class ListBackupTargetsQueryHandler(
    IAuthorizationService authorization,
    IBackupTargetFactory targetFactory,
    ICloudBackupConfigurationProvider cloudConfiguration) :
    IQueryHandler<ListBackupTargetsQuery, Result<IReadOnlyList<BackupTargetSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BackupTargetSummary>>> HandleAsync(
        ListBackupTargetsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.BackupCreate, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<BackupTargetSummary>>.Failure(permitted.Error);
        }

        var summaries = new List<BackupTargetSummary>();

        foreach (string kind in targetFactory.GetKnownKinds())
        {
            IBackupTarget target = await targetFactory.ResolveAsync(kind, cancellationToken);

            if (target is LocalFolderBackupTarget folder)
            {
                summaries.Add(new BackupTargetSummary(
                    target.Kind,
                    target.DisplayName,
                    true,
                    BackupTargetState.Ready,
                    folder.Folder));
                continue;
            }

            CloudBackupConfiguration? configuration = await cloudConfiguration.GetAsync(cancellationToken);

            summaries.Add(new BackupTargetSummary(
                target.Kind,
                target.DisplayName,
                configuration is not null,
                configuration is null
                    ? BackupTargetState.NotConfigured
                    : BackupTargetState.Ready,
                configuration is null
                    ? null
                    : $"{configuration.Bucket} at {configuration.Endpoint}"));
        }

        return Result<IReadOnlyList<BackupTargetSummary>>.Success(summaries);
    }
}

/// <summary>
/// Asks a destination whether it is reachable.
/// </summary>
public sealed class TestBackupTargetQueryHandler(
    IAuthorizationService authorization,
    IBackupTargetFactory targetFactory,
    ICloudBackupConfigurationProvider cloudConfiguration) :
    IQueryHandler<TestBackupTargetQuery, Result<BackupTargetTestResult>>
{
    /// <inheritdoc />
    public async Task<Result<BackupTargetTestResult>> HandleAsync(
        TestBackupTargetQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.BackupCreate, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<BackupTargetTestResult>.Failure(permitted.Error);
        }

        IBackupTarget target;

        try
        {
            target = await targetFactory.ResolveAsync(query.TargetKind, cancellationToken);
        }
        catch (ArgumentException)
        {
            return Result<BackupTargetTestResult>.Failure(
                BackupErrorCodes.TargetNotConfigured,
                "That backup target is not one this build knows.");
        }

        BackupTargetStatus status = await target.TestConnectionAsync(cancellationToken);

        if (!status.IsReachable)
        {
            // A destination that has not been set up is not a failure: it is a state the owner can
            // see and fix. The cloud target is the only one that can be in that state.
            CloudBackupConfiguration? configuration = await cloudConfiguration.GetAsync(cancellationToken);

            bool notConfigured = target is not LocalFolderBackupTarget && configuration is null;

            return Result<BackupTargetTestResult>.Success(new BackupTargetTestResult(
                target.Kind,
                target.DisplayName,
                false,
                notConfigured ? BackupTargetState.NotConfigured : BackupTargetState.Unreachable,
                status.Detail));
        }

        string? detail = target is LocalFolderBackupTarget folder
            ? folder.Folder
            : await DescribeCloudAsync(cancellationToken);

        return Result<BackupTargetTestResult>.Success(new BackupTargetTestResult(
            target.Kind,
            target.DisplayName,
            true,
            BackupTargetState.Ready,
            detail));
    }

    /// <summary>
    /// Says where the cloud copies would go, when the cloud target is set up.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bucket and the endpoint, or nothing when the target is not set up.</returns>
    private async Task<string?> DescribeCloudAsync(CancellationToken cancellationToken)
    {
        CloudBackupConfiguration? configuration = await cloudConfiguration.GetAsync(cancellationToken);

        return configuration is null
            ? null
            : $"{configuration.Bucket} at {configuration.Endpoint}";
    }
}

/// <summary>
/// Reads the backup log, newest first.
/// </summary>
public sealed class ListBackupHistoryQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) :
    IQueryHandler<ListBackupHistoryQuery, Result<IReadOnlyList<BackupHistoryEntry>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BackupHistoryEntry>>> HandleAsync(
        ListBackupHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.BackupCreate, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<BackupHistoryEntry>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<BackupLog> logs = context.BackupLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.TargetKind))
        {
            logs = logs.Where(log => log.TargetKind == query.TargetKind);
        }

        if (query.Result is BackupResult result)
        {
            logs = logs.Where(log => log.Result == result);
        }

        if (query.From is DateOnly from)
        {
            DateTimeOffset start = new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            logs = logs.Where(log => log.CreatedUtc >= start);
        }

        if (query.To is DateOnly to)
        {
            DateTimeOffset end = new(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

            logs = logs.Where(log => log.CreatedUtc <= end);
        }

        List<BackupHistoryEntry> entries = await logs
            // The log is append-only, so the identifier is the order the rows were written in.
            // SQLite cannot sort by a date-and-time column, and sorting by the identifier gives
            // the same order without asking the database to do something it cannot.
            .OrderByDescending(log => log.Id)
            .Select(log => new BackupHistoryEntry(
                log.Id,
                log.TargetKind,
                log.Path,
                log.CreatedUtc,
                log.Sha256,
                log.SizeBytes,
                log.Result,
                log.Message))
            .Take(Math.Clamp(query.Limit, 1, 1000))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<BackupHistoryEntry>>.Success(entries);
    }
}

/// <summary>
/// Lists the backup files that are on this computer, so that the owner can restore one of them.
/// </summary>
public sealed class ListBackupArtifactsQueryHandler(
    IAuthorizationService authorization,
    IBackupTargetFactory targetFactory) :
    IQueryHandler<ListBackupArtifactsQuery, Result<IReadOnlyList<BackupArtifactInfo>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BackupArtifactInfo>>> HandleAsync(
        ListBackupArtifactsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.BackupCreate, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<BackupArtifactInfo>>.Failure(permitted.Error);
        }

        IBackupTarget target = await targetFactory.ResolveAsync(
            LocalFolderBackupTarget.KindName,
            cancellationToken);

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

        return Result<IReadOnlyList<BackupArtifactInfo>>.Success(artifacts);
    }
}

/// <summary>
/// Reads a backup file and says what is inside it, and whether it may be restored.
/// </summary>
public sealed class InspectBackupQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) :
    IQueryHandler<InspectBackupQuery, Result<RestorePreview>>
{
    /// <inheritdoc />
    public async Task<Result<RestorePreview>> HandleAsync(
        InspectBackupQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.BackupCreate, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<RestorePreview>.Failure(permitted.Error);
        }

        if (!File.Exists(query.ArchivePath))
        {
            return Result<RestorePreview>.Failure(
                BackupErrorCodes.NotFound,
                "The backup file could not be found.");
        }

        BackupArchiveInspection inspection = BackupArchive.Inspect(query.ArchivePath);

        if (inspection.Manifest is null)
        {
            return Result<RestorePreview>.Failure(
                BackupErrorCodes.NotABackup,
                inspection.IntegrityMessage ?? "That file is not a StoreFlow backup.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        (IReadOnlyList<string> known, string current) =
            await BackupSchemaVersions.ReadAsync(context, cancellationToken);

        SchemaCompatibility compatibility = BackupSchemaCompatibility.Evaluate(
            inspection.Manifest.SchemaVersion,
            known,
            current);

        var preview = new RestorePreview(
            query.ArchivePath,
            Path.GetFileName(query.ArchivePath),
            inspection.Manifest.CreatedUtc,
            inspection.ArchiveSizeBytes,
            inspection.ArchiveSha256,
            inspection.Manifest.AppVersion,
            inspection.Manifest.SchemaVersion,
            inspection.Manifest.MachineName,
            inspection.IsIntegrityOk,
            inspection.IntegrityMessage,
            compatibility.IsCompatible,
            compatibility.Message);

        return Result<RestorePreview>.Success(preview);
    }
}

/// <summary>
/// Puts a backup file back where the shop reads it.
/// </summary>
/// <remarks>
/// The permission is checked here, in the application layer, before anything is read or written.
/// A hidden button is a convenience; a refused command is the control (PRD §3, §8.5).
/// </remarks>
public sealed class RestoreBackupCommandHandler(
    IAuthorizationService authorization,
    IBackupRestoreService restoreService,
    ISaleActivityMonitor saleMonitor,
    IAuditWriter auditWriter,
    IUserSession session) : ICommandHandler<RestoreBackupCommand, Result<RestoreOutcome>>
{
    /// <inheritdoc />
    public async Task<Result<RestoreOutcome>> HandleAsync(
        RestoreBackupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.BackupRestore, cancellationToken);

        if (permitted.IsFailure)
        {
            await auditWriter.WriteAsync(
                new AuditEntry(
                    BackupAudit.RestoreRefused,
                    nameof(BackupLog),
                    command.ArchivePath,
                    null,
                    permitted.Error.Message,
                    session.Current?.UserId),
                cancellationToken);

            return Result<RestoreOutcome>.Failure(permitted.Error);
        }

        if (saleMonitor.IsSaleInProgress)
        {
            const string message = "A sale is being posted right now, so the shop cannot be restored " +
                                   "until it is finished.";

            await auditWriter.WriteAsync(
                new AuditEntry(
                    BackupAudit.RestoreRefused,
                    nameof(BackupLog),
                    command.ArchivePath,
                    null,
                    message,
                    session.Current?.UserId),
                cancellationToken);

            return Result<RestoreOutcome>.Failure(BackupErrorCodes.RestoreBusy, message);
        }

        RestoreOutcome outcome = await restoreService.RestoreAsync(
            command.ArchivePath,
            command.CreateRestorePoint,
            cancellationToken);

        if (!outcome.IsSucceeded)
        {
            await auditWriter.WriteAsync(
                new AuditEntry(
                    BackupAudit.RestoreRefused,
                    nameof(BackupLog),
                    command.ArchivePath,
                    null,
                    outcome.Message,
                    session.Current?.UserId),
                cancellationToken);

            return Result<RestoreOutcome>.Failure(BackupErrorCodes.RestoreFailed, outcome.Message);
        }

        await auditWriter.WriteAsync(
            new AuditEntry(
                BackupAudit.Restored,
                nameof(BackupLog),
                command.ArchivePath,
                null,
                outcome.Message,
                session.Current?.UserId),
            cancellationToken);

        return Result<RestoreOutcome>.Success(outcome);
    }
}

/// <summary>
/// Reads the scheduler's settings.
/// </summary>
public sealed class GetBackupScheduleQueryHandler(
    IBackupScheduleSettingsStore scheduleStore,
    IStoreSettingsReader settingsReader,
    IAuthorizationService authorization) :
    IQueryHandler<GetBackupScheduleQuery, Result<BackupScheduleView>>
{
    /// <inheritdoc />
    public async Task<Result<BackupScheduleView>> HandleAsync(
        GetBackupScheduleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.BackupCreate, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<BackupScheduleView>.Failure(permitted.Error);
        }

        BackupScheduleSettings schedule = await scheduleStore.GetAsync(cancellationToken);
        StoreSettings settings = await settingsReader.GetAsync(cancellationToken);

        string directory = string.IsNullOrWhiteSpace(settings.BackupDirectory)
            ? string.Empty
            : settings.BackupDirectory;

        var view = new BackupScheduleView(
            schedule.TargetKind,
            schedule.DailyTimeMinutes,
            schedule.WeeklyDayOfWeek,
            schedule.WeeklyTimeMinutes,
            settings.DailyBackupEnabled,
            settings.WeeklyBackupEnabled,
            settings.DailyBackupRetention,
            settings.WeeklyBackupRetention,
            directory);

        return Result<BackupScheduleView>.Success(view);
    }
}

/// <summary>
/// Stores the scheduler's settings.
/// </summary>
public sealed class SaveBackupScheduleCommandHandler(
    IBackupScheduleSettingsStore scheduleStore,
    IStoreSettingsReader settingsReader,
    IStoreSettingsWriter settingsWriter,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IUserSession session) : ICommandHandler<SaveBackupScheduleCommand, Result>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        SaveBackupScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.SettingsManage, cancellationToken);

        if (permitted.IsFailure)
        {
            return permitted;
        }

        StoreSettings current = await settingsReader.GetAsync(cancellationToken);

        var settings = current with
        {
            BackupDirectory = string.IsNullOrWhiteSpace(command.BackupDirectory)
                ? null
                : command.BackupDirectory.Trim(),
            DailyBackupEnabled = command.DailyEnabled,
            WeeklyBackupEnabled = command.WeeklyEnabled,
            DailyBackupRetention = Math.Clamp(command.DailyRetention, 1, 365),
            WeeklyBackupRetention = Math.Clamp(command.WeeklyRetention, 1, 52),
        };

        await settingsWriter.SaveAsync(settings, cancellationToken);

        var schedule = new BackupScheduleSettings(
            command.TargetKind,
            Math.Clamp(command.DailyTimeMinutes, 0, 24 * 60 - 1),
            Math.Clamp(command.WeeklyDayOfWeek, 0, 6),
            Math.Clamp(command.WeeklyTimeMinutes, 0, 24 * 60 - 1));

        await scheduleStore.SaveAsync(schedule, cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                BackupAudit.ScheduleChanged,
                nameof(BackupLog),
                null,
                null,
                $"daily {schedule.DailyTimeMinutes} minutes, weekly {schedule.WeeklyDayOfWeek} at " +
                $"{schedule.WeeklyTimeMinutes} minutes, keeping {settings.DailyBackupRetention} daily and " +
                $"{settings.WeeklyBackupRetention} weekly",
                session.Current?.UserId),
            cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Runs the scheduled backups that are due.
/// </summary>
/// <remarks>
/// The scheduler is the shop's own work, not a person's, so it runs whether or not anybody is
/// signed in. The point of a daily backup is that the shop does not have to remember it.
/// </remarks>
public sealed class RunScheduledBackupsCommandHandler(
    BackupRunner runner,
    IBackupScheduleSettingsStore scheduleStore,
    IStoreSettingsReader settingsReader,
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IClock clock) : ICommandHandler<RunScheduledBackupsCommand, Result<int>>
{
    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(
        RunScheduledBackupsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BackupScheduleSettings schedule = await scheduleStore.GetAsync(cancellationToken);
        StoreSettings settings = await settingsReader.GetAsync(cancellationToken);

        BackupScheduleState state = await ReadStateAsync(cancellationToken);

        BackupScheduleDecision decision = BackupSchedulePolicy.Decide(
            schedule,
            settings.DailyBackupEnabled,
            settings.WeeklyBackupEnabled,
            clock.LocalNow,
            state);

        int taken = 0;

        if (decision.IsDailyDue)
        {
            await runner.RunAsync(schedule.TargetKind, BackupScheduleKind.Daily, null, cancellationToken);
            taken++;
        }

        if (decision.IsWeeklyDue)
        {
            await runner.RunAsync(schedule.TargetKind, BackupScheduleKind.Weekly, null, cancellationToken);
            taken++;
        }

        return Result<int>.Success(taken);
    }

    /// <summary>
    /// Reads the days the scheduled backups last ran, from the backup log.
    /// </summary>
    private async Task<BackupScheduleState> ReadStateAsync(CancellationToken cancellationToken)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<BackupLog> logs = await context.BackupLogs
            .AsNoTracking()
            .Where(log => log.Result == BackupResult.Succeeded && log.Path != null)
            .OrderByDescending(log => log.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        DateOnly? lastDaily = null;
        DateOnly? lastWeekly = null;

        foreach (BackupLog log in logs)
        {
            string name = Path.GetFileName(log.Path!.Replace('\\', '/'));

            BackupScheduleKind? kind = BackupFileNaming.Classify(name);

            if (kind == BackupScheduleKind.Daily)
            {
                lastDaily ??= DateOnly.FromDateTime(log.CreatedUtc.LocalDateTime);

                continue;
            }

            if (kind == BackupScheduleKind.Weekly)
            {
                lastWeekly ??= DateOnly.FromDateTime(log.CreatedUtc.LocalDateTime);
            }
        }

        return new BackupScheduleState(lastDaily, lastWeekly);
    }
}
