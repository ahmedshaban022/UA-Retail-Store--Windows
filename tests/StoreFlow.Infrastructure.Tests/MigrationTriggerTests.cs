using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Infrastructure.Persistence;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// A shop that has already run the programme must still be able to be migrated.
/// </summary>
/// <remarks>
/// The guard triggers are laid down on every start, and a migration that changes the shape of a
/// table rebuilds it: SQLite makes a new table, copies the rows, drops the old one and renames
/// the new one. SQLite refuses to drop a table that a trigger still refers to, so a migration of
/// that kind fails with "no such table: main.SalesInvoices" while the triggers are in place. The
/// shop then cannot open at all, and the owner is stranded in the recovery wizard.
///
/// The fix is to take the triggers down before the migrations and put them back after them.
/// </remarks>
public sealed class MigrationTriggerTests : IDisposable
{
    /// <summary>The last migration applied before the shop's triggers were laid down.</summary>
    private const string MigrationBeforeTheRebuild = "20261101161754_AddInvoiceDiscountShare";

    /// <summary>The migration that rebuilds tables, and therefore needs the triggers out of the way.</summary>
    private const string MigrationThatRebuildsTables = "20261101193637_AddSalesReturnsAndReversals";

    /// <summary>The triggers the shop carries once it has run the programme at least once.</summary>
    private static readonly string[] ExpectedTriggers =
    [
        "trg_StockBalances_NoNegative",
        "trg_StockBalances_NoNegativeInsert",
        "trg_StockMovements_NoUpdate",
        "trg_StockMovements_NoDelete",
        "trg_SalesInvoiceLines_NoUpdate",
        "trg_SalesInvoiceLines_NoDelete",
        "trg_PurchaseLines_NoUpdate",
        "trg_PurchaseLines_NoDelete",
        "trg_Products_BarcodeNotTaken",
        "trg_Products_BarcodeNotTakenOnUpdate",
        "trg_ProductBarcodes_NotTaken",
        "trg_ProductBarcodes_NotTakenOnUpdate",
    ];

    private readonly string _directory;
    private readonly string _databasePath;

    /// <summary>Creates a throwaway shop on disk.</summary>
    public MigrationTriggerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "storeflow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "storeflow.db");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A lingering SQLite handle can keep the file locked for a moment; the operating system
            // cleans the temporary directory up afterwards.
        }
    }

    [Fact]
    public async Task A_shop_that_already_ran_the_programme_can_still_be_migrated()
    {
        DbContextOptions<StoreFlowDbContext> options = BuildOptions();

        // A shop that has run the programme: its tables are at the older shape and its triggers are
        // in place, exactly as they are after the programme has been closed and opened again.
        await using (StoreFlowDbContext context = new(options))
        {
            await context.Database.MigrateAsync(MigrationBeforeTheRebuild);
            await context.Database.ExecuteSqlRawAsync(SchemaTriggers.Script);
        }

        List<string> triggersBefore = await ReadTriggerNamesAsync(options);
        triggersBefore.Count.ShouldBe(ExpectedTriggers.Length);

        // The new programme starts and migrates the shop.
        var initializer = new DatabaseInitializer(new SingleContextFactory(options), new TestClock());
        await initializer.InitializeAsync();

        await using StoreFlowDbContext after = new(options);

        List<string> applied = await after.Database
            .SqlQueryRaw<string>(
                "SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"")
            .ToListAsync();

        applied.ShouldContain(MigrationThatRebuildsTables);

        List<string> columns = await after.Database
            .SqlQueryRaw<string>("SELECT \"name\" AS \"Value\" FROM pragma_table_info('SalesInvoices')")
            .ToListAsync();

        columns.ShouldContain("ReversesDocumentId");
    }

    [Fact]
    public async Task The_guards_are_back_after_the_migration()
    {
        DbContextOptions<StoreFlowDbContext> options = BuildOptions();

        await using (StoreFlowDbContext context = new(options))
        {
            await context.Database.MigrateAsync(MigrationBeforeTheRebuild);
            await context.Database.ExecuteSqlRawAsync(SchemaTriggers.Script);
        }

        var initializer = new DatabaseInitializer(new SingleContextFactory(options), new TestClock());
        await initializer.InitializeAsync();

        List<string> triggersAfter = await ReadTriggerNamesAsync(options);

        triggersAfter.ShouldBe(ExpectedTriggers, ignoreOrder: true);
    }

    [Fact]
    public async Task A_shop_whose_database_file_exists_but_is_empty_still_opens()
    {
        // The framework checks whether the database exists before it migrates it, and it opens
        // that check read-only so the check does not bring a database into being. A read-only
        // connection cannot change the journal mode of the file, and the pragmas must not ask it
        // to: the failure would come out of the open and the shop would never start.
        File.WriteAllBytes(_databasePath, []);

        DbContextOptions<StoreFlowDbContext> options = BuildOptions();

        var initializer = new DatabaseInitializer(new SingleContextFactory(options), new TestClock());
        await initializer.InitializeAsync();

        await using StoreFlowDbContext context = new(options);

        List<string> applied = await context.Database
            .SqlQueryRaw<string>(
                "SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"")
            .ToListAsync();

        applied.ShouldContain(MigrationThatRebuildsTables);

        List<string> triggers = await ReadTriggerNamesAsync(options);
        triggers.ShouldBe(ExpectedTriggers, ignoreOrder: true);
    }

    [Fact]
    public async Task A_connection_that_cannot_write_is_not_asked_to_change_the_file()
    {
        // A small database of its own, left in the ordinary journal mode, so that asking for the
        // write-ahead log is a real change and not a no-op that happens to succeed.
        string otherPath = Path.Combine(_directory, "other.db");

        await using (SqliteConnection writable = new($"Data Source={otherPath}"))
        {
            writable.Open();
            using SqliteCommand command = writable.CreateCommand();
            command.CommandText = "CREATE TABLE Probe (Id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = otherPath,
            Mode = SqliteOpenMode.ReadOnly,
        };

        await using SqliteConnection readOnly = new(builder.ToString());
        readOnly.Open();

        Should.NotThrow(() => SqlitePragmaInterceptor.ApplyTo(readOnly));

        // The read-only connection still gets the settings that belong to the connection itself.
        Scalar(readOnly, "PRAGMA foreign_keys").ShouldBe(1L);

        // And the file is left as it was: a connection that cannot write cannot change it.
        Scalar(readOnly, "PRAGMA journal_mode").ShouldBe("delete");
    }

    [Fact]
    public async Task A_connection_that_can_write_puts_the_shop_in_the_write_ahead_log()
    {
        DbContextOptions<StoreFlowDbContext> options = BuildOptions();

        await using (StoreFlowDbContext context = new(options))
        {
            await context.Database.MigrateAsync();
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
        };

        await using SqliteConnection writable = new(builder.ToString());
        writable.Open();

        SqlitePragmaInterceptor.ApplyTo(writable);

        Scalar(writable, "PRAGMA journal_mode").ShouldBe("wal");
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private DbContextOptions<StoreFlowDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<StoreFlowDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

    private static async Task<List<string>> ReadTriggerNamesAsync(DbContextOptions<StoreFlowDbContext> options)
    {
        await using StoreFlowDbContext context = new(options);

        return await context.Database
            .SqlQueryRaw<string>(
                "SELECT \"name\" AS \"Value\" FROM \"sqlite_master\" WHERE \"type\" = 'trigger'")
            .ToListAsync();
    }

    private sealed class SingleContextFactory(DbContextOptions<StoreFlowDbContext> options)
        : IDbContextFactory<StoreFlowDbContext>
    {
        public StoreFlowDbContext CreateDbContext() => new(options);
    }
}
