using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// Reads the shape of the data this build of the shop knows, and the shape it is running.
/// </summary>
public static class BackupSchemaVersions
{
    /// <summary>
    /// Reads the known and the current schema versions.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The known versions and the current one.</returns>
    public static async Task<(IReadOnlyList<string> Known, string Current)> ReadAsync(
        StoreFlowDbContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The migrations this build knows are read from the assembly, so they are available even
        // when the shop's own database cannot be opened.
        IReadOnlyList<string> known = context.Database.GetMigrations().ToList();

        string current = string.Empty;

        try
        {
            IReadOnlyList<string> applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();

            current = applied.Count == 0 ? string.Empty : applied[^1];
        }
        catch (SqliteException)
        {
            // The database cannot be read. The current shape is unknown, and an unknown shape is
            // handled as "no shape to compare against" rather than as a failure.
            current = string.Empty;
        }

        return (known, current);
    }
}
