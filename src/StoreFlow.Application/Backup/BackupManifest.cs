namespace StoreFlow.Application.Backup;

/// <summary>
/// One part of a backup archive, with the checksum that proves the part has not changed since the
/// archive was written.
/// </summary>
public sealed record BackupPartEntry
{
    /// <summary>The path of the part inside the archive, always with forward slashes.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>The size of the part in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>The SHA-256 of the part, in lower-case hexadecimal.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>
/// What a backup archive says about itself: which application wrote it, which shape the data was,
/// when it was written, where it was written, and what it contains.
/// </summary>
/// <remarks>
/// The manifest is the part of the archive a person can read. It is written last, after every other
/// part has been hashed, so the checksums it carries always describe the bytes that are actually
/// in the archive.
/// </remarks>
public sealed record BackupManifest
{
    /// <summary>The version of the archive format itself.</summary>
    public int FormatVersion { get; init; }

    /// <summary>The version of the application that wrote the archive.</summary>
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>
    /// The schema of the data inside the archive, expressed as the identifier of the migration the
    /// database was on when it was captured.
    /// </summary>
    public string SchemaVersion { get; init; } = string.Empty;

    /// <summary>When the archive was written, in UTC.</summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>The machine that wrote the archive.</summary>
    public string MachineName { get; init; } = string.Empty;

    /// <summary>
    /// True when the archive's payload is encrypted. This build writes plain archives, so the flag
    /// is always false; it exists so that a future encrypted format can be told apart from a plain
    /// one instead of being guessed at.
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>Every part in the archive, except the manifest itself.</summary>
    public IReadOnlyList<BackupPartEntry> Parts { get; init; } = [];
}
