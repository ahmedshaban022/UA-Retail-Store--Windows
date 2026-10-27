namespace StoreFlow.Application.Abstractions;

/// <summary>
/// Stores the OAuth tokens used by the cloud backup targets. Implementations encrypt the payload
/// with the Windows Data Protection API, scoped to the current user; tokens are never written to the
/// database and never logged.
/// </summary>
public interface ITokenVault
{
    /// <summary>Stores a token payload for a provider and key.</summary>
    Task StoreAsync(string provider, string key, string payload, CancellationToken cancellationToken = default);

    /// <summary>Reads a token payload, or null when it is not present.</summary>
    Task<string?> ReadAsync(string provider, string key, CancellationToken cancellationToken = default);

    /// <summary>Removes every token for a provider, used when access is revoked.</summary>
    Task ClearAsync(string provider, CancellationToken cancellationToken = default);
}
