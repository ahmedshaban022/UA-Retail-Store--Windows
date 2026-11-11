using Microsoft.Data.Sqlite;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// Takes a safe copy of a SQLite database and checks that the copy is sound.
/// </summary>
/// <remarks>
/// The shop runs SQLite in WAL mode, so the database file on disk is only part of the story: there
/// may be committed pages still sitting in the write-ahead log. Copying the bytes of the file
/// would therefore capture a database that is missing its most recent writes, and could capture
/// half of a page that was being written at the time. SQLite's own online backup API walks the
/// database through the same read path a reader would use, so the copy is a consistent snapshot of
/// the committed database (PRD §8.4).
/// </remarks>
public static class SqliteSnapshot
{
    /// <summary>
    /// Copies a database into a new file, safely.
    /// </summary>
    /// <param name="sourcePath">The database to copy.</param>
    /// <param name="destinationPath">Where the copy is written.</param>
    public static void Copy(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        using SqliteConnection source = Open(sourcePath, readOnly: false);
        using SqliteConnection destination = Open(destinationPath, readOnly: false);

        source.Open();
        destination.Open();

        source.BackupDatabase(destination);
    }

    /// <summary>
    /// Runs SQLite's own integrity check over a database file.
    /// </summary>
    /// <remarks>
    /// This check answers; it never throws. A file that is not a SQLite database at all, or one so
    /// damaged that SQLite cannot read its header, is not sound — and that is the only answer the
    /// caller can act on. The recovery wizard exists for exactly the case where the shop's own
    /// file cannot be read, so an exception here would take the programme down on the one day the
    /// owner needs it.
    /// </remarks>
    /// <param name="databasePath">The database to check.</param>
    /// <param name="message">What the check said.</param>
    /// <returns>True when the database is sound.</returns>
    public static bool IsHealthy(string databasePath, out string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        try
        {
            using SqliteConnection connection = Open(databasePath, readOnly: true);
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";

            object? value = command.ExecuteScalar();
            message = value as string ?? string.Empty;

            return string.Equals(message, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException exception)
        {
            // SQLite could not read the file at all: it is not a database, or its header is broken.
            message = $"The file could not be read as a SQLite database ({exception.Message}).";

            return false;
        }
        catch (IOException exception)
        {
            message = $"The file could not be opened ({exception.Message}).";

            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            message = $"The file could not be opened ({exception.Message}).";

            return false;
        }
    }

    /// <summary>
    /// Opens a connection to a database file.
    /// </summary>
    /// <param name="databasePath">The database file.</param>
    /// <param name="readOnly">True to open the file read-only.</param>
    /// <returns>The open connection; the caller disposes it.</returns>
    public static SqliteConnection Open(string databasePath, bool readOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = readOnly
                ? SqliteOpenMode.ReadOnly
                : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,

            // The copy is a file of its own that is read once and thrown away. Pooling it would
            // keep the file handle open after the connection was closed, and the file could not
            // then be deleted.
            Pooling = false,
        };

        return new SqliteConnection(builder.ToString());
    }
}
