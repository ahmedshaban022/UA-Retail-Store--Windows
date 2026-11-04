using System.Diagnostics;
using System.Globalization;
using StoreFlow.Application.Updates;
using StoreFlow.Infrastructure.Updates;

namespace StoreFlow.UpdateProbe;

/// <summary>
/// Asks the updater's own service to install a published release, and reports what it did.
/// </summary>
/// <remarks>
/// The probe uses the programme's own service and the programme's own client, so that what it
/// measures is the programme and not a copy of it. The installer it is given is a stand-in: the
/// probe downloads and checks a release for real, and never installs anything.
/// </remarks>
internal static class Program
{
    /// <summary>Runs the probe.</summary>
    /// <returns>Zero when the probe ran, one when the release feed could not be read.</returns>
    private static async Task<int> Main()
    {
        var launcher = new RecordingLauncher();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StoreFlow-Desktop");

        var service = new GitHubReleaseUpdateService("1.0.0", client, launcher);

        Console.WriteLine("checking the feed...");

        UpdateCheckResult check = await service.CheckAsync(CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine("status:  " + check.Status);
        Console.WriteLine("latest:  " + (check.LatestVersion ?? "(none)"));
        Console.WriteLine("reason:  " + (check.Reason ?? "(none)"));

        if (!check.HasUpdate)
        {
            return 1;
        }

        var clock = Stopwatch.StartNew();

        Console.WriteLine("downloading...");

        UpdateInstallResult result = await service.InstallAsync(check, null, CancellationToken.None).ConfigureAwait(false);

        clock.Stop();

        Console.WriteLine("started: " + result.Started);
        Console.WriteLine("reason:  " + (result.Reason ?? "(none)"));
        Console.WriteLine(
            "elapsed: " + clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s");

        if (launcher.Path is not null)
        {
            var file = new FileInfo(launcher.Path);

            Console.WriteLine("the file:  " + file.FullName);
            Console.WriteLine("its size:  " + file.Length);
        }

        return 0;
    }
}

/// <summary>Stands in for the installer, so that the probe never installs anything.</summary>
internal sealed class RecordingLauncher : IInstallerLauncher
{
    /// <summary>The installer the service asked to start.</summary>
    public string? Path { get; private set; }

    /// <inheritdoc />
    public bool Start(string installerPath)
    {
        Path = installerPath;
        return true;
    }
}
