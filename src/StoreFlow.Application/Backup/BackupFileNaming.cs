using System.Globalization;

namespace StoreFlow.Application.Backup;

/// <summary>Why a backup was taken.</summary>
public enum BackupScheduleKind
{
    /// <summary>The owner pressed "back up now".</summary>
    Manual = 0,

    /// <summary>The daily scheduled backup.</summary>
    Daily = 1,

    /// <summary>The weekly scheduled backup.</summary>
    Weekly = 2,
}

/// <summary>
/// The names backup files carry, and how a file is told apart from another.
/// </summary>
/// <remarks>
/// The name carries the kind, so that a file on a disk can be classified without reading its
/// manifest: a file is a daily backup or a weekly one because of what it is called. The names are
/// sorted alphabetically in the same order as their dates, so the newest file is simply the last
/// one in the list.
/// </remarks>
public static class BackupFileNaming
{
    /// <summary>The prefix a manual backup carries.</summary>
    public const string ManualPrefix = "storeflow-manual-";

    /// <summary>The prefix a daily backup carries.</summary>
    public const string DailyPrefix = "storeflow-daily-";

    /// <summary>The prefix a weekly backup carries.</summary>
    public const string WeeklyPrefix = "storeflow-weekly-";

    /// <summary>
    /// Builds the file name for a backup.
    /// </summary>
    /// <param name="kind">Why the backup was taken.</param>
    /// <param name="createdUtc">When the backup was taken, in UTC.</param>
    /// <returns>The file name.</returns>
    public static string BuildName(BackupScheduleKind kind, DateTimeOffset createdUtc)
    {
        string prefix = PrefixFor(kind);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}{createdUtc.UtcDateTime:yyyy-MM-dd-HHmmss}Z{BackupArchive.FileExtension}");
    }

    /// <summary>
    /// Reads the kind out of a file name.
    /// </summary>
    /// <param name="fileName">The name of the file.</param>
    /// <returns>The kind, or null when the name is not a backup file.</returns>
    public static BackupScheduleKind? Classify(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if (fileName.StartsWith(DailyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BackupScheduleKind.Daily;
        }

        if (fileName.StartsWith(WeeklyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BackupScheduleKind.Weekly;
        }

        if (fileName.StartsWith(ManualPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BackupScheduleKind.Manual;
        }

        return null;
    }

    /// <summary>
    /// The prefix that belongs to a kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The prefix.</returns>
    public static string PrefixFor(BackupScheduleKind kind) => kind switch
    {
        BackupScheduleKind.Daily => DailyPrefix,
        BackupScheduleKind.Weekly => WeeklyPrefix,
        _ => ManualPrefix,
    };
}
