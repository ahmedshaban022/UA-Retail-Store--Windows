namespace StoreFlow.Application.Updates;

/// <summary>What a check for a newer release found.</summary>
public enum UpdateStatus
{
    /// <summary>The running build is the newest one that has been published.</summary>
    UpToDate,

    /// <summary>A newer release has been published and can be installed.</summary>
    UpdateAvailable,

    /// <summary>
    /// No update could be offered: the release feed could not be reached, or the release that was
    /// found carries no installer.
    /// </summary>
    Unavailable,
}

/// <summary>The outcome of a check for a newer release.</summary>
/// <param name="RunningVersion">The version of the running build.</param>
/// <param name="LatestVersion">The version of the latest published release, when one was found.</param>
/// <param name="Status">What the check found.</param>
/// <param name="InstallerUrl">Where the installer of the newer release can be downloaded.</param>
/// <param name="ChecksumUrl">
/// Where the checksum published beside that installer can be downloaded. The two travel together:
/// the checksum is what proves the download arrived intact.
/// </param>
/// <param name="Reason">Why no update is offered, in a form that can be logged.</param>
public sealed record UpdateCheckResult(
    string RunningVersion,
    string? LatestVersion,
    UpdateStatus Status,
    Uri? InstallerUrl = null,
    Uri? ChecksumUrl = null,
    string? Reason = null)
{
    /// <summary>True when there is something to install.</summary>
    public bool HasUpdate => Status == UpdateStatus.UpdateAvailable;
}

/// <summary>The outcome of an attempt to install a newer release.</summary>
/// <param name="Started">True when the installer was started.</param>
/// <param name="Reason">Why it could not be started, in a form that can be shown to the shopkeeper.</param>
public sealed record UpdateInstallResult(bool Started, string? Reason = null);

/// <summary>
/// Finds a newer release and installs it.
/// </summary>
/// <remarks>
/// The update is always the shopkeeper's decision. This port never installs anything on its own:
/// it is called when the shopkeeper asks for the update, and it reports back what happened.
/// </remarks>
public interface IUpdateService
{
    /// <summary>The version of the running build, which every check is compared against.</summary>
    string RunningVersion { get; }

    /// <summary>Looks for a newer release.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// The outcome. A feed that cannot be reached is reported as
    /// <see cref="UpdateStatus.Unavailable"/>, never as an exception.
    /// </returns>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the installer of a newer release, checks it against the checksum published beside
    /// it, and starts the installer for the current user.
    /// </summary>
    /// <param name="update">The check that found the update.</param>
    /// <param name="progress">Reports the download's progress as a percentage.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>Whether the installer was started, and why not when it was not.</returns>
    Task<UpdateInstallResult> InstallAsync(
        UpdateCheckResult update,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Starts the installer of a downloaded release.</summary>
public interface IInstallerLauncher
{
    /// <summary>Starts the installer and waits for it to be handed over.</summary>
    /// <param name="installerPath">The installer that was downloaded.</param>
    /// <returns>True when the installer was started.</returns>
    bool Start(string installerPath);
}
