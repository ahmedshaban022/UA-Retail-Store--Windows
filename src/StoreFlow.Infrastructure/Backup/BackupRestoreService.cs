using System.Globalization;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Pos;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// Puts a backup archive back where the shop reads it.
/// </summary>
/// <remarks>
/// <para>The order of the work matters. The archive is checked before anything is touched. The
/// database that is about to be replaced is copied aside, so that the restore can be undone. Only
/// then is the new database put in place, and only then are the pictures brought back.</para>
/// <para>A restore is refused while a sale is being posted: a sale that is half written and a
/// restore that lands at the same moment would lose money.</para>
/// </remarks>
public sealed class BackupRestoreService(
    AppPaths paths,
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    ISaleActivityMonitor saleMonitor,
    IClock clock) : IBackupRestoreService
{
    /// <inheritdoc />
    public async Task<RestoreOutcome> RestoreAsync(
        string archivePath,
        bool createRestorePoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        if (saleMonitor.IsSaleInProgress)
        {
            return new RestoreOutcome(
                false,
                null,
                "A sale is being posted right now, so the shop cannot be restored until it is finished.",
                false,
                []);
        }

        if (!File.Exists(archivePath))
        {
            return new RestoreOutcome(false, null, "The backup file could not be found.", false, []);
        }

        BackupArchiveInspection inspection = BackupArchive.Inspect(archivePath);

        if (!inspection.IsIntegrityOk || inspection.Manifest is null)
        {
            return new RestoreOutcome(
                false,
                null,
                inspection.IntegrityMessage ?? "The backup file is damaged and cannot be restored.",
                false,
                []);
        }

        try
        {
            await using (StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
            {
                (IReadOnlyList<string> known, string current) =
                    await BackupSchemaVersions.ReadAsync(context, cancellationToken);

                SchemaCompatibility compatibility = BackupSchemaCompatibility.Evaluate(
                    inspection.Manifest.SchemaVersion,
                    known,
                    current);

                if (!compatibility.IsCompatible)
                {
                    return new RestoreOutcome(
                        false,
                        null,
                        compatibility.Message ?? "This backup cannot be restored by this version of StoreFlow.",
                        false,
                        []);
                }
            }
        }
        catch (SqliteException)
        {
            // The shop's own database cannot be opened. That is the situation a recovery is for,
            // so the archive is judged on its own manifest and nothing else.
        }

        string? restorePoint = createRestorePoint
            ? WriteRestorePoint()
            : null;

        try
        {
            ReplaceDatabase(archivePath, cancellationToken);
        }
        catch (IOException ex)
        {
            return new RestoreOutcome(false, restorePoint, $"The shop's data could not be replaced: {ex.Message}", false, []);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new RestoreOutcome(false, restorePoint, $"The shop's data could not be replaced: {ex.Message}", false, []);
        }
        catch (SqliteException ex)
        {
            return new RestoreOutcome(false, restorePoint, $"The shop's data could not be replaced: {ex.Message}", false, []);
        }
        catch (InvalidDataException ex)
        {
            // The archive carries no database to restore. The shop's own data is untouched.
            return new RestoreOutcome(false, restorePoint, $"The shop's data could not be replaced: {ex.Message}", false, []);
        }

        // The pictures are restored after the database, because the database is the shop's data.
        // A picture that cannot be written must not undo a restore that has already worked, and
        // it must not throw out of the restore either: the shop was restored, and it has to be
        // told so.
        IReadOnlyList<string> removed = [];
        string? mediaProblem = null;

        try
        {
            removed = RestoreMedia(archivePath);
        }
        catch (IOException ex)
        {
            mediaProblem = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            mediaProblem = ex.Message;
        }
        catch (InvalidDataException ex)
        {
            mediaProblem = ex.Message;
        }

        string message = restorePoint is null
            ? "The shop was restored from the backup. StoreFlow will restart now."
            : "The shop was restored from the backup, and the data it replaced was kept as a restore " +
              "point. StoreFlow will restart now.";

        if (mediaProblem is not null)
        {
            message =
                "The shop was restored from the backup, but its pictures could not be restored: " +
                mediaProblem + " StoreFlow will restart now.";
        }

        return new RestoreOutcome(true, restorePoint, message, true, removed);
    }

    /// <summary>
    /// Copies the database that is about to be replaced into the restore-points folder, so that the
    /// restore can be undone. A database that cannot be copied — because it is damaged, which is
    /// exactly when a recovery is needed — does not stop the restore.
    /// </summary>
    /// <returns>The path of the restore point, or null when one could not be written.</returns>
    private string? WriteRestorePoint()
    {
        if (!File.Exists(paths.DatabasePath))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(paths.RestorePointsDirectory);

            string name = string.Create(
                CultureInfo.InvariantCulture,
                $"storeflow-restorepoint-{clock.UtcNow.UtcDateTime:yyyyMMdd-HHmmss}.db");

            string restorePoint = Path.Combine(paths.RestorePointsDirectory, name);

            SqliteSnapshot.Copy(paths.DatabasePath, restorePoint);

            return restorePoint;
        }
        catch (SqliteException)
        {
            // The shop's data is damaged, which is exactly when a recovery is needed. The restore
            // goes ahead without a restore point; there is nothing else that can be done.
            return null;
        }
        catch (IOException)
        {
            // The restore-points folder could not be written to. The restore goes ahead without a
            // restore point rather than failing the recovery it was meant to serve.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // The same: the folder exists, but this account may not write to it.
            return null;
        }
    }

    private void ReplaceDatabase(string archivePath, CancellationToken cancellationToken)
    {
        string staged = paths.DatabasePath + ".restore";

        try
        {
            using (ZipArchive archive = BackupArchive.OpenRead(archivePath))
            {
                ZipArchiveEntry entry = archive.GetEntry(BackupArchive.DatabaseEntryPath)
                    ?? throw new InvalidDataException("The backup file carries no database.");

                Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);

                using (Stream source = entry.Open())
                using (FileStream target = new(staged, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(target);
                }
            }

            // The shop's own connections must let go of the old file before it is replaced, or the
            // write-ahead log of the old database would be applied to the new one.
            SqliteConnection.ClearAllPools();

            DeleteIfPresent(paths.DatabasePath + "-wal");
            DeleteIfPresent(paths.DatabasePath + "-shm");

            // The restore is called off before the shop's data is touched, not after. A restore
            // that has already replaced the database cannot be taken back, so the cancellation is
            // asked for while the shop's own database is still the one on disk.
            cancellationToken.ThrowIfCancellationRequested();

            // The move is the last thing that happens to the shop's data. Windows puts the new
            // file in place in one step, so the shop's database is either the old one or the new
            // one and never a mixture of the two. The old one was copied to the restore points
            // before the swap, and that is how the owner undoes a restore that turned out to be
            // the wrong one.
            File.Move(staged, paths.DatabasePath, overwrite: true);
        }
        finally
        {
            // The staged copy is a whole shop's data lying beside the shop's data. When the move
            // did not happen it is deleted, so that a restore that failed leaves nothing behind.
            DeleteIfPresentQuietly(staged);
        }
    }

    private IReadOnlyList<string> RestoreMedia(string archivePath)
    {
        Directory.CreateDirectory(paths.MediaDirectory);

        var restored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();

        using (ZipArchive archive = BackupArchive.OpenRead(archivePath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(BackupArchive.MediaEntryPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = entry.FullName[BackupArchive.MediaEntryPrefix.Length..];

                if (!TryResolveInside(paths.MediaDirectory, relative, out string target))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using (Stream source = entry.Open())
                using (FileStream file = new(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(file);
                }

                restored.Add(relative);
            }
        }

        foreach (string existing in Directory.GetFiles(paths.MediaDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(paths.MediaDirectory, existing).Replace('\\', '/');

            if (restored.Contains(relative))
            {
                continue;
            }

            // The picture was taken after the backup was made, so the backup does not have it.
            // It goes, so that the shop looks the way the backup says it should.
            removed.Add(relative);
            DeleteIfPresent(existing);
        }

        return removed;
    }

    /// <summary>
    /// Works out where an archive entry belongs inside a folder, refusing any entry that would
    /// land outside it.
    /// </summary>
    private static bool TryResolveInside(string root, string relative, out string target)
    {
        target = string.Empty;

        if (string.IsNullOrWhiteSpace(relative))
        {
            return false;
        }

        string combined = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        target = combined;

        return true;
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Deletes a file, and does not mind when it cannot be deleted.
    /// </summary>
    /// <remarks>
    /// The staged copy of the shop's data is taken away after the restore. A file that will not
    /// be deleted — because something else has it open — is left where it is rather than turning
    /// a finished restore into a failure. It is a copy of data the shop already has, so nothing
    /// is lost, and the next restore writes over it.
    /// </remarks>
    private static void DeleteIfPresentQuietly(string path)
    {
        try
        {
            DeleteIfPresent(path);
        }
        catch (IOException)
        {
            // Something else has the file open. The next restore writes over it.
        }
        catch (UnauthorizedAccessException)
        {
            // This account may not delete it. The next restore writes over it.
        }
    }
}
