using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Application.Backup;

/// <summary>The state a backup destination is in.</summary>
/// <remarks>
/// The state is carried as a code rather than as a sentence, because the sentence is the
/// interface's to write: a shop running in Arabic must not be told that its destination is
/// "ready" in English.
/// </remarks>
public enum BackupTargetState
{
    /// <summary>The destination is ready to be used.</summary>
    Ready = 0,

    /// <summary>The destination has not been set up.</summary>
    NotConfigured = 1,

    /// <summary>The destination could not be reached.</summary>
    Unreachable = 2,
}

/// <summary>A place backups can be written to, as the backup screen shows it.</summary>
/// <param name="Kind">The target's identifier, for example <c>LocalFolder</c>.</param>
/// <param name="DisplayName">The name shown to the owner.</param>
/// <param name="IsConfigured">True when the target has everything it needs to be used.</param>
/// <param name="State">The state the target is in.</param>
/// <param name="Detail">Where the copies go, or the error that stopped the target answering.</param>
public sealed record BackupTargetSummary(
    string Kind,
    string DisplayName,
    bool IsConfigured,
    BackupTargetState State,
    string? Detail);

/// <summary>The result of asking a target whether it is reachable.</summary>
/// <param name="Kind">The target's identifier.</param>
/// <param name="DisplayName">The name shown to the owner.</param>
/// <param name="IsReachable">True when the target answered.</param>
/// <param name="State">The state the target turned out to be in.</param>
/// <param name="Detail">Where the copies would go, or the error that stopped the target answering.</param>
public sealed record BackupTargetTestResult(
    string Kind,
    string DisplayName,
    bool IsReachable,
    BackupTargetState State,
    string? Detail);

/// <summary>One row of the backup log.</summary>
/// <param name="Id">The row's identifier.</param>
/// <param name="TargetKind">The kind of target the backup was written to.</param>
/// <param name="Path">Where the backup was written.</param>
/// <param name="CreatedUtc">When the attempt was made.</param>
/// <param name="Sha256">The SHA-256 of the produced file.</param>
/// <param name="SizeBytes">The size of the produced file.</param>
/// <param name="Result">Whether the attempt worked.</param>
/// <param name="Message">What happened.</param>
public sealed record BackupHistoryEntry(
    long Id,
    string TargetKind,
    string? Path,
    DateTimeOffset CreatedUtc,
    string? Sha256,
    long SizeBytes,
    BackupResult Result,
    string? Message);

/// <summary>A backup file found on disk.</summary>
/// <param name="Path">The full path of the file.</param>
/// <param name="FileName">The name of the file.</param>
/// <param name="SizeBytes">The size of the file.</param>
/// <param name="CreatedUtc">When the file was written.</param>
/// <param name="Kind">Why the backup was taken, read from the file's name.</param>
public sealed record BackupArtifactInfo(
    string Path,
    string FileName,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    BackupScheduleKind Kind);

/// <summary>What a backup run did.</summary>
/// <param name="IsSucceeded">True when the backup was written and verified.</param>
/// <param name="TargetKind">The kind of target it was written to.</param>
/// <param name="ArchivePath">Where the backup file is.</param>
/// <param name="Sha256">The SHA-256 of the backup file.</param>
/// <param name="SizeBytes">The size of the backup file.</param>
/// <param name="Message">A short description of the outcome.</param>
/// <param name="Result">Whether the attempt worked.</param>
public sealed record BackupRunResult(
    bool IsSucceeded,
    string TargetKind,
    string ArchivePath,
    string? Sha256,
    long SizeBytes,
    string Message,
    BackupResult Result);

/// <summary>What an archive holds, shown before anything is replaced.</summary>
/// <param name="ArchivePath">The full path of the archive.</param>
/// <param name="FileName">The name of the archive.</param>
/// <param name="CreatedUtc">When the archive was written.</param>
/// <param name="SizeBytes">The size of the archive.</param>
/// <param name="Sha256">The SHA-256 of the archive.</param>
/// <param name="AppVersion">The version of the shop that wrote it.</param>
/// <param name="SchemaVersion">The shape of the data inside it.</param>
/// <param name="MachineName">The machine that wrote it.</param>
/// <param name="IsIntegrityOk">True when the archive still matches its own checksum.</param>
/// <param name="IntegrityMessage">Why the integrity check failed, when it failed.</param>
/// <param name="IsSchemaCompatible">True when this build can restore the archive.</param>
/// <param name="SchemaMessage">Why the archive cannot be restored, when it cannot.</param>
public sealed record RestorePreview(
    string ArchivePath,
    string FileName,
    DateTimeOffset CreatedUtc,
    long SizeBytes,
    string? Sha256,
    string AppVersion,
    string SchemaVersion,
    string MachineName,
    bool IsIntegrityOk,
    string? IntegrityMessage,
    bool IsSchemaCompatible,
    string? SchemaMessage);

/// <summary>What a restore did.</summary>
/// <param name="IsSucceeded">True when the store was replaced.</param>
/// <param name="RestorePointPath">Where the old database was kept, when one was kept.</param>
/// <param name="Message">A short description of the outcome.</param>
/// <param name="RequiresRestart">True when the shop must be restarted to read the new data.</param>
/// <param name="RemovedMediaFiles">The pictures that were removed because the backup did not have them.</param>
public sealed record RestoreOutcome(
    bool IsSucceeded,
    string? RestorePointPath,
    string Message,
    bool RequiresRestart,
    IReadOnlyList<string> RemovedMediaFiles);

/// <summary>The scheduler's settings, as the backup screen shows them.</summary>
/// <param name="TargetKind">The kind of target the scheduled backups are written to.</param>
/// <param name="DailyTimeMinutes">The time of the daily backup, in minutes after midnight, local time.</param>
/// <param name="WeeklyDayOfWeek">The day of the weekly backup, where 0 is Sunday.</param>
/// <param name="WeeklyTimeMinutes">The time of the weekly backup, in minutes after midnight, local time.</param>
/// <param name="DailyEnabled">True when the daily backup runs.</param>
/// <param name="WeeklyEnabled">True when the weekly backup runs.</param>
/// <param name="DailyRetention">How many daily backups are kept.</param>
/// <param name="WeeklyRetention">How many weekly backups are kept.</param>
/// <param name="BackupDirectory">Where local backups are written.</param>
public sealed record BackupScheduleView(
    string TargetKind,
    int DailyTimeMinutes,
    int WeeklyDayOfWeek,
    int WeeklyTimeMinutes,
    bool DailyEnabled,
    bool WeeklyEnabled,
    int DailyRetention,
    int WeeklyRetention,
    string BackupDirectory);
