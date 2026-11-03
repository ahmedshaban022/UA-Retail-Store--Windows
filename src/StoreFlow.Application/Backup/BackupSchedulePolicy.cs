using StoreFlow.Application.Settings;

namespace StoreFlow.Application.Backup;

/// <summary>When the scheduled backups last ran.</summary>
/// <param name="LastDailyDate">The local day the last daily backup ran on.</param>
/// <param name="LastWeeklyDate">The local day the last weekly backup ran on.</param>
public sealed record BackupScheduleState(DateOnly? LastDailyDate, DateOnly? LastWeeklyDate)
{
    /// <summary>Nothing has run yet.</summary>
    public static BackupScheduleState None { get; } = new(null, null);
}

/// <summary>Which of the scheduled backups are due.</summary>
/// <param name="IsDailyDue">True when the daily backup is due.</param>
/// <param name="IsWeeklyDue">True when the weekly backup is due.</param>
public sealed record BackupScheduleDecision(bool IsDailyDue, bool IsWeeklyDue)
{
    /// <summary>Nothing is due.</summary>
    public static BackupScheduleDecision None { get; } = new(false, false);
}

/// <summary>
/// Decides whether the shop is due a scheduled backup, and what happens when the shop was closed
/// on the day a backup was due.
/// </summary>
/// <remarks>
/// <para>The catch-up rule is this: when the application starts and finds that a scheduled backup
/// was missed, it takes one, not one for every missed day. A shop that was shut for a week comes
/// back with one fresh daily backup and one fresh weekly backup, rather than eight of each. The
/// older days are covered by the copies that are already on disk.</para>
/// <para>The daily backup becomes due at its time on the day it is due. The weekly backup becomes
/// due at its time on its day, and stays due until it has been taken — so a shop that starts up
/// on the Monday after its Saturday backup takes it then.</para>
/// </remarks>
public static class BackupSchedulePolicy
{
    /// <summary>
    /// Decides which backups are due.
    /// </summary>
    /// <param name="schedule">When and where the backups are written.</param>
    /// <param name="dailyEnabled">True when the daily backup runs.</param>
    /// <param name="weeklyEnabled">True when the weekly backup runs.</param>
    /// <param name="localNow">The current instant, in local time.</param>
    /// <param name="state">When the scheduled backups last ran.</param>
    /// <returns>Which of the backups are due.</returns>
    public static BackupScheduleDecision Decide(
        BackupScheduleSettings schedule,
        bool dailyEnabled,
        bool weeklyEnabled,
        DateTimeOffset localNow,
        BackupScheduleState state)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(state);

        bool dailyDue = false;
        bool weeklyDue = false;

        // The schedule is a wall-clock time: "eleven at night" means eleven at night where the
        // shop is, not eleven at night in Greenwich. So the whole rule is worked out in the
        // clock's own terms, and no time zone is applied twice.
        DateTime wallClock = localNow.DateTime;
        DateOnly today = DateOnly.FromDateTime(wallClock);

        if (dailyEnabled)
        {
            DateTime occurrence = wallClock.Date.AddMinutes(schedule.DailyTimeMinutes);

            dailyDue = wallClock >= occurrence && (state.LastDailyDate is null || state.LastDailyDate < today);
        }

        if (weeklyEnabled)
        {
            int scheduledDay = Math.Clamp(schedule.WeeklyDayOfWeek, 0, 6);

            // The most recent day the weekly backup was meant to run, whether or not it did.
            int daysBack = ((int)today.DayOfWeek - scheduledDay + 7) % 7;
            DateOnly occurrenceDay = today.AddDays(-daysBack);
            DateTime occurrence = occurrenceDay.ToDateTime(TimeOnly.MinValue).AddMinutes(schedule.WeeklyTimeMinutes);

            weeklyDue = wallClock >= occurrence && (state.LastWeeklyDate is null || state.LastWeeklyDate < occurrenceDay);
        }

        return new BackupScheduleDecision(dailyDue, weeklyDue);
    }
}
