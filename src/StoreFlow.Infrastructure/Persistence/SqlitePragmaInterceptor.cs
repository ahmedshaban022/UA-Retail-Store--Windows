using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StoreFlow.Infrastructure.Persistence;

/// <summary>
/// Applies the SQLite settings the product depends on to every connection, so no code path can
/// open a database that behaves differently from the one the tests used.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><c>foreign_keys = ON</c> — enforced on every connection and tested.</description></item>
/// <item><description><c>journal_mode = WAL</c> — readers do not block the writer.</description></item>
/// <item><description><c>synchronous = FULL</c> — the correct pairing with WAL for a shop that may lose
/// power mid-sale; a committed sale must survive a sudden power cut.</description></item>
/// <item><description><c>busy_timeout = 5000</c> — waits instead of failing when another writer holds
/// the lock.</description></item>
/// </list>
/// </remarks>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// The pragmas that change the connection itself. They are safe on a connection that cannot
    /// write, so they are applied to every connection without question.
    /// </summary>
    /// <remarks>
    /// The busy timeout comes first: a connection that finds the database locked should wait for
    /// the lock rather than fail, and the wait must be in place before anything else is asked of
    /// the database.
    /// </remarks>
    public const string ConnectionPragmaScript = """
        PRAGMA busy_timeout = 5000;
        PRAGMA foreign_keys = ON;
        PRAGMA synchronous = FULL;
        PRAGMA temp_store = MEMORY;
        """;

    /// <summary>
    /// The statement that changes the file: the journal mode belongs to the database file, not to
    /// the connection that happens to be open.
    /// </summary>
    public const string JournalModeStatement = "PRAGMA journal_mode = WAL;";

    /// <summary>SQLite's SQLITE_READONLY: the connection was not opened able to write.</summary>
    private const int ReadOnlyErrorCode = 8;

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqliteConnection sqliteConnection)
        {
            ApplyTo(sqliteConnection);
        }
    }

    /// <summary>
    /// Applies the settings to a connection that has just been opened.
    /// </summary>
    /// <remarks>
    /// A connection that cannot write cannot change the journal mode of the file, and the
    /// framework opens one of those when it checks whether the database exists: that check must
    /// not bring a database into being as a side effect, so it is opened read-only. Asking such a
    /// connection to change the journal mode fails with "attempt to write a readonly database",
    /// and the failure comes out of the open and stops the shop from starting.
    ///
    /// Nothing is lost when the switch is refused. The journal mode is a property of the file:
    /// the connection that creates the shop sets it, and every later writable connection sees it.
    /// </remarks>
    /// <param name="connection">The connection that has just been opened.</param>
    public static void ApplyTo(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = ConnectionPragmaScript;
            command.ExecuteNonQuery();
        }

        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = JournalModeStatement;
            command.ExecuteScalar();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == ReadOnlyErrorCode)
        {
            // This connection cannot write, so it cannot change the file. The journal mode stays
            // what it is, and the connections that can write set it.
        }
    }
}
