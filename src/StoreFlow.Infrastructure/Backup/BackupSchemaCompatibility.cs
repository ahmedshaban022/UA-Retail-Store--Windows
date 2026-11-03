namespace StoreFlow.Infrastructure.Backup;

/// <summary>What the schema of a backup means for this build of the shop.</summary>
/// <param name="IsCompatible">True when this build can restore the backup.</param>
/// <param name="Message">Why it cannot, when it cannot.</param>
/// <param name="IsOlder">True when the backup was made by an earlier version of the shop.</param>
public sealed record SchemaCompatibility(bool IsCompatible, string? Message, bool IsOlder);

/// <summary>
/// Decides whether a backup may be restored by this build of the shop.
/// </summary>
/// <remarks>
/// A backup is written against the shape of the data at the time it was taken, and that shape is
/// named by the migration the database was on. Three answers are possible:
/// <list type="bullet">
/// <item>The same shape: the backup restores exactly.</item>
/// <item>An older shape this build still knows: the backup restores, and the shop brings the data
/// forward with its own migrations the next time it starts. This is the documented rollback path
/// (NFR-12), and it is safe because a backup is taken before any migration that could change the
/// shape.</item>
/// <item>A shape this build does not know — either newer, or from a build this one has never
/// heard of: the backup is refused, because restoring it could leave the shop with data it cannot
/// read.</item>
/// </list>
/// </remarks>
public static class BackupSchemaCompatibility
{
    /// <summary>
    /// Decides whether a backup may be restored.
    /// </summary>
    /// <param name="archiveSchemaVersion">The shape the backup was written against.</param>
    /// <param name="knownSchemaVersions">Every shape this build knows.</param>
    /// <param name="currentSchemaVersion">The shape this build is running.</param>
    /// <returns>What the backup means for this build.</returns>
    public static SchemaCompatibility Evaluate(
        string archiveSchemaVersion,
        IReadOnlyList<string> knownSchemaVersions,
        string currentSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(knownSchemaVersions);

        string archive = archiveSchemaVersion ?? string.Empty;

        if (string.Equals(archive, currentSchemaVersion, StringComparison.Ordinal))
        {
            return new SchemaCompatibility(true, null, false);
        }

        if (string.IsNullOrEmpty(currentSchemaVersion))
        {
            // The shop's own shape is unknown — the database cannot be read, which is exactly the
            // situation a recovery is for. The archive is judged on its own: it must be a shape
            // this build knows.
            return knownSchemaVersions.Contains(archive, StringComparer.Ordinal)
                ? new SchemaCompatibility(true, null, true)
                : new SchemaCompatibility(
                    false,
                    "This backup was made by a version of StoreFlow that this one does not know, so it " +
                    "cannot be restored safely.",
                    false);
        }

        if (knownSchemaVersions.Contains(archive, StringComparer.Ordinal))
        {
            return new SchemaCompatibility(true, null, true);
        }

        if (string.CompareOrdinal(archive, currentSchemaVersion) > 0)
        {
            return new SchemaCompatibility(
                false,
                "This backup was made by a newer version of StoreFlow than the one installed. " +
                "Update StoreFlow, then restore the backup.",
                false);
        }

        return new SchemaCompatibility(
            false,
            "This backup was made by a version of StoreFlow that this one does not know, so it " +
            "cannot be restored safely.",
            false);
    }
}
