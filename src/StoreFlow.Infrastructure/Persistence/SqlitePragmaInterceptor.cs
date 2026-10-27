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
    /// <summary>The pragmas applied to every connection, in order.</summary>
    public const string PragmaScript = """
        PRAGMA foreign_keys = ON;
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = FULL;
        PRAGMA busy_timeout = 5000;
        PRAGMA temp_store = MEMORY;
        """;

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqliteConnection sqliteConnection)
        {
            using SqliteCommand command = sqliteConnection.CreateCommand();
            command.CommandText = PragmaScript;
            command.ExecuteNonQuery();
        }
    }
}
