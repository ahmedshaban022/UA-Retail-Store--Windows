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

    /// <summary>
    /// A feed that answers "too many requests" is not the shop's problem, so it is not an error
    /// the shopkeeper is shown: the check simply finds nothing today.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_feed_that_is_rate_limited_or_unavailable_is_not_an_error(HttpStatusCode status)
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(status));
        GitHubReleaseUpdateService service = Service(handler, "1.0.0");

        UpdateCheckResult result = await service.CheckAsync(CancellationToken.None);

        result.Status.ShouldBe(UpdateStatus.Unavailable);
        result.HasUpdate.ShouldBeFalse();
        result.LatestVersion.ShouldBeNull();
    }

    /// <summary>
    /// A feed that cannot be reached at all is not an error either: the shop's programme carries
    /// on, and nothing is written anywhere.
    /// </summary>
    [Fact]
    public async Task A_feed_that_cannot_be_reached_at_all_is_not_an_error()
    {
        using var handler = new FakeHandler(_ => throw new HttpRequestException("The name could not be resolved."));
        GitHubReleaseUpdateService service = Service(handler, "1.0.0");

        UpdateCheckResult result = await service.CheckAsync(CancellationToken.None);

        result.Status.ShouldBe(UpdateStatus.Unavailable);
        result.HasUpdate.ShouldBeFalse();
    }

    /// <summary>
    /// A download that breaks in the middle starts no installer, leaves no half-written file
    /// behind, and says nothing the shop cannot act on.
    /// </summary>
    [Fact]
    public async Task A_download_that_breaks_in_the_middle_leaves_nothing_behind()
    {
        using var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            var url when url == ChecksumUrl => Text(new string('A', 64)),
            var url when url == InstallerUrl => Broken(Encoding.UTF8.GetBytes("half an installer")),
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

        // Nothing was installed, and the half-written download was thrown away.
        launcher.StartedPath.ShouldBeNull();
        Directory.GetFiles(scoped.Path, "*.msi", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    /// <summary>
    /// A download that is cancelled starts no installer and leaves nothing behind, so the shop
    /// can try again later without finding a broken file in the way.
    /// </summary>
    [Fact]
    public async Task A_download_that_is_cancelled_leaves_nothing_behind()
    {
        using var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            var url when url == ChecksumUrl => Text(new string('A', 64)),
            var url when url == InstallerUrl => Bytes(Encoding.UTF8.GetBytes("the installer bytes")),
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

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        UpdateInstallResult result = await service.InstallAsync(check, null, cancelled.Token);

        result.Started.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();
        launcher.StartedPath.ShouldBeNull();
        Directory.GetFiles(scoped.Path, "*.msi", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    /// <summary>
    /// An update never writes inside the shop's own data folder: the download is kept in the
    /// folder it was given, and the shop's ledger, catalogue and backups are never touched.
    /// </summary>
    [Fact]
    public async Task An_update_never_writes_inside_the_shop_data()
    {
        byte[] installer = Encoding.UTF8.GetBytes("the installer bytes");
        string checksum = Convert.ToHexString(SHA256.HashData(installer));

        using var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            var url when url == ChecksumUrl => Text(checksum),
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

        result.Started.ShouldBeTrue();

        // Everything the update wrote is under the folder it was given, and nothing else.
        Path.GetFullPath(launcher.StartedPath!).StartsWith(
            Path.GetFullPath(scoped.Path),
            StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
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

    [Fact]
    public async Task An_installer_the_shop_does_not_trust_is_never_run()
    {
        // The checksum proves the download arrived intact. It cannot prove who published it, because
        // the same publisher published the checksum beside the installer. When the installer's own
        // signature is not the one this build expects, the file is thrown away and nothing is run.
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

        var service = new GitHubReleaseUpdateService(
            "1.0.0",
            handler.Client,
            launcher,
            scoped.Path,
            new RefusingTrust());

        UpdateCheckResult check = await service.CheckAsync(CancellationToken.None);
        UpdateInstallResult result = await service.InstallAsync(check, null, CancellationToken.None);

        result.Started.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();

        // Nothing was run, and the file that was refused was thrown away.
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

    /// <summary>A download that delivers a few bytes and then breaks, the way a dropped connection does.</summary>
    private static HttpResponseMessage Broken(byte[] head) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new BreakingStream(head)) };

    /// <summary>A stream that hands over part of a download and then fails.</summary>
    private sealed class BreakingStream : Stream
    {
        private readonly byte[] _head;
        private int _read;

        public BreakingStream(byte[] head) => _head = head;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= _head.Length)
            {
                throw new IOException("The connection was closed before the download finished.");
            }

            int take = Math.Min(count, _head.Length - _read);
            Array.Copy(_head, _read, buffer, offset, take);
            _read += take;

            return take;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

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

    /// <summary>A trust check that refuses every installer, as one that does not know the publisher does.</summary>
    private sealed class RefusingTrust : IInstallerTrust
    {
        public bool IsTrusted(string installerPath, out string? problem)
        {
            problem = "the installer is not signed by the publisher";

            return false;
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
