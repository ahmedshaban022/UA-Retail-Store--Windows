namespace StoreFlow.Application.Settings;

/// <summary>
/// When the scheduled backups run, and where they are written.
/// </summary>
/// <param name="TargetKind">The kind of target the scheduled backups are written to.</param>
/// <param name="DailyTimeMinutes">The time of the daily backup, in minutes after midnight, local time.</param>
/// <param name="WeeklyDayOfWeek">The day of the weekly backup, where 0 is Sunday and 6 is Saturday.</param>
/// <param name="WeeklyTimeMinutes">The time of the weekly backup, in minutes after midnight, local time.</param>
public sealed record BackupScheduleSettings(
    string TargetKind,
    int DailyTimeMinutes,
    int WeeklyDayOfWeek,
    int WeeklyTimeMinutes)
{
    /// <summary>The settings a shop starts with: a daily backup at eleven at night, and one every
    /// Saturday at the same hour.</summary>
    public static BackupScheduleSettings Defaults { get; } = new(
        TargetKind: "LocalFolder",
        DailyTimeMinutes: 23 * 60,
        WeeklyDayOfWeek: 6,
        WeeklyTimeMinutes: 23 * 60);
}
