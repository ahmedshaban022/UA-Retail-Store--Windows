namespace StoreFlow.Infrastructure;

/// <summary>
/// The well-known locations the application uses. Everything writable lives under the user's
/// application-data folder, never beside the executable under Program Files (PRD §8.4).
/// </summary>
public sealed class AppPaths
{
    /// <summary>The folder name used under the application-data root.</summary>
    public const string ApplicationFolderName = "StoreFlow";

    /// <summary>
    /// The environment variable that points the application at a different data root. It exists so
    /// a portable installation, and a test run, can keep their data somewhere of their own choosing.
    /// </summary>
    public const string RootOverrideVariable = "STOREFLOW_HOME";

    /// <summary>Creates the paths rooted at the user's application-data folder.</summary>
    /// <param name="rootOverride">The data root to use, or null to use the default.</param>
    public AppPaths(string? rootOverride = null)
    {
        RootDirectory = rootOverride
                        ?? Environment.GetEnvironmentVariable(RootOverrideVariable)
                        ?? Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            ApplicationFolderName);

        DatabasePath = Path.Combine(RootDirectory, "data", "storeflow.db");
        ThemesDirectory = Path.Combine(RootDirectory, "themes");
        TokensDirectory = Path.Combine(RootDirectory, "tokens");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        MediaDirectory = Path.Combine(RootDirectory, "media");
        BackupsDirectory = Path.Combine(RootDirectory, "backups");
        RestorePointsDirectory = Path.Combine(RootDirectory, "data", "restore-points");
        BrandDirectory = Path.Combine(RootDirectory, "brand");
        DiagnosticsDirectory = Path.Combine(RootDirectory, "diagnostics");
    }

    /// <summary>The application-data root.</summary>
    public string RootDirectory { get; }

    /// <summary>The SQLite database file.</summary>
    public string DatabasePath { get; }

    /// <summary>Where theme files live.</summary>
    public string ThemesDirectory { get; }

    /// <summary>Where encrypted cloud tokens live.</summary>
    public string TokensDirectory { get; }

    /// <summary>Where rolling log files are written.</summary>
    public string LogsDirectory { get; }

    /// <summary>Where product images live.</summary>
    public string MediaDirectory { get; }

    /// <summary>Where local backups are written by default.</summary>
    public string BackupsDirectory { get; }

    /// <summary>
    /// Where the database is kept before it is replaced by a restore, so that a restore can be
    /// undone (PRD §8.5).
    /// </summary>
    public string RestorePointsDirectory { get; }

    /// <summary>Where the logo and other brand assets live.</summary>
    public string BrandDirectory { get; }

    /// <summary>Where diagnostic bundles are written.</summary>
    public string DiagnosticsDirectory { get; }

    /// <summary>Creates every directory the application writes to.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Directory.CreateDirectory(ThemesDirectory);
        Directory.CreateDirectory(TokensDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(MediaDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(RestorePointsDirectory);
        Directory.CreateDirectory(BrandDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }
}
