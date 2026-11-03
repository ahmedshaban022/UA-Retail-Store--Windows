using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Settings;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>The shop's own settings, as they are written into a backup.</summary>
/// <param name="StoreDetails">The shop's name, address and tax number.</param>
/// <param name="StoreSettings">The shop's behavioural settings.</param>
public sealed record BackupSettingsDocument(StoreDetails? StoreDetails, StoreSettings StoreSettings);

/// <summary>Raised when the shop could not be captured.</summary>
public sealed class BackupCaptureException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong.</param>
    public BackupCaptureException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The error underneath.</param>
    public BackupCaptureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Captures the shop into a <c>.sfb</c> archive: the database, copied safely and checked for
/// damage; the product pictures; the shop's settings; and a manifest that says what all of it is
/// and what every part's checksum is.
/// </summary>
public sealed class SqliteBackupCaptureService(
    AppPaths paths,
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IClock clock) : IBackupCaptureService
{
    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        WriteIndented = true,
    };

    /// <inheritdoc />
    public async Task<BackupCaptureResult> CaptureAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        string? directory = Path.GetDirectoryName(archivePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string snapshotPath = Path.Combine(
            Path.GetTempPath(),
            $"storeflow-capture-{Guid.NewGuid():N}.db");

        try
        {
            SqliteSnapshot.Copy(paths.DatabasePath, snapshotPath);

            if (!SqliteSnapshot.IsHealthy(snapshotPath, out string health))
            {
                throw new BackupCaptureException(
                    $"The copied database did not pass its integrity check ({health}), so no backup was written.");
            }

            string schemaVersion = await ReadSchemaVersionAsync(cancellationToken);
            BackupSettingsDocument document = await ReadSettingsAsync(cancellationToken);

            var parts = new List<BackupPartEntry>();
            BackupManifest manifest;

            using (FileStream output = new(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (FileStream database = File.OpenRead(snapshotPath))
                {
                    parts.Add(BackupArchive.AddPart(archive, BackupArchive.DatabaseEntryPath, database));
                }

                parts.Add(BackupArchive.AddPart(
                    archive,
                    BackupArchive.SettingsEntryPath,
                    JsonSerializer.Serialize(document, SettingsJson)));

                foreach (string mediaFile in EnumerateMediaFiles(cancellationToken))
                {
                    string relative = Path.GetRelativePath(paths.MediaDirectory, mediaFile).Replace('\\', '/');

                    using FileStream media = File.OpenRead(mediaFile);

                    parts.Add(BackupArchive.AddPart(
                        archive,
                        BackupArchive.MediaEntryPrefix + relative,
                        media));
                }

                manifest = new BackupManifest
                {
                    FormatVersion = BackupArchive.FormatVersion,
                    AppVersion = ApplicationVersion.Current,
                    SchemaVersion = schemaVersion,
                    CreatedUtc = clock.UtcNow,
                    MachineName = Environment.MachineName,
                    IsEncrypted = false,
                    Parts = parts,
                };

                BackupArchive.WriteManifest(archive, manifest);
            }

            FileInfo written = new(archivePath);
            string sha256 = BackupArchive.ComputeFileSha256(archivePath);

            return new BackupCaptureResult(archivePath, written.Length, sha256, manifest);
        }
        catch (BackupCaptureException)
        {
            throw;
        }
        catch (IOException ex)
        {
            throw new BackupCaptureException("The backup could not be written to disk.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BackupCaptureException("The backup folder could not be written to.", ex);
        }
        catch (SqliteException ex)
        {
            throw new BackupCaptureException("The shop's database could not be copied.", ex);
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }
    }

    /// <summary>
    /// The media files that go into the archive. A file that is locked by another program is left
    /// out rather than failing the whole backup: the pictures can be taken again, and a missing
    /// picture must not cost the shop its ledger.
    /// </summary>
    private IReadOnlyList<string> EnumerateMediaFiles(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.MediaDirectory))
        {
            return [];
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(paths.MediaDirectory, "*", SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        Array.Sort(files, StringComparer.Ordinal);

        var usable = new List<string>(files.Length);

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (new FileInfo(file).Length > 0)
                {
                    usable.Add(file);
                }
            }
            catch (IOException)
            {
                // The file is busy; leave it out.
            }
            catch (UnauthorizedAccessException)
            {
                // The file cannot be read; leave it out.
            }
        }

        return usable;
    }

    private async Task<string> ReadSchemaVersionAsync(CancellationToken cancellationToken)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            IReadOnlyList<string> applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();

            return applied.Count == 0 ? string.Empty : applied[^1];
        }
        catch (SqliteException)
        {
            // A database that has never been migrated has no history to read.
            return string.Empty;
        }
    }

    private async Task<BackupSettingsDocument> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<Setting> rows = await context.Settings
            .AsNoTracking()
            .Where(setting =>
                setting.Key == DatabaseInitializer.StoreSettingsSettingKey ||
                setting.Key == DatabaseInitializer.StoreDetailsSettingKey)
            .ToListAsync(cancellationToken);

        StoreSettings settings = StoreSettings.Defaults;
        StoreDetails? details = null;

        foreach (Setting row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Value))
            {
                continue;
            }

            try
            {
                if (row.Key == DatabaseInitializer.StoreSettingsSettingKey)
                {
                    settings = JsonSerializer.Deserialize<StoreSettings>(row.Value) ?? StoreSettings.Defaults;
                }
                else
                {
                    details = JsonSerializer.Deserialize<StoreDetails>(row.Value);
                }
            }
            catch (JsonException)
            {
                // A settings row that cannot be read is written into the backup as it was found.
                // The restore keeps the database's own copy, which is the one that matters.
            }
        }

        return new BackupSettingsDocument(details, settings);
    }
}
