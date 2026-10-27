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

    /// <summary>The path of the database file under test.</summary>
    public string DatabasePath { get; private set; } = string.Empty;

    /// <summary>Creates contexts against the database under test.</summary>
    public IDbContextFactory<StoreFlowDbContext> Factory { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "storeflow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        DatabasePath = Path.Combine(_directory, "storeflow.db");

        DbContextOptions<StoreFlowDbContext> options = new DbContextOptionsBuilder<StoreFlowDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        Factory = new TestContextFactory(options);

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

    private sealed class TestContextFactory(DbContextOptions<StoreFlowDbContext> options)
        : IDbContextFactory<StoreFlowDbContext>
    {
        public StoreFlowDbContext CreateDbContext() => new(options);
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
