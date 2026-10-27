namespace StoreFlow.Domain.Entities;

/// <summary>
/// A local user account. The identifier is stable for the life of the account and is the only actor
/// identifier written to the audit trail — display names are never used as audit identifiers
/// (PRD §3). Users with transaction history are deactivated, never deleted.
/// </summary>
public sealed class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The password hash, in the format <c>argon2id$m,t,p$salt$hash</c> (or the legacy
    /// <c>pbkdf2$sha256$iterations$salt$hash</c>). Never a plaintext, never reversible.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public int RoleId { get; set; }

    public Role? Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
