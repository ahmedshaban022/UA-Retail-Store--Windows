using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using StoreFlow.Application.Updates;

namespace StoreFlow.Infrastructure.Updates;

/// <summary>
/// Finds newer releases on GitHub and installs them.
/// </summary>
/// <remarks>
/// <para>The feed is the public GitHub Releases API and the request is anonymous: no token, no
/// account, no secret inside the programme. That is only possible while the repository is public,
/// and the consequence of privatising it is written down in <c>docs/UPDATER.md</c>.</para>
/// <para>The check never throws at the caller. This is an offline-first shop programme: a feed
/// that cannot be reached is simply "no update today", and the shop carries on.</para>
/// </remarks>
public sealed class GitHubReleaseUpdateService : IUpdateService
{
    /// <summary>The repository the releases are published from.</summary>
    public const string Repository = "ahmedshaban022/UA-Retail-Store--Windows";

    private static readonly Uri LatestReleaseUrl = new(
        "https://api.github.com/repos/" + Repository + "/releases/latest");

    private const string InstallerExtension = ".msi";
    private const string ChecksumExtension = ".sha256";
    private const int CopyBufferSize = 81920;

    private readonly string _runningVersion;
    private readonly HttpClient _client;
    private readonly IInstallerLauncher _launcher;
    private readonly IInstallerTrust _trust;
    private readonly string _downloadRoot;

    /// <summary>Creates the service for the running programme.</summary>
    /// <param name="runningVersion">The version of the running build.</param>
    public GitHubReleaseUpdateService(string runningVersion)
        : this(runningVersion, CreateClient(), new MsiexecInstallerLauncher())
    {
    }

    /// <summary>Creates the service with the parts it depends on.</summary>
    /// <param name="runningVersion">The version of the running build.</param>
    /// <param name="client">The client used to reach the release feed.</param>
    /// <param name="launcher">Starts the installer once a download has been checked.</param>
    /// <param name="downloadRoot">
    /// Where a downloaded installer is kept before it is started. The default is a folder under the
    /// machine's temporary directory, which is never the installation folder and never the shop's
    /// data folder.
    /// </param>
    public GitHubReleaseUpdateService(
        string runningVersion,
        HttpClient client,
        IInstallerLauncher launcher,
        string? downloadRoot = null,
        IInstallerTrust? trust = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runningVersion);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(launcher);

        _runningVersion = runningVersion;
        _client = client;
        _launcher = launcher;
        _trust = trust ?? new AuthenticodeInstallerTrust();
        _downloadRoot = downloadRoot ?? Path.Combine(Path.GetTempPath(), "StoreFlow");
    }

    /// <inheritdoc />
    public string RunningVersion => _runningVersion;

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response =
                await _client.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Unavailable(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "the release feed answered {0}",
                        (int)response.StatusCode));
            }

            await using Stream body = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            ReleasePayload? payload = await JsonSerializer
                .DeserializeAsync<ReleasePayload>(body, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (payload is null || string.IsNullOrWhiteSpace(payload.TagName))
            {
                return Unavailable("the release feed did not name a version");
            }

            if (!UpdateVersion.TryParse(payload.TagName, out Version? latest))
            {
                return new UpdateCheckResult(
                    _runningVersion,
                    payload.TagName,
                    UpdateStatus.Unavailable,
                    Reason: string.Format(
                        CultureInfo.InvariantCulture,
                        "the release '{0}' is not a version this build can compare",
                        payload.TagName));
            }

            if (!UpdateVersion.IsNewer(payload.TagName, _runningVersion))
            {
                return new UpdateCheckResult(_runningVersion, payload.TagName, UpdateStatus.UpToDate);
            }

            ReleaseAsset? installer = Find(payload, asset =>
                asset.Name.EndsWith(InstallerExtension, StringComparison.OrdinalIgnoreCase));

            ReleaseAsset? checksum = Find(payload, asset =>
                asset.Name.EndsWith(
                    InstallerExtension + ChecksumExtension,
                    StringComparison.OrdinalIgnoreCase));

            if (installer is null ||
                checksum is null ||
                !Uri.TryCreate(installer.DownloadUrl, UriKind.Absolute, out Uri? installerUrl) ||
                !Uri.TryCreate(checksum.DownloadUrl, UriKind.Absolute, out Uri? checksumUrl))
            {
                return new UpdateCheckResult(
                    _runningVersion,
                    payload.TagName,
                    UpdateStatus.Unavailable,
                    Reason: "the published release carries no installer to install");
            }

            return new UpdateCheckResult(
                _runningVersion,
                payload.TagName,
                UpdateStatus.UpdateAvailable,
                installerUrl,
                checksumUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The feed did not answer in time. This is not the shop's problem, so it is not an
            // error the shopkeeper is shown.
            return Unavailable("the release feed did not answer in time");
        }
        catch (HttpRequestException ex)
        {
            return Unavailable(ex.Message);
        }
        catch (JsonException ex)
        {
            return Unavailable(ex.Message);
        }
        catch (IOException ex)
        {
            return Unavailable(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<UpdateInstallResult> InstallAsync(
        UpdateCheckResult update,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.InstallerUrl is null || update.ChecksumUrl is null)
        {
            return new UpdateInstallResult(false, "there is no installer to install");
        }

        string folder = Path.Combine(
            _downloadRoot,
            "update-" + SafeFolderName(update.LatestVersion));

        string target = Path.Combine(folder, FileNameFrom(update.InstallerUrl));

        try
        {
            Directory.CreateDirectory(folder);

            await DownloadAsync(update.InstallerUrl, target, progress, cancellationToken)
                .ConfigureAwait(false);

            string expected = await ReadChecksumAsync(update.ChecksumUrl, cancellationToken)
                .ConfigureAwait(false);

            string actual = await ComputeChecksumAsync(target, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                // The download does not match what was published beside it. It is discarded and the
                // installation is left exactly as it was.
                TryDelete(target);

                return new UpdateInstallResult(
                    false,
                    "the download did not match the checksum published with it");
            }

            // The checksum proves the download arrived intact. It cannot prove who published it,
            // because the checksum was published beside the installer, in the same release. The
            // installer's own signature is the publisher's proof, so it is checked before the
            // installer is run.
            if (!_trust.IsTrusted(target, out string? problem))
            {
                TryDelete(target);

                return new UpdateInstallResult(false, problem ?? "the installer is not trusted");
            }

            if (!_launcher.Start(target))
            {
                TryDelete(target);

                return new UpdateInstallResult(false, "the installer could not be started");
            }

            return new UpdateInstallResult(true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(target);

            return new UpdateInstallResult(false, "the download was interrupted");
        }
        catch (HttpRequestException ex)
        {
            TryDelete(target);

            return new UpdateInstallResult(false, ex.Message);
        }
        catch (IOException ex)
        {
            TryDelete(target);

            return new UpdateInstallResult(false, ex.Message);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // The GitHub API refuses a request that does not say who is asking.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StoreFlow-Desktop");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        return client;
    }

    private UpdateCheckResult Unavailable(string reason) =>
        new(_runningVersion, null, UpdateStatus.Unavailable, Reason: reason);

    private static ReleaseAsset? Find(ReleasePayload payload, Func<ReleaseAsset, bool> match)
    {
        if (payload.Assets is null)
        {
            return null;
        }

        foreach (ReleaseAsset asset in payload.Assets)
        {
            if (match(asset))
            {
                return asset;
            }
        }

        return null;
    }

    private async Task DownloadAsync(
        Uri source,
        string target,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client
            .GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        await using Stream input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var output = new FileStream(
            target,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            useAsync: true);

        var buffer = new byte[CopyBufferSize];
        long written = 0;
        int read;

        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            written += read;

            if (progress is not null && total is > 0)
            {
                int percent = (int)Math.Min(100, written * 100 / total.Value);

                progress.Report(percent);
            }
        }

        progress?.Report(100);
    }

    private async Task<string> ReadChecksumAsync(Uri source, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client
            .GetAsync(source, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return ExtractChecksum(text);
    }

    /// <summary>
    /// Reads the checksum out of the text published beside an installer. The file holds the hash
    /// and the file's name, separated by whitespace; only the hash is wanted.
    /// </summary>
    /// <param name="text">The published text.</param>
    /// <returns>The checksum in upper case, or an empty string when the text holds none.</returns>
    public static string ExtractChecksum(string text)
    {
        string[] parts = (text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string part in parts)
        {
            string candidate = part.TrimStart('*');

            if (candidate.Length == 64 && candidate.All(Uri.IsHexDigit))
            {
                return candidate.ToUpperInvariant();
            }
        }

        return string.Empty;
    }

    private static async Task<string> ComputeChecksumAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            useAsync: true);

        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexString(hash);
    }

    private static string FileNameFrom(Uri url)
    {
        string name = Path.GetFileName(Uri.UnescapeDataString(url.Segments.Length > 0 ? url.Segments[^1] : string.Empty));

        return string.IsNullOrWhiteSpace(name) ? "update.msi" : name;
    }

    private static string SafeFolderName(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "unknown";
        }

        char[] invalid = Path.GetInvalidFileNameChars();

        return new string(version.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A file that cannot be removed is not a reason to fail the update. The installer
            // would refuse it anyway, because the checksum did not match.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The part of a GitHub release this service reads.</summary>
    private sealed class ReleasePayload
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<ReleaseAsset>? Assets { get; set; }
    }

    /// <summary>One file published with a release.</summary>
    private sealed class ReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = string.Empty;
    }
}

/// <summary>
/// Starts the installer of a downloaded release, for the current user, once the programme has
/// closed.
/// </summary>
/// <remarks>
/// <para>
/// The install is per-user, like the installer the shop already uses: a till does not have an
/// administrator account, and an update that needs one would never be applied.
/// </para>
/// <para>
/// The installer is started by a helper that waits for this programme to close first. The
/// installer cannot replace the programme's files while the programme is holding them open, and
/// an installer that starts too early is quietly deferred to the next reboot by Windows
/// Installer: the shop would be told the update was applied when it had not been. The helper
/// waits, and the installer replaces the files while nothing is holding them.
/// </para>
/// </remarks>
public sealed class MsiexecInstallerLauncher : IInstallerLauncher
{
    /// <summary>How long the helper waits for the programme to close, in seconds.</summary>
    public const int WaitSeconds = 120;

    private readonly string _processToWaitFor;

    /// <summary>Creates the launcher.</summary>
    /// <param name="processToWaitFor">
    /// The name of the process the installer must wait for. The default is this programme: the
    /// installer cannot replace the programme's files while the programme is holding them open.
    /// </param>
    public MsiexecInstallerLauncher(string? processToWaitFor = null)
    {
        _processToWaitFor = string.IsNullOrWhiteSpace(processToWaitFor)
            ? System.Diagnostics.Process.GetCurrentProcess().ProcessName
            : processToWaitFor;
    }

    /// <inheritdoc />
    public bool Start(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        string msiexec = Path.Combine(Environment.SystemDirectory, "msiexec.exe");

        if (!File.Exists(msiexec))
        {
            return false;
        }

        string script = Path.Combine(
            Path.GetDirectoryName(installerPath) ?? Path.GetTempPath(),
            Path.GetFileNameWithoutExtension(installerPath) + ".cmd");

        try
        {
            File.WriteAllText(script, BuildScript(msiexec, installerPath));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = string.Format(CultureInfo.InvariantCulture, "/c \"\"{0}\"\"", script),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            return Process.Start(info) is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The script the helper runs: it waits for the programme to close, and only then starts the
    /// installer.
    /// </summary>
    /// <param name="msiexec">The Windows installer.</param>
    /// <param name="installerPath">The installer that was downloaded.</param>
    /// <returns>The script.</returns>
    public string BuildScript(string msiexec, string installerPath) =>
        string.Join(
            Environment.NewLine,
            "@echo off",
            string.Format(
                CultureInfo.InvariantCulture,
                "waitfor /t {0} {1}",
                WaitSeconds,
                _processToWaitFor),
            string.Format(
                CultureInfo.InvariantCulture,
                "\"{0}\" /i \"{1}\" /qb /norestart",
                msiexec,
                installerPath),
            "del \"%~f0\"");
}
