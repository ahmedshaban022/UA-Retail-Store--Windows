namespace StoreFlow.Application.Backup;

/// <summary>
/// Decides which backup files have grown past their welcome.
/// </summary>
/// <remarks>
/// <para>Retention keeps a number of daily copies and a number of weekly copies (PRD §8.5). When a
/// new copy is written, the oldest one past that number is removed.</para>
/// <para>Two rules protect the shop. The newest backup is never removed: the number of copies kept
/// is never allowed to fall below one, however small the shop sets the retention to. And backups
/// the owner took by hand are never removed by the scheduler at all — only the owner decides when
/// those go.</para>
/// </remarks>
public static class BackupRetentionPolicy
{
    /// <summary>
    /// The copies that should be removed.
    /// </summary>
    /// <param name="artifacts">Every backup file that exists.</param>
    /// <param name="dailyRetention">How many daily copies to keep.</param>
    /// <param name="weeklyRetention">How many weekly copies to keep.</param>
    /// <returns>The files to remove, newest first.</returns>
    public static IReadOnlyList<BackupArtifactInfo> SelectExpired(
        IReadOnlyList<BackupArtifactInfo> artifacts,
        int dailyRetention,
        int weeklyRetention)
    {
        ArgumentNullException.ThrowIfNull(artifacts);

        // The newest copy of each kind is always kept, so a retention of zero still leaves the
        // shop with something to restore from.
        int keepDaily = Math.Max(1, dailyRetention);
        int keepWeekly = Math.Max(1, weeklyRetention);

        var expired = new List<BackupArtifactInfo>();

        expired.AddRange(ExpiredBeyond(artifacts, BackupScheduleKind.Daily, keepDaily));
        expired.AddRange(ExpiredBeyond(artifacts, BackupScheduleKind.Weekly, keepWeekly));

        return expired;
    }

    private static IEnumerable<BackupArtifactInfo> ExpiredBeyond(
        IReadOnlyList<BackupArtifactInfo> artifacts,
        BackupScheduleKind kind,
        int keep)
    {
        List<BackupArtifactInfo> group = artifacts
            .Where(artifact => artifact.Kind == kind)
            .OrderByDescending(artifact => artifact.CreatedUtc)
            .ThenByDescending(artifact => artifact.FileName, StringComparer.Ordinal)
            .ToList();

        if (group.Count <= keep)
        {
            return [];
        }

        return group.Skip(keep);
    }
}
