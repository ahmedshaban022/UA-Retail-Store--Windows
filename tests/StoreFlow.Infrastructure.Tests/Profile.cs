using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Domain.Authorization;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// One shop's data folder: its own database, its own media, its own backups. A test that needs
/// two shops — a backup and a restore into another profile — makes two of these.
/// </summary>
public sealed class Profile : IAsyncDisposable
{
    private string _root = string.Empty;

    /// <summary>The data root of this profile.</summary>
    public string Root => _root;

    /// <summary>The known locations inside the profile.</summary>
    public AppPaths Paths { get; private set; } = null!;

    /// <summary>Creates contexts against this profile's database.</summary>
    public IDbContextFactory<StoreFlowDbContext> Factory { get; private set; } = null!;

    /// <summary>
    /// Creates a profile with a fresh database, brought up to date and seeded.
    /// </summary>
    /// <returns>The profile.</returns>
    public static async Task<Profile> CreateAsync()
    {
        var profile = new Profile();

        profile._root = Path.Combine(Path.GetTempPath(), "storeflow-profiles", Guid.NewGuid().ToString("N"));

        profile.Paths = new AppPaths(profile._root);
        profile.Paths.EnsureCreated();

        DbContextOptions<StoreFlowDbContext> options = new DbContextOptionsBuilder<StoreFlowDbContext>()
            .UseSqlite($"Data Source={profile.Paths.DatabasePath}")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        profile.Factory = new ProfileContextFactory(options);

        var initializer = new DatabaseInitializer(profile.Factory, new MutableClock());

        await initializer.InitializeAsync();

        return profile;
    }

    /// <summary>Removes the profile's folder.</summary>
    public ValueTask DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A lingering SQLite handle can keep a file locked for a moment; the temporary
                // directory is cleaned up by the operating system afterwards.
            }
            catch (UnauthorizedAccessException)
            {
                // The same.
            }
        }

        return ValueTask.CompletedTask;
    }

    private sealed class ProfileContextFactory(DbContextOptions<StoreFlowDbContext> options)
        : IDbContextFactory<StoreFlowDbContext>
    {
        public StoreFlowDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// A clock the test moves by hand, so that a backup taken today and a backup taken tomorrow can
/// be told apart.
/// </summary>
public sealed class MutableClock : IClock
{
    /// <summary>The current instant, in UTC.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public DateTimeOffset LocalNow => UtcNow;

    /// <inheritdoc />
    public DateOnly LocalToday => DateOnly.FromDateTime(UtcNow.LocalDateTime);
}

/// <summary>
/// A session with one signed-in user, so that the audit trail names an actor.
/// </summary>
public sealed class ShopSession : IUserSession
{
    /// <summary>The signed-in user's identifier.</summary>
    public int UserId { get; set; }

    /// <summary>The signed-in user's name.</summary>
    public string DisplayName { get; set; } = "Tester";

    /// <summary>True when the signed-in user is an administrator.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>The permissions the signed-in user holds.</summary>
    public HashSet<Permission> Permissions { get; } = [];

    /// <inheritdoc />
    public UserSession? Current => new(
        UserId,
        "tester",
        DisplayName,
        Permissions,
        IsAdministrator);

    /// <inheritdoc />
    public bool IsAuthenticated => true;

    /// <inheritdoc />
    public bool IsLocked => false;

    /// <inheritdoc />
    public void SignIn(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        UserId = session.UserId;
        DisplayName = session.DisplayName;
        IsAdministrator = session.IsAdministrator;
    }

    /// <inheritdoc />
    public void SignOut()
    {
    }

    /// <inheritdoc />
    public void Lock()
    {
    }

    /// <inheritdoc />
    public void Unlock()
    {
    }
}
