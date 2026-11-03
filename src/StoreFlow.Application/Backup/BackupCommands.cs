using StoreFlow.Application.Common;
using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Application.Backup;

/// <summary>
/// Takes a backup now, and writes it to the named target.
/// </summary>
/// <param name="TargetKind">The kind of target to write to, for example <c>LocalFolder</c>.</param>
/// <param name="Kind">Why the backup was taken. A backup the owner asked for is a manual one and is never removed by the scheduler.</param>
public sealed record CreateBackupCommand(string TargetKind, BackupScheduleKind Kind = BackupScheduleKind.Manual)
    : ICommand<Result<BackupRunResult>>;

/// <summary>Lists the targets the shop can write to.</summary>
public sealed record ListBackupTargetsQuery : IQuery<Result<IReadOnlyList<BackupTargetSummary>>>;

/// <summary>Asks a target whether it is reachable.</summary>
/// <param name="TargetKind">The kind of target to test.</param>
public sealed record TestBackupTargetQuery(string TargetKind) : IQuery<Result<BackupTargetTestResult>>;

/// <summary>
/// Reads the backup log, newest first. The log is the PRD's <c>BackupLogs</c> row made visible.
/// </summary>
/// <param name="TargetKind">The kind of target to narrow the log to.</param>
/// <param name="Result">The outcome to narrow the log to.</param>
/// <param name="From">The first day to show.</param>
/// <param name="To">The last day to show.</param>
/// <param name="Limit">How many rows to read at most.</param>
public sealed record ListBackupHistoryQuery(
    string? TargetKind = null,
    BackupResult? Result = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Limit = 500) : IQuery<Result<IReadOnlyList<BackupHistoryEntry>>>;

/// <summary>Lists the backup files that are on this computer.</summary>
public sealed record ListBackupArtifactsQuery : IQuery<Result<IReadOnlyList<BackupArtifactInfo>>>;

/// <summary>
/// Reads a backup file and says what is inside it, and whether it may be restored.
/// </summary>
/// <param name="ArchivePath">The full path of the backup file.</param>
public sealed record InspectBackupQuery(string ArchivePath) : IQuery<Result<RestorePreview>>;

/// <summary>
/// Puts a backup file back where the shop reads it.
/// </summary>
/// <param name="ArchivePath">The full path of the backup file.</param>
/// <param name="CreateRestorePoint">
/// True to keep the database that is being replaced, so that the restore can be undone.
/// </param>
public sealed record RestoreBackupCommand(string ArchivePath, bool CreateRestorePoint = true)
    : ICommand<Result<RestoreOutcome>>;

/// <summary>
/// Puts a backup file back when the shop's own data cannot be read, so that the shop can be
/// started again.
/// </summary>
/// <remarks>
/// This is the one way into the shop that does not need a signed-in administrator, and it is only
/// open when the shop's data cannot be read at all. The situation it exists for is one in which
/// nobody can sign in, because there is no shop to sign in to.
/// </remarks>
/// <param name="ArchivePath">The full path of the backup file.</param>
public sealed record RecoverStoreCommand(string ArchivePath) : ICommand<Result<RestoreOutcome>>;

/// <summary>Reads the scheduler's settings.</summary>
public sealed record GetBackupScheduleQuery : IQuery<Result<BackupScheduleView>>;

/// <summary>
/// Stores the scheduler's settings.
/// </summary>
/// <param name="TargetKind">The kind of target the scheduled backups are written to.</param>
/// <param name="DailyTimeMinutes">The time of the daily backup, in minutes after midnight, local time.</param>
/// <param name="WeeklyDayOfWeek">The day of the weekly backup, where 0 is Sunday.</param>
/// <param name="WeeklyTimeMinutes">The time of the weekly backup, in minutes after midnight, local time.</param>
/// <param name="DailyEnabled">True when the daily backup runs.</param>
/// <param name="WeeklyEnabled">True when the weekly backup runs.</param>
/// <param name="DailyRetention">How many daily backups to keep.</param>
/// <param name="WeeklyRetention">How many weekly backups to keep.</param>
/// <param name="BackupDirectory">Where local backups are written.</param>
public sealed record SaveBackupScheduleCommand(
    string TargetKind,
    int DailyTimeMinutes,
    int WeeklyDayOfWeek,
    int WeeklyTimeMinutes,
    bool DailyEnabled,
    bool WeeklyEnabled,
    int DailyRetention,
    int WeeklyRetention,
    string BackupDirectory) : ICommand<Result>;

/// <summary>
/// Runs the scheduled backups that are due. The scheduler calls this when the shop starts and
/// again whenever the day turns over.
/// </summary>
public sealed record RunScheduledBackupsCommand : ICommand<Result<int>>;
