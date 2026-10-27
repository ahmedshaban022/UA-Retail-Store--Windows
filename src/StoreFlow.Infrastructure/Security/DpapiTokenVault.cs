using System.Security.Cryptography;
using System.Text;
using StoreFlow.Application.Abstractions;

namespace StoreFlow.Infrastructure.Security;

/// <summary>
/// Stores cloud OAuth tokens encrypted with the Windows Data Protection API, scoped to the current
/// user. Tokens never touch the database and never reach a log file.
/// </summary>
public sealed class DpapiTokenVault : ITokenVault
{
    private const string FileExtension = ".token";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StoreFlow.TokenVault.v1");

    private readonly string _tokensDirectory;

    /// <summary>Creates the vault rooted at a directory, which is created if missing.</summary>
    public DpapiTokenVault(string tokensDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokensDirectory);

        _tokensDirectory = tokensDirectory;
        Directory.CreateDirectory(_tokensDirectory);
    }

    /// <inheritdoc />
    public async Task StoreAsync(string provider, string key, string payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(payload);

        byte[] protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(payload),
            Entropy,
            DataProtectionScope.CurrentUser);

        string path = PathFor(provider, key);
        string temporaryPath = path + ".tmp";

        await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <inheritdoc />
    public async Task<string?> ReadAsync(string provider, string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string path = PathFor(provider, key);

        if (!File.Exists(path))
        {
            return null;
        }

        byte[] protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);

        try
        {
            byte[] plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            // The file was protected by a different user or a previous machine. Treat it as absent
            // rather than failing the whole operation.
            return null;
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(string provider, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        foreach (string path in Directory.EnumerateFiles(_tokensDirectory, $"{Sanitise(provider)}*{FileExtension}"))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string provider, string key) =>
        Path.Combine(_tokensDirectory, $"{Sanitise(provider)}.{Sanitise(key)}{FileExtension}");

    private static string Sanitise(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string([.. value.Select(character => invalid.Contains(character) ? '_' : character)]);
    }
}
