using System.IO;
using Microsoft.Data.Sqlite;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Backup;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Services;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The path the shop takes when its own database cannot be read: the health check has to answer
/// rather than throw, and the recovery has to be allowed to run. This is the day the recovery
/// wizard exists for, so it is the day it must not fail.
/// </summary>
public sealed class RecoveryTests : IDisposable
{
    private string _root = string.Empty;

    /// <inheritdoc />
    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A lingering SQLite handle can keep a file locked for a moment.
            }
        }
    }

    [Fact]
    public void The_health_check_says_a_file_that_is_not_a_database_is_not_healthy()
    {
        string path = Write("not-a-database.db", "this is a text file, not a database");

        bool healthy = SqliteSnapshot.IsHealthy(path, out string message);

        healthy.ShouldBeFalse();
        message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_health_check_says_random_bytes_are_not_a_database()
    {
        string path = Write("random.db", string.Join(string.Empty, Enumerable.Range(0, 512).Select(index => (char)(index % 256))));

        bool healthy = SqliteSnapshot.IsHealthy(path, out string message);

        healthy.ShouldBeFalse();
        message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_health_check_says_a_file_that_is_not_there_is_not_healthy()
    {
        string path = Path.Combine(_root, "never-existed.db");

        bool healthy = SqliteSnapshot.IsHealthy(path, out string message);

        healthy.ShouldBeFalse();
        message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_recovery_is_allowed_when_the_shop_database_cannot_be_read()
    {
        AppPaths paths = NewPaths();
        var restore = new RecordingRestoreService { Outcome = new RestoreOutcome(true, null, "Restored.", true, []) };

        var handler = new RecoverStoreCommandHandler(paths, restore, new ThrowingAuditWriter());

        // The shop's own database has been replaced by a text file. The wizard has to be allowed to
        // do its job, and the damage to the trail must not stop the answer from coming back.
        File.WriteAllText(paths.DatabasePath, "this is not a database");

        Result<RestoreOutcome> result = await handler.HandleAsync(
            new RecoverStoreCommand("some-backup.sfb"),
            CancellationToken.None);

        result.IsFailure.ShouldBeFalse(result.Error.Message);
        restore.Called.ShouldBeTrue();
    }

    [Fact]
    public async Task The_recovery_is_refused_when_the_shop_database_can_still_be_read()
    {
        AppPaths paths = NewPaths();
        var restore = new RecordingRestoreService();

        var handler = new RecoverStoreCommandHandler(paths, restore, new ThrowingAuditWriter());

        // A real, healthy database: the owner signs in and uses the ordinary restore wizard.
        using (SqliteConnection connection = SqliteSnapshot.Open(paths.DatabasePath, readOnly: false))
        {
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE probe (id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        Result<RestoreOutcome> result = await handler.HandleAsync(
            new RecoverStoreCommand("some-backup.sfb"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        restore.Called.ShouldBeFalse();
    }

    private AppPaths NewPaths()
    {
        _root = Path.Combine(Path.GetTempPath(), "storeflow-recovery-tests", Guid.NewGuid().ToString("N"));

        var paths = new AppPaths(_root);
        paths.EnsureCreated();

        return paths;
    }

    private string Write(string name, string content)
    {
        _root = Path.Combine(Path.GetTempPath(), "storeflow-recovery-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);

        string path = Path.Combine(_root, name);

        File.WriteAllText(path, content);

        return path;
    }

    private sealed class RecordingRestoreService : IBackupRestoreService
    {
        public bool Called { get; private set; }

        public RestoreOutcome Outcome { get; init; } = new(false, null, "Refused.", false, []);

        public Task<RestoreOutcome> RestoreAsync(
            string archivePath,
            bool createRestorePoint,
            CancellationToken cancellationToken = default)
        {
            Called = true;

            return Task.FromResult(Outcome);
        }
    }

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The shop's database cannot be written to.");

        public void AddTo(IAuditTrail trail, AuditEntry entry) =>
            throw new InvalidOperationException("The shop's database cannot be written to.");
    }
}
