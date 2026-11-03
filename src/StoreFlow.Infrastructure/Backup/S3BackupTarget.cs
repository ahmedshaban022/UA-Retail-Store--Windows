using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using StoreFlow.Application.Abstractions;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// A backup target that is an S3-compatible object store: Amazon S3, or any service that speaks
/// the same protocol, such as MinIO, Cloudflare R2 or Wasabi.
/// </summary>
/// <remarks>
/// <para>An object store was chosen for the remote target because it needs no consent screen and
/// no application registration with a consumer provider: the shop is given an address, a bucket
/// and a key, and that is the whole set-up. The same code therefore reaches a private object
/// store in a back office as well as a public cloud one.</para>
/// <para>The key is signed with the shop's own credentials, which are kept in the Windows Data
/// Protection vault and are never written to the database.</para>
/// </remarks>
public sealed class S3BackupTarget : IBackupTarget, IDisposable
{
    /// <summary>The kind this target answers to.</summary>
    public const string KindName = "S3";

    private readonly ICloudBackupConfigurationProvider _configurationProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AmazonS3Client? _client;
    private bool _disposed;

    /// <summary>Creates the target.</summary>
    /// <param name="configurationProvider">Reads the shop's cloud settings.</param>
    public S3BackupTarget(ICloudBackupConfigurationProvider configurationProvider)
    {
        ArgumentNullException.ThrowIfNull(configurationProvider);

        _configurationProvider = configurationProvider;
    }

    /// <inheritdoc />
    public string Kind => KindName;

    /// <inheritdoc />
    public string DisplayName => "S3-compatible object store";

    /// <inheritdoc />
    public async Task<BackupTargetStatus> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        CloudBackupConfiguration? configuration = await _configurationProvider.GetAsync(cancellationToken);

        if (configuration is null)
        {
            return new BackupTargetStatus(false, "The cloud target has not been set up yet.");
        }

        try
        {
            AmazonS3Client client = await GetClientAsync(configuration, cancellationToken);

            await client.HeadBucketAsync(new HeadBucketRequest { BucketName = configuration.Bucket }, cancellationToken);

            return new BackupTargetStatus(
                true,
                $"Ready. Backups are written to {configuration.Bucket} at {configuration.Endpoint}.");
        }
        catch (AmazonS3Exception ex)
        {
            return new BackupTargetStatus(false, "The cloud target could not be reached.", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new BackupTargetStatus(false, "The cloud target could not be reached.", ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            return new BackupTargetStatus(false, "The cloud target did not answer in time.", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<BackupUploadResult> UploadAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        CloudBackupConfiguration configuration = await RequireConfigurationAsync(cancellationToken);
        AmazonS3Client client = await GetClientAsync(configuration, cancellationToken);

        string key = BuildKey(configuration, fileName);

        var request = new PutObjectRequest
        {
            BucketName = configuration.Bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
        };

        await client.PutObjectAsync(request, cancellationToken);

        return new BackupUploadResult(key, $"{configuration.Endpoint}/{configuration.Bucket}/{key}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemoteBackupFile>> ListAsync(CancellationToken cancellationToken = default)
    {
        CloudBackupConfiguration configuration = await RequireConfigurationAsync(cancellationToken);
        AmazonS3Client client = await GetClientAsync(configuration, cancellationToken);

        var files = new List<RemoteBackupFile>();
        string? continuation = null;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = configuration.Bucket,
                Prefix = NormalisePrefix(configuration.Prefix),
                ContinuationToken = continuation,
            };

            ListObjectsV2Response response = await client.ListObjectsV2Async(request, cancellationToken);

            // From the fourth version of the client onwards, the list of objects is null rather
            // than empty when the store has nothing to say. A bucket with no backups in it is
            // an ordinary state, not a fault.
            foreach (S3Object item in response.S3Objects ?? [])
            {
                files.Add(new RemoteBackupFile(
                    item.Key,
                    Path.GetFileName(item.Key),
                    item.Size ?? 0,
                    new DateTimeOffset(item.LastModified ?? DateTime.UnixEpoch, TimeSpan.Zero)));
            }

            continuation = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (!string.IsNullOrEmpty(continuation));

        return files;
    }

    /// <inheritdoc />
    public async Task DownloadAsync(string remoteId, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);
        ArgumentNullException.ThrowIfNull(destination);

        CloudBackupConfiguration configuration = await RequireConfigurationAsync(cancellationToken);
        AmazonS3Client client = await GetClientAsync(configuration, cancellationToken);

        using GetObjectResponse response = await client.GetObjectAsync(
            new GetObjectRequest { BucketName = configuration.Bucket, Key = remoteId },
            cancellationToken);

        await response.ResponseStream.CopyToAsync(destination, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);

        CloudBackupConfiguration configuration = await RequireConfigurationAsync(cancellationToken);
        AmazonS3Client client = await GetClientAsync(configuration, cancellationToken);

        await client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = configuration.Bucket, Key = remoteId },
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _client?.Dispose();
        _gate.Dispose();
    }

    private async Task<CloudBackupConfiguration> RequireConfigurationAsync(CancellationToken cancellationToken)
    {
        CloudBackupConfiguration? configuration = await _configurationProvider.GetAsync(cancellationToken);

        return configuration ?? throw new InvalidOperationException(
            "The cloud target has not been set up yet.");
    }

    private async Task<AmazonS3Client> GetClientAsync(
        CloudBackupConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var clientConfig = new AmazonS3Config
            {
                ServiceURL = configuration.Endpoint,
                AuthenticationRegion = string.IsNullOrWhiteSpace(configuration.Region)
                    ? "us-east-1"
                    : configuration.Region,
                UseHttp = configuration.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
                ForcePathStyle = true,
                Timeout = TimeSpan.FromMinutes(10),
            };

            var credentials = new BasicAWSCredentials(configuration.AccessKeyId, configuration.SecretAccessKey);

            _client = new AmazonS3Client(credentials, clientConfig);

            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string NormalisePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        return prefix.EndsWith('/') ? prefix : prefix + "/";
    }

    private static string BuildKey(CloudBackupConfiguration configuration, string fileName)
    {
        return NormalisePrefix(configuration.Prefix) + Path.GetFileName(fileName);
    }
}
