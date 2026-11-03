using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// A throwaway SQLite database for one test, so the integration tests exercise the real provider,
/// the real pragmas and the real triggers rather than a substitute.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private string _directory = string.Empty;
    private readonly ParameterCountInterceptor _parameters = new();

    /// <summary>The most parameters seen on a command since the last reset.</summary>
    public int LargestParameterCount => _parameters.Largest;

    /// <summary>Starts a fresh measurement of command parameters.</summary>
    public void ResetParameterCount() => _parameters.Reset();

    /// <summary>The path of the database file under test.</summary>
    public string DatabasePath { get; private set; } = string.Empty;

    /// <summary>Creates contexts against the database under test.</summary>
    public IDbContextFactory<StoreFlowDbContext> Factory { get; private set; } = null!;

    /// <summary>
    /// The fault injector the contexts carry. A test arms it to fail one write on purpose, so that
    /// the command can be shown to have committed everything or nothing.
    /// </summary>
    public ControllableWriteFaultInjector Faults { get; } = new();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "storeflow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        DatabasePath = Path.Combine(_directory, "storeflow.db");

        DbContextOptions<StoreFlowDbContext> options = new DbContextOptionsBuilder<StoreFlowDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .AddInterceptors(new SqlitePragmaInterceptor(), _parameters)
            .Options;

        Factory = new TestContextFactory(options, Faults);

        var initializer = new DatabaseInitializer(Factory, new TestClock());
        await initializer.InitializeAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_directory) && Directory.Exists(_directory))
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A lingering SQLite handle can keep the file locked for a moment; the temporary
                // directory is cleaned up by the operating system afterwards.
            }
        }

        return Task.CompletedTask;
    }

    private sealed class ParameterCountInterceptor : DbCommandInterceptor
    {
        private int _largest;

        public int Largest => Volatile.Read(ref _largest);

        public void Reset() => Interlocked.Exchange(ref _largest, 0);

        private void Record(DbCommand command)
        {
            int count = command.Parameters.Count;
            int current;
            do
            {
                current = Largest;
                if (count <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _largest, count, current) != current);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestContextFactory(
        DbContextOptions<StoreFlowDbContext> options,
        ControllableWriteFaultInjector faults)
        : IDbContextFactory<StoreFlowDbContext>
    {
        public StoreFlowDbContext CreateDbContext() => new(options, faults);
    }
}

/// <summary>A clock that does not move, so timestamps are predictable in tests.</summary>
public sealed class TestClock : Application.Abstractions.IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public DateTimeOffset LocalNow => UtcNow;

    /// <inheritdoc />
    public DateOnly LocalToday { get; } = new(2026, 1, 1);
}
