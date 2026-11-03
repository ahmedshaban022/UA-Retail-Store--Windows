using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StoreFlow.Application.Backup;

/// <summary>
/// What an inspection of an archive found: the manifest it claims, and whether the bytes in the
/// file still agree with it.
/// </summary>
/// <param name="Manifest">The manifest the archive carries, or null when it carries none.</param>
/// <param name="IsIntegrityOk">True when every part matches the checksum the manifest records.</param>
/// <param name="IntegrityMessage">Why the integrity check failed, when it failed.</param>
/// <param name="ArchiveSha256">The SHA-256 of the whole archive file.</param>
/// <param name="ArchiveSizeBytes">The size of the whole archive file.</param>
/// <param name="MismatchedParts">The paths of the parts that did not match.</param>
public sealed record BackupArchiveInspection(
    BackupManifest? Manifest,
    bool IsIntegrityOk,
    string? IntegrityMessage,
    string ArchiveSha256,
    long ArchiveSizeBytes,
    IReadOnlyList<string> MismatchedParts);

/// <summary>
/// The <c>.sfb</c> container: a zip archive holding the store's data, its media and a manifest that
/// says what it all is.
/// </summary>
/// <remarks>
/// <para>A zip was chosen because every Windows PC can open one, because the format is standard
/// rather than bespoke, and because the archive can be listed and inspected with tools the shop
/// already has.</para>
/// <para>The manifest is written last, after every other part has been hashed, and it lists the
/// SHA-256 of every part. A restore recomputes those hashes and refuses an archive whose bytes no
/// longer agree with its own manifest. An archive that carries a part the manifest does not list is
/// refused as well: an archive that has grown a part nobody wrote is an archive that has been
/// changed.</para>
/// </remarks>
public static class BackupArchive
{
    /// <summary>The extension every backup file carries.</summary>
    public const string FileExtension = ".sfb";

    /// <summary>The version of the archive format this build writes.</summary>
    public const int FormatVersion = 1;

    /// <summary>The path of the manifest inside the archive.</summary>
    public const string ManifestEntryPath = "manifest.json";

    /// <summary>The path of the captured database inside the archive.</summary>
    public const string DatabaseEntryPath = "database/storeflow.db";

    /// <summary>The path of the store settings inside the archive.</summary>
    public const string SettingsEntryPath = "settings/store-settings.json";

    /// <summary>The prefix every media file carries inside the archive.</summary>
    public const string MediaEntryPrefix = "media/";

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Reads the manifest of an archive without checking it. Used by the writer, which builds the
    /// manifest itself.
    /// </summary>
    /// <param name="archivePath">The archive to read.</param>
    /// <returns>The manifest, or null when the archive carries none.</returns>
    public static BackupManifest? ReadManifest(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        using ZipArchive archive = ZipFile.OpenRead(archivePath);

        ZipArchiveEntry? entry = archive.GetEntry(ManifestEntryPath);

        if (entry is null)
        {
            return null;
        }

        using Stream stream = entry.Open();

        return JsonSerializer.Deserialize<BackupManifest>(stream, ManifestJson);
    }

    /// <summary>
    /// Opens an archive for reading.
    /// </summary>
    /// <param name="archivePath">The archive to open.</param>
    /// <returns>The open archive; the caller disposes it.</returns>
    public static ZipArchive OpenRead(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        return ZipFile.OpenRead(archivePath);
    }

    /// <summary>
    /// Reads an archive and checks that every part still matches the checksum its manifest records.
    /// </summary>
    /// <param name="archivePath">The archive to inspect.</param>
    /// <returns>What the inspection found.</returns>
    public static BackupArchiveInspection Inspect(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        FileInfo file = new(archivePath);
        string archiveHash = ComputeFileSha256(archivePath);

        BackupManifest? manifest;

        try
        {
            manifest = ReadManifest(archivePath);
        }
        catch (InvalidDataException)
        {
            return new BackupArchiveInspection(
                null,
                false,
                "The backup file is not a readable archive.",
                archiveHash,
                file.Exists ? file.Length : 0,
                []);
        }
        catch (JsonException)
        {
            return new BackupArchiveInspection(
                null,
                false,
                "The backup file's manifest cannot be read.",
                archiveHash,
                file.Exists ? file.Length : 0,
                []);
        }

        if (manifest is null)
        {
            return new BackupArchiveInspection(
                null,
                false,
                "The backup file carries no manifest, so nothing can be checked.",
                archiveHash,
                file.Length,
                []);
        }

        using ZipArchive archive = ZipFile.OpenRead(archivePath);

        var mismatched = new List<string>();

        foreach (BackupPartEntry part in manifest.Parts)
        {
            ZipArchiveEntry? entry = archive.GetEntry(part.Path);

            if (entry is null)
            {
                mismatched.Add(part.Path);
                continue;
            }

            using Stream stream = entry.Open();
            string actual = ComputeSha256(stream);

            if (!string.Equals(actual, part.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                mismatched.Add(part.Path);
            }
        }

        // A part the manifest does not list is a part somebody added. An archive that has been
        // changed is refused, whether the change was made to a part or by adding one.
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, ManifestEntryPath, StringComparison.Ordinal))
            {
                continue;
            }

            bool listed = manifest.Parts.Any(part => string.Equals(part.Path, entry.FullName, StringComparison.Ordinal));

            if (!listed)
            {
                mismatched.Add(entry.FullName);
            }
        }

        bool ok = mismatched.Count == 0;

        string? message = ok
            ? null
            : $"The backup file does not match its own checksum ({mismatched.Count} part(s) differ). " +
              "It may be damaged or changed since it was written.";

        return new BackupArchiveInspection(manifest, ok, message, archiveHash, file.Length, mismatched);
    }

    /// <summary>
    /// Computes the SHA-256 of a file, in lower-case hexadecimal.
    /// </summary>
    /// <param name="path">The file to hash.</param>
    /// <returns>The hash.</returns>
    public static string ComputeFileSha256(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using FileStream stream = File.OpenRead(path);

        return ComputeSha256(stream);
    }

    /// <summary>
    /// Computes the SHA-256 of a stream, in lower-case hexadecimal. The stream is read to its end.
    /// </summary>
    /// <param name="stream">The stream to hash.</param>
    /// <returns>The hash.</returns>
    public static string ComputeSha256(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using SHA256 sha = SHA256.Create();

        byte[] hash = sha.ComputeHash(stream);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Writes the manifest into an archive.
    /// </summary>
    /// <param name="archive">The archive being written.</param>
    /// <param name="manifest">The manifest to write.</param>
    public static void WriteManifest(ZipArchive archive, BackupManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(manifest);

        ZipArchiveEntry entry = archive.CreateEntry(ManifestEntryPath, CompressionLevel.Optimal);

        using Stream stream = entry.Open();

        JsonSerializer.Serialize(stream, manifest, ManifestJson);
    }

    /// <summary>
    /// Copies a part into an archive, hashing it as it goes.
    /// </summary>
    /// <param name="archive">The archive being written.</param>
    /// <param name="path">The path of the part inside the archive.</param>
    /// <param name="content">The content of the part.</param>
    /// <returns>The entry the manifest records for the part.</returns>
    public static BackupPartEntry AddPart(ZipArchive archive, string path, Stream content)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);

        using (Stream target = entry.Open())
        {
            using SHA256 sha = SHA256.Create();
            byte[] buffer = new byte[81920];
            long written = 0;

            int read;

            while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                target.Write(buffer, 0, read);
                written += read;
            }

            sha.TransformFinalBlock([], 0, 0);

            string hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();

            return new BackupPartEntry { Path = path, SizeBytes = written, Sha256 = hash };
        }
    }

    /// <summary>
    /// Adds a part made of bytes, hashing it as it goes.
    /// </summary>
    /// <param name="archive">The archive being written.</param>
    /// <param name="path">The path of the part inside the archive.</param>
    /// <param name="content">The content of the part.</param>
    /// <returns>The entry the manifest records for the part.</returns>
    public static BackupPartEntry AddPart(ZipArchive archive, string path, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        using MemoryStream stream = new(content, writable: false);

        return AddPart(archive, path, stream);
    }

    /// <summary>
    /// Adds a part made of text, written as UTF-8.
    /// </summary>
    /// <param name="archive">The archive being written.</param>
    /// <param name="path">The path of the part inside the archive.</param>
    /// <param name="content">The content of the part.</param>
    /// <returns>The entry the manifest records for the part.</returns>
    public static BackupPartEntry AddPart(ZipArchive archive, string path, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return AddPart(archive, path, Encoding.UTF8.GetBytes(content));
    }
}
