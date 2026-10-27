using StoreFlow.Domain.Authorization;

namespace StoreFlow.Application.Identity;

/// <summary>
/// A user account as the interface sees it. The password hash is never part of this shape.
/// </summary>
/// <param name="Id">The stable actor id.</param>
/// <param name="Username">The login name.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
/// <param name="RoleCode">The role's machine-readable code.</param>
/// <param name="RoleName">The role's display name.</param>
/// <param name="IsActive">False for a deactivated account.</param>
/// <param name="LastLoginUtc">When the user last signed in.</param>
public sealed record UserAccountDto(
    int Id,
    string Username,
    string DisplayName,
    string RoleCode,
    string RoleName,
    bool IsActive,
    DateTimeOffset? LastLoginUtc);

/// <summary>The permissions a role grants.</summary>
/// <param name="RoleCode">The role's machine-readable code.</param>
/// <param name="Permissions">The permissions granted.</param>
public sealed record RoleGrantDto(string RoleCode, IReadOnlySet<Permission> Permissions);
