using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Application.Backup;

/// <summary>The codes the backup flow answers with.</summary>
public static class BackupErrorCodes
{
    /// <summary>The backup file could not be found.</summary>
    public const string NotFound = "backup.not_found";

    /// <summary>The backup file does not match its own checksum.</summary>
    public const string IntegrityFailed = "backup.integrity_failed";

    /// <summary>The backup was made by a version of the shop that this build cannot restore.</summary>
    public const string SchemaMismatch = "backup.schema_mismatch";

    /// <summary>The target could not be reached.</summary>
    public const string TargetUnreachable = "backup.target_unreachable";

    /// <summary>The store could not be captured.</summary>
    public const string CaptureFailed = "backup.capture_failed";

    /// <summary>The backup could not be written to the target.</summary>
    public const string UploadFailed = "backup.upload_failed";

    /// <summary>A sale is being posted, so the store may not be replaced right now.</summary>
    public const string RestoreBusy = "backup.restore_busy";

    /// <summary>The store could not be restored.</summary>
    public const string RestoreFailed = "backup.restore_failed";

    /// <summary>The folder chosen for backups cannot be written to.</summary>
    public const string DirectoryUnusable = "backup.directory_unusable";

    /// <summary>The target has not been set up yet.</summary>
    public const string TargetNotConfigured = "backup.target_not_configured";

    /// <summary>The backup file is not one this application wrote.</summary>
    public const string NotABackup = "backup.not_a_backup";
}
