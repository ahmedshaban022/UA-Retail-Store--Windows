using System.Globalization;
using System.Net;
using System.Text;
using StoreFlow.Application.Abstractions;
using StoreFlow.Infrastructure.Backup;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The remote backup target, exercised against a local object store that speaks the same
/// protocol: the backup really leaves the machine, really lands somewhere else, and can really be
/// read back.
/// </summary>
/// <remarks>
/// The store under test is a small listener that answers the same requests an S3-compatible
/// service answers, so the test drives the real client, the real signing and the real request
/// path rather than a substitute.
/// </remarks>
public sealed class S3BackupTargetTests : IAsyncLifetime
{
    private const string Bucket = "shop-backups";

    private HttpListener _listener = null!;
    private CancellationTokenSource _cancellation = null!;
    private Task _loop = null!;
    private string _endpoint = string.Empty;

    private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _cancellation = new CancellationTokenSource();

        int port = FreePort();

        _endpoint = $"http://127.0.0.1:{port}";

        _listener = new HttpListener();
        _listener.Prefixes.Add(_endpoint + "/");
        _listener.Start();

        _loop = Task.Run(() => ListenAsync(_cancellation.Token));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _cancellation.CancelAsync();

        _listener.Stop();
        _listener.Close();

        try
        {
            await _loop;
        }
        catch (HttpListenerException)
        {
            // The listener was stopped while it was waiting; that is how it ends.
        }
        catch (OperationCanceledException)
        {
            // The same.
        }

        _cancellation.Dispose();
    }

    [Fact]
    public async Task A_backup_is_written_to_the_object_store_and_read_back()
    {
        using var target = new S3BackupTarget(new StubConfiguration(_endpoint));

        BackupTargetStatus status = await target.TestConnectionAsync();

        status.IsReachable.ShouldBeTrue(status.Detail);
        status.Message.Contains(Bucket, StringComparison.Ordinal).ShouldBeTrue();

        using (var content = new MemoryStream("the backup"u8.ToArray()))
        {
            BackupUploadResult upload = await target.UploadAsync("storeflow-manual.sfb", content);

            upload.RemoteId.ShouldBe("backups/storeflow-manual.sfb");
            upload.Uri.ShouldNotBeNullOrWhiteSpace();
        }

        // The object really left this process.
        lock (_gate)
        {
            _objects.ShouldContainKey("backups/storeflow-manual.sfb");
            _objects["backups/storeflow-manual.sfb"].Length.ShouldBe(10);
        }

        IReadOnlyList<RemoteBackupFile> files = await target.ListAsync();

        files.Count.ShouldBe(1);
        files[0].Name.ShouldBe("storeflow-manual.sfb");
        files[0].SizeBytes.ShouldBe(10);

        using var destination = new MemoryStream();

        await target.DownloadAsync(files[0].RemoteId, destination);

        Encoding.UTF8.GetString(destination.ToArray()).ShouldBe("the backup");

        await target.DeleteAsync(files[0].RemoteId);

        (await target.ListAsync()).Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_cloud_target_that_was_never_set_up_says_so()
    {
        using var target = new S3BackupTarget(new MissingConfiguration());

        BackupTargetStatus status = await target.TestConnectionAsync();

        status.IsReachable.ShouldBeFalse();
        status.Message.ShouldNotBeNullOrWhiteSpace();

        await Should.ThrowAsync<InvalidOperationException>(
            () => target.ListAsync());
    }

    [Fact]
    public async Task A_cloud_target_that_would_travel_in_the_open_is_refused()
    {
        // The backup file is not encrypted — the design for that is ADR-0005 and it is
        // deliberately not in this release — so the whole of the shop's ledger would cross the
        // network in the clear. A plain address is refused rather than quietly downgraded.
        using var target = new S3BackupTarget(new StubConfiguration("http://backup.example.com"));

        BackupTargetStatus status = await target.TestConnectionAsync();

        status.IsReachable.ShouldBeFalse();
        status.Message.ShouldContain("https", Case.Insensitive);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                await AnswerAsync(context);
            }
            catch (HttpListenerException)
            {
                return;
            }
        }
    }

    private async Task AnswerAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        // Every object store answers with these, and the client reads them.
        response.Headers["x-amz-request-id"] = Guid.NewGuid().ToString();
        response.Headers["x-amz-id-2"] = Guid.NewGuid().ToString();
        response.Headers["Server"] = "StoreFlowTestStore";
        response.Headers["Date"] = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);

        string path = request.Url!.AbsolutePath.TrimStart('/');

        string key = path.Contains('/', StringComparison.Ordinal)
            ? path[(path.IndexOf('/', StringComparison.Ordinal) + 1)..]
            : string.Empty;

        if (request.HttpMethod == "HEAD")
        {
            response.StatusCode = 200;
            response.Close();

            return;
        }

        if (request.HttpMethod == "PUT")
        {
            using var buffer = new MemoryStream();

            await request.InputStream.CopyToAsync(buffer);

            byte[] raw = buffer.ToArray();

            // The client signs the body as a stream, so what arrives on the wire is chunk-framed.
            // A real object store unframes it; so does this one.
            bool isChunked = request.Headers["Content-Encoding"]?.Contains("aws-chunked", StringComparison.OrdinalIgnoreCase) == true
                || request.Headers["x-amz-decoded-content-length"] is not null;

            byte[] stored = isChunked ? Unframe(raw) : raw;

            lock (_gate)
            {
                _objects[key] = stored;
            }

            response.Headers["ETag"] = "\"" + Convert.ToHexString(stored) + "\"";

            response.StatusCode = 200;
            response.Close();

            return;
        }

        if (request.HttpMethod == "DELETE")
        {
            lock (_gate)
            {
                _objects.Remove(key);
            }

            response.StatusCode = 204;
            response.Close();

            return;
        }

        if (request.HttpMethod == "GET" && key.Length == 0)
        {
            string xml = BuildListXml();


            byte[] bytes = Encoding.UTF8.GetBytes(xml);

            response.ContentType = "application/xml";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();

            return;
        }

        if (request.HttpMethod == "GET")
        {
            byte[]? bytes = null;

            lock (_gate)
            {
                _objects.TryGetValue(key, out bytes);
            }

            if (bytes is null)
            {
                response.StatusCode = 404;
                response.Close();

                return;
            }

            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();

            return;
        }

        response.StatusCode = 405;
        response.Close();
    }

    private string BuildListXml()
    {
        var builder = new StringBuilder();

        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.Append("<ListBucketResult>");
        builder.Append("<Name>").Append(Bucket).Append("</Name>");
        builder.Append("<Prefix>backups/</Prefix>");
        builder.Append("<KeyCount>").Append(_objects.Count).Append("</KeyCount>");
        builder.Append("<MaxKeys>1000</MaxKeys>");
        builder.Append("<IsTruncated>false</IsTruncated>");

        lock (_gate)
        {
            foreach (KeyValuePair<string, byte[]> item in _objects.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                builder.Append("<Contents>");
                builder.Append("<Key>").Append(item.Key).Append("</Key>");
                builder.Append("<LastModified>")
                    .Append(DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.000Z", CultureInfo.InvariantCulture))
                    .Append("</LastModified>");
                builder.Append("<ETag>\"etag\"</ETag>");
                builder.Append("<Size>").Append(item.Value.Length).Append("</Size>");
                builder.Append("<StorageClass>STANDARD</StorageClass>");
                builder.Append("<Owner><ID>shop</ID><DisplayName>shop</DisplayName></Owner>");
                builder.Append("</Contents>");
            }
        }

        builder.Append("</ListBucketResult>");

        return builder.ToString();
    }

    /// <summary>
    /// Takes the chunk framing off a streamed body: a hexadecimal size, the bytes, and a blank
    /// line, repeated until the size is zero.
    /// </summary>
    private static byte[] Unframe(byte[] raw)
    {
        var content = new List<byte>(raw.Length);

        int index = 0;

        while (index < raw.Length)
        {
            int lineEnd = -1;

            for (int scan = index; scan + 1 < raw.Length; scan++)
            {
                if (raw[scan] == (byte)'\r' && raw[scan + 1] == (byte)'\n')
                {
                    lineEnd = scan;
                    break;
                }
            }

            if (lineEnd < 0)
            {
                break;
            }

            string sizeLine = Encoding.ASCII.GetString(raw, index, lineEnd - index);

            string sizeText = sizeLine.Split(';')[0].Trim();

            if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int size))
            {
                break;
            }

            index = lineEnd + 2;

            if (size == 0)
            {
                break;
            }

            for (int taken = 0; taken < size && index < raw.Length; taken++)
            {
                content.Add(raw[index]);
                index++;
            }

            index += 2;
        }

        return [.. content];
    }

    private static int FreePort()
    {
        using System.Net.Sockets.TcpListener probe = new(IPAddress.Loopback, 0);

        probe.Start();

        int port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    /// <summary>A cloud target the shop has set up.</summary>
    private sealed class StubConfiguration(string endpoint) : ICloudBackupConfigurationProvider
    {
        public Task<CloudBackupConfiguration?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CloudBackupConfiguration?>(new CloudBackupConfiguration(
                endpoint,
                Bucket,
                "us-east-1",
                "backups",
                "shop-key",
                "shop-secret"));
    }

    /// <summary>A cloud target the shop has never set up.</summary>
    private sealed class MissingConfiguration : ICloudBackupConfigurationProvider
    {
        public Task<CloudBackupConfiguration?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CloudBackupConfiguration?>(null);
    }
}
