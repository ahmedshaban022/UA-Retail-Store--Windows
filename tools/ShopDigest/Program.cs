using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace StoreFlow.Tools.ShopDigest;

/// <summary>
/// Prints what a shop's database actually holds, and the SHA-256 of that text.
/// </summary>
/// <remarks>
/// <para>The release cycle has to say that the shop's data came through an upgrade untouched. The
/// bytes of the database file cannot answer that: the shop runs SQLite in WAL mode, so closing
/// the programme checkpoints the write-ahead log into the file and the file changes even when
/// nothing was written. What does not change is the <em>content</em>, and this tool prints it in
/// a canonical form: every table, its schema, and its rows in a fixed order, so that two shops
/// that hold the same data print exactly the same text.</para>
/// <para>Usage: <c>ShopDigest &lt;database file&gt; [dump file]</c>. The digest goes to the
/// standard output; the dump, when a path is given, is written next to it as the evidence a
/// person can read.</para>
/// </remarks>
internal static class Program
{
    /// <summary>The exit code when the database could not be read.</summary>
    private const int FailureExitCode = 2;

    /// <summary>Runs the tool.</summary>
    /// <param name="args">The database file, and optionally the file the dump is written to.</param>
    /// <returns>Zero when the database was read, two when it could not be.</returns>
    private static int Main(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("usage: ShopDigest <database file> [dump file]");
            return FailureExitCode;
        }

        string databasePath = args[0];
        string? dumpPath = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : null;

        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine("the database is not there: " + databasePath);
            return FailureExitCode;
        }

        string dump;
        int tables;
        long rows;

        try
        {
            (dump, tables, rows) = Read(databasePath);
        }
        catch (SqliteException ex)
        {
            Console.Error.WriteLine("the database could not be read: " + ex.Message);
            return FailureExitCode;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine("the database could not be read: " + ex.Message);
            return FailureExitCode;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine("the database could not be read: " + ex.Message);
            return FailureExitCode;
        }

        byte[] bytes = new UTF8Encoding(false).GetBytes(dump);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (dumpPath is not null)
        {
            File.WriteAllText(dumpPath, dump, new UTF8Encoding(false));
        }

        Console.WriteLine("sha256=" + hash);
        Console.WriteLine("tables=" + tables.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("rows=" + rows.ToString(CultureInfo.InvariantCulture));

        // Every table is hashed on its own as well, so that a difference can be named.
        PrintTableHashes(databasePath);

        return 0;
    }

    /// <summary>Reads the database and returns its canonical text, the table count and the row count.</summary>
    /// <param name="databasePath">The database file.</param>
    /// <returns>The canonical text, the number of tables and the number of rows.</returns>
    private static (string Dump, int Tables, long Rows) Read(string databasePath)
    {
        var builder = new StringBuilder();

        using SqliteConnection connection = new("Data Source=" + databasePath);
        connection.Open();

        List<string> tables = ReadTableNames(connection);

        long rows = 0;

        foreach (string table in tables)
        {
            string schema = ReadSchema(connection, table);

            builder.Append("-- table: ").Append(table).Append('\n');
            builder.Append("-- schema: ").Append(schema).Append('\n');

            List<string> lines = ReadRows(connection, table);

            builder.Append("-- rows: ").Append(lines.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');

            foreach (string line in lines)
            {
                builder.Append(line).Append('\n');
            }

            rows += lines.Count;
        }

        return (builder.ToString(), tables.Count, rows);
    }

    /// <summary>
    /// Prints the hash of every table on its own, so that a difference between two shops can be
    /// named rather than merely detected.
    /// </summary>
    /// <param name="databasePath">The database file.</param>
    /// <returns>The number of tables and the number of rows.</returns>
    private static (int Tables, long Rows) PrintTableHashes(string databasePath)
    {
        using SqliteConnection connection = new("Data Source=" + databasePath);
        connection.Open();

        List<string> tables = ReadTableNames(connection);

        long rows = 0;

        foreach (string table in tables)
        {
            List<string> lines = ReadRows(connection, table);

            string hash = Convert.ToHexString(
                SHA256.HashData(new UTF8Encoding(false).GetBytes(string.Join("\n", lines))))
                .ToLowerInvariant();

            Console.WriteLine(
                "table=" + table +
                " rows=" + lines.Count.ToString(CultureInfo.InvariantCulture) +
                " sha256=" + hash);

            rows += lines.Count;
        }

        return (tables.Count, rows);
    }

    /// <summary>Lists the tables of the database, in a fixed order.</summary>
    /// <param name="connection">The open connection.</param>
    /// <returns>The names of the tables, excluding SQLite's own bookkeeping tables.</returns>
    private static List<string> ReadTableNames(SqliteConnection connection)
    {
        var names = new List<string>();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite@_%' ESCAPE '@' " +
            "ORDER BY name COLLATE BINARY;";

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>Reads the statement that made a table.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="table">The table's name.</param>
    /// <returns>The statement, with its runs of whitespace collapsed.</returns>
    private static string ReadSchema(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);

        object? value = command.ExecuteScalar();

        if (value is null)
        {
            return string.Empty;
        }

        return Collapse(((string)value).Trim());
    }

    /// <summary>Reads the rows of a table, in a fixed order.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="table">The table's name.</param>
    /// <returns>One line per row, in the order the lines sort.</returns>
    private static List<string> ReadRows(SqliteConnection connection, string table)
    {
        var lines = new List<string>();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\";";

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            var line = new StringBuilder();

            for (int column = 0; column < reader.FieldCount; column++)
            {
                if (column > 0)
                {
                    line.Append('|');
                }

                line.Append(Describe(reader, column));
            }

            lines.Add(line.ToString());
        }

        lines.Sort(StringComparer.Ordinal);

        return lines;
    }

    /// <summary>Renders one value in a way that cannot be confused with another value.</summary>
    /// <param name="reader">The reader holding the row.</param>
    /// <param name="column">The column's index.</param>
    /// <returns>The rendered value.</returns>
    private static string Describe(SqliteDataReader reader, int column)
    {
        if (reader.IsDBNull(column))
        {
            return "n";
        }

        object value = reader.GetValue(column);

        return value switch
        {
            long number => "i" + number.ToString(CultureInfo.InvariantCulture),
            int number => "i" + number.ToString(CultureInfo.InvariantCulture),
            bool flag => "b" + (flag ? "1" : "0"),
            double number => "r" + number.ToString("R", CultureInfo.InvariantCulture),
            float number => "r" + number.ToString("R", CultureInfo.InvariantCulture),
            decimal number => "r" + number.ToString(CultureInfo.InvariantCulture),
            byte[] bytes => "x" + Convert.ToHexString(bytes).ToLowerInvariant(),
            _ => Text(value.ToString() ?? string.Empty),
        };
    }

    /// <summary>Renders text, with its length, so that it cannot be confused with another value.</summary>
    /// <param name="text">The text to render.</param>
    /// <returns>The rendered text.</returns>
    private static string Text(string text)
    {
        return "s" + text.Length.ToString(CultureInfo.InvariantCulture) + ":" + Escape(text);
    }

    /// <summary>Escapes the characters that would otherwise change the shape of a line.</summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>The escaped text.</returns>
    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (char character in text)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Collapses the runs of whitespace in a statement.</summary>
    /// <param name="text">The statement.</param>
    /// <returns>The statement, with single spaces.</returns>
    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool space = false;

        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(character);
        }

        return builder.ToString();
    }
}
