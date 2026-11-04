using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Shouldly;
using StoreFlow.Application.Updates;
using StoreFlow.Infrastructure.Updates;
using Xunit;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The update service against a feed that is answered by hand, so that the behaviour of every
/// branch is pinned without touching the internet.
/// </summary>
public sealed class GitHubReleaseUpdateServiceTests
{
    private const string InstallerUrl = "https://example.test/StoreFlow-1.0.1-x64.msi";
    private const string ChecksumUrl = "https://example.test/StoreFlow-1.0.1-x64.msi.sha256";

    [Fact]
    public async Task A_newer_release_is_offered_with_its_installer_and_checksum()
    {
        using var handler = new FakeHandler(_ => Json(Release("v1.0.1")));
        GitHubReleaseUpdateService service = Service(handler, "1.0.0");

        UpdateCheckResult result = await service.CheckAsync(CancellationToken.None);

        result.Status.ShouldBe(UpdateStatus.UpdateAvailable);
        result.HasUpdate.ShouldBeTrue();
        result.LatestVersion.ShouldBe("v1.0.1");
        result.RunningVersion.ShouldBe("1.0.0");
        result.InstallerUrl.ShouldBe(new Uri(InstallerUrl));
        result.ChecksumUrl.ShouldBe(new Uri(ChecksumUrl));
        handler.Requests.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    public async Task A_release_that_is_not_newer_is_not_offered(string tag)
    {
        using var handler = new FakeHandler(_ => Json(Release(tag)));
        GitHubReleaseUpdateService service = Service(handler, "1.0.0");

        UpdateCheckResult result = await service.CheckAsync(CancellationToken.None);

        result.Status.ShouldBe(UpdateStatus.UpToDate, result.Reason ?? "no reason given");
        result.HasUpdate.ShouldBeFalse();
        result.InstallerUrl.ShouldBeNull();
    }

    [Fact]
    public async Task A_release_without_an_installer_offers_nothing()
    {
        const string body = """
            { "tag_name": "v1.0.1", "assets": [] }
            """;

        using var handler = new FakeHandler(_ => Json(body));
        GitHubReleaseUpdateService service = Service(handler, "1.0.0");

        UpdateCheckResult result = await service.CheckAsync(CancellationToken.None);

        result.Status.ShouldBe(UpdateStatus.Unavailable);
        result.HasUpdate.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_feed_that_cannot_be_reached_is_not_an_error()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        GitHubReleaseUpdateService service = Service(handler, "1.0.0");

        UpdateCheckResult result = await service.CheckAsync(CancellationToken.None);

        result.Status.ShouldBe(UpdateStatus.Unavailable);
        result.HasUpdate.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_body_that_is_not_a_release_is_not_an_error()
    {
        using var handler = new FakeHandler(_ => Json("this is not json"));
        GitHubReleaseUpdateService service = Service(handler, "1.0.0");

        UpdateCheckResult result = await service.CheckAsync(CancellationToken.None);

        result.Status.ShouldBe(UpdateStatus.Unavailable);
        result.HasUpdate.ShouldBeFalse();
    }

    [Fact]
    public async Task A_download_that_matches_its_checksum_starts_the_installer()
    {
        byte[] installer = Encoding.UTF8.GetBytes("the installer bytes");
        string checksum = Convert.ToHexString(SHA256.HashData(installer));

        using var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            var url when url == ChecksumUrl => Text(checksum + " *StoreFlow-1.0.1-x64.msi"),
            var url when url == InstallerUrl => Bytes(installer),
            _ => Json(Release("v1.0.1")),
        });

        using var scoped = new TempFolder();

        var launcher = new RecordingLauncher();

        var withLauncher = new GitHubReleaseUpdateService(
            "1.0.0",
            handler.Client,
            launcher,
            scoped.Path);

        UpdateCheckResult check = await withLauncher.CheckAsync(CancellationToken.None);
        UpdateInstallResult result = await withLauncher.InstallAsync(check, null, CancellationToken.None);

        result.Started.ShouldBeTrue();
        launcher.StartedPath.ShouldNotBeNullOrWhiteSpace();
        File.Exists(launcher.StartedPath!).ShouldBeTrue();
        Path.GetFileName(launcher.StartedPath!).ShouldBe("StoreFlow-1.0.1-x64.msi");
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_is_refused()
    {
        byte[] installer = Encoding.UTF8.GetBytes("the installer bytes");

        using var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            var url when url == ChecksumUrl => Text(new string('A', 64)),
            var url when url == InstallerUrl => Bytes(installer),
            _ => Json(Release("v1.0.1")),
        });

        using var scoped = new TempFolder();

        var launcher = new RecordingLauncher();

        var service = new GitHubReleaseUpdateService(
            "1.0.0",
            handler.Client,
            launcher,
            scoped.Path);

        UpdateCheckResult check = await service.CheckAsync(CancellationToken.None);
        UpdateInstallResult result = await service.InstallAsync(check, null, CancellationToken.None);

        result.Started.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();

        // Nothing was installed, and the file that did not match was thrown away.
        launcher.StartedPath.ShouldBeNull();
        Directory.GetFiles(scoped.Path, "*.msi", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789", "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789  *StoreFlow.msi", "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("  0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  StoreFlow.msi", "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    public void The_checksum_is_read_out_of_the_text_published_beside_the_installer(string text, string expected)
    {
        GitHubReleaseUpdateService.ExtractChecksum(text).ShouldBe(expected);
    }

    [Fact]
    public void Text_that_holds_no_checksum_is_read_as_nothing()
    {
        GitHubReleaseUpdateService.ExtractChecksum("no hash here").ShouldBe(string.Empty);
        GitHubReleaseUpdateService.ExtractChecksum("abc123  *StoreFlow.msi").ShouldBe(string.Empty);
        GitHubReleaseUpdateService.ExtractChecksum(string.Empty).ShouldBe(string.Empty);
    }

    private static GitHubReleaseUpdateService Service(FakeHandler handler, string runningVersion)
    {
        return new GitHubReleaseUpdateService(runningVersion, handler.Client, new RecordingLauncher());
    }

    private static string Release(string tag) =>
        $$"""
          {
            "tag_name": "{{tag}}",
            "assets": [
              { "name": "StoreFlow-1.0.1-x64.msi", "browser_download_url": "{{InstallerUrl}}" },
              { "name": "StoreFlow-1.0.1-x64.msi.sha256", "browser_download_url": "{{ChecksumUrl}}" }
            ]
          }
          """;

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage Bytes(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    /// <summary>A handler that answers by hand, so no test touches the internet.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;

            // The client must be built on this handler, or it would reach the real internet.
            Client = new HttpClient(this, disposeHandler: false);
        }

        public List<Uri> Requests { get; } = [];

        public HttpClient Client { get; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);

            return Task.FromResult(_respond(request));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Client.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A launcher that records what it was asked to start, and starts nothing.</summary>
    private sealed class RecordingLauncher : IInstallerLauncher
    {
        public string? StartedPath { get; private set; }

        public bool Start(string installerPath)
        {
            StartedPath = installerPath;

            return true;
        }
    }

    /// <summary>A folder that is removed when the test is over.</summary>
    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "storeflow-update-test-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
