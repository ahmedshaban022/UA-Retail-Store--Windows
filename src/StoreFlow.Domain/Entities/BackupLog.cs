using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Domain.Entities;

/// <summary>
/// The outcome of a backup or restore attempt, with the checksum that proves the artifact is
/// intact (PRD §8.5).
/// </summary>
public sealed class BackupLog
{
    public long Id { get; set; }

    /// <summary>The kind of backup target, for example <c>GoogleDrive</c>.</summary>
    public string TargetKind { get; set; } = string.Empty;

    /// <summary>The destination path or remote identifier.</summary>
    public string? Path { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>The SHA-256 of the produced artifact.</summary>
    public string? Sha256 { get; set; }

    public long SizeBytes { get; set; }

    public BackupResult Result { get; set; }

    /// <summary>The reason a backup failed, or a note on what it covered.</summary>
    public string? Message { get; set; }
}
