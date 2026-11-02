namespace StoreFlow.Application.Identity;

using StoreFlow.Application.Common;

/// <summary>The codes the user administration answers with.</summary>
public static class UserAdminErrorCodes
{
    /// <summary>The user does not exist.</summary>
    public const string NotFound = "user.not_found";

    /// <summary>The role does not exist.</summary>
    public const string RoleNotFound = "user.role_not_found";

    /// <summary>The user name is already taken.</summary>
    public const string DuplicateUsername = "user.duplicate_username";

    /// <summary>The password is too short to be accepted.</summary>
    public const string PasswordTooShort = "user.password_too_short";

    /// <summary>
    /// The shop would be left without an administrator. The last administrator cannot be
    /// deactivated or demoted, because nobody would then be able to bring the shop back.
    /// </summary>
    public const string LastAdministrator = "user.last_administrator";

    /// <summary>A signed-in user cannot deactivate their own account.</summary>
    public const string CannotDeactivateSelf = "user.cannot_deactivate_self";
}

/// <summary>One user account, as the manager's screen shows it.</summary>
/// <param name="Id">The account's id.</param>
/// <param name="Username">The login name.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
/// <param name="RoleId">The role the account carries.</param>
/// <param name="RoleCode">The role's machine-readable code.</param>
/// <param name="RoleName">The role's name.</param>
/// <param name="IsActive">False when the account has been deactivated.</param>
/// <param name="LastLoginUtc">When the account last signed in.</param>
public sealed record UserSummary(
    int Id,
    string Username,
    string DisplayName,
    int RoleId,
    string RoleCode,
    string RoleName,
    bool IsActive,
    DateTimeOffset? LastLoginUtc);

/// <summary>One role, as the manager's screen shows it.</summary>
/// <param name="Id">The role's id.</param>
/// <param name="Code">The role's machine-readable code.</param>
/// <param name="Name">The role's name.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="IsAdministrator">True for the role that bypasses grant checks.</param>
/// <param name="Permissions">The permissions the role carries.</param>
/// <param name="UserCount">How many accounts carry the role.</param>
public sealed record RoleSummary(
    int Id,
    string Code,
    string Name,
    string? Description,
    bool IsAdministrator,
    IReadOnlyList<string> Permissions,
    int UserCount);

/// <summary>Lists the user accounts.</summary>
public sealed record ListUsersQuery : IQuery<Result<IReadOnlyList<UserSummary>>>;

/// <summary>Lists the roles and the permissions each one carries.</summary>
public sealed record ListRolesQuery : IQuery<Result<IReadOnlyList<RoleSummary>>>;

/// <summary>Adds a user account.</summary>
/// <param name="Username">The login name.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
/// <param name="Password">The first password.</param>
/// <param name="RoleId">The role the account carries.</param>
public sealed record CreateUserCommand(
    string Username,
    string DisplayName,
    string Password,
    int RoleId) : ICommand<Result<UserSummary>>;

/// <summary>Changes the role a user account carries.</summary>
/// <param name="UserId">The account.</param>
/// <param name="RoleId">The role it should carry.</param>
public sealed record ChangeUserRoleCommand(int UserId, int RoleId) : ICommand<Result<UserSummary>>;

/// <summary>Deactivates a user account, or brings it back.</summary>
/// <param name="UserId">The account.</param>
/// <param name="IsActive">True to bring the account back, false to deactivate it.</param>
public sealed record SetUserActiveCommand(int UserId, bool IsActive) : ICommand<Result<UserSummary>>;
