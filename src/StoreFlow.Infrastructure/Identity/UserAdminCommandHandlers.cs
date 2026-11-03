using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Identity;
using StoreFlow.Application.Security;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Identity;

/// <summary>The actions the user administration writes to the audit trail.</summary>
public static class UserAdminAudit
{
    /// <summary>An account was added.</summary>
    public const string Created = "user.created";

    /// <summary>An account's role was changed.</summary>
    public const string RoleChanged = "user.role_changed";

    /// <summary>An account was deactivated.</summary>
    public const string Deactivated = "user.deactivated";

    /// <summary>An account was brought back.</summary>
    public const string Reactivated = "user.reactivated";
}

/// <summary>Lists the user accounts.</summary>
public sealed class ListUsersQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListUsersQuery, Result<IReadOnlyList<UserSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<UserSummary>>> HandleAsync(
        ListUsersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.UserManage, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<UserSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<User> users = await context.Users
            .AsNoTracking()
            .Include(user => user.Role)
            .OrderBy(user => user.DisplayName)
            .ToListAsync(cancellationToken);

        var summaries = new List<UserSummary>(users.Count);

        foreach (User user in users)
        {
            summaries.Add(new UserSummary(
                user.Id,
                user.Username,
                user.DisplayName,
                user.RoleId,
                user.Role?.Code ?? string.Empty,
                user.Role?.Name ?? string.Empty,
                user.IsActive,
                user.LastLoginUtc));
        }

        return Result<IReadOnlyList<UserSummary>>.Success(summaries);
    }
}

/// <summary>Lists the roles and the permissions each one carries.</summary>
public sealed class ListRolesQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListRolesQuery, Result<IReadOnlyList<RoleSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RoleSummary>>> HandleAsync(
        ListRolesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.UserManage, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<RoleSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<Role> roles = await context.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken);

        var summaries = new List<RoleSummary>(roles.Count);

        foreach (Role role in roles)
        {
            List<string> permissions = await context.RolePermissions
                .AsNoTracking()
                .Where(grant => grant.RoleId == role.Id)
                .OrderBy(grant => grant.Permission)
                .Select(grant => grant.Permission.ToString())
                .ToListAsync(cancellationToken);

            int userCount = await context.Users
                .AsNoTracking()
                .CountAsync(user => user.RoleId == role.Id && user.IsActive, cancellationToken);

            summaries.Add(new RoleSummary(
                role.Id,
                role.Code,
                role.Name,
                role.NameAr,
                role.IsAdministrator,
                permissions,
                userCount));
        }

        return Result<IReadOnlyList<RoleSummary>>.Success(summaries);
    }
}

/// <summary>
/// Adds a user account.
/// </summary>
/// <remarks>
/// The password is hashed before it is written and never leaves this handler in any other form.
/// </remarks>
public sealed class CreateUserCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IPasswordHasher passwordHasher,
    IClock clock,
    IAuditWriter auditWriter,
    IUserSession session)
    : ICommandHandler<CreateUserCommand, Result<UserSummary>>
{
    /// <summary>The shortest password the shop accepts.</summary>
    public const int MinimumPasswordLength = 8;

    /// <inheritdoc />
    public async Task<Result<UserSummary>> HandleAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.UserManage, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<UserSummary>.Failure(permitted.Error);
        }

        string username = command.Username.Trim();

        if (username.Length == 0 || command.DisplayName.Trim().Length == 0)
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.NotFound,
                "The account needs a user name and a name to show.");
        }

        if (command.Password.Length < MinimumPasswordLength)
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.PasswordTooShort,
                $"The password needs at least {MinimumPasswordLength} characters.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        bool taken = await context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Username.ToLower() == username.ToLower(), cancellationToken);

        if (taken)
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.DuplicateUsername,
                "That user name is already taken.");
        }

        Role? role = await context.Roles.FirstOrDefaultAsync(candidate => candidate.Id == command.RoleId, cancellationToken);

        if (role is null)
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.RoleNotFound,
                "That role does not exist.");
        }

        var user = new User
        {
            Username = username,
            DisplayName = command.DisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(command.Password),
            RoleId = role.Id,
            IsActive = true,
            CreatedUtc = clock.UtcNow,
            UpdatedUtc = clock.UtcNow,
        };

        context.Users.Add(user);

        await context.BeforeWriteAsync(WritePoint.CreateUser, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                UserAdminAudit.Created,
                nameof(User),
                user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                $"role {role.Code}",
                session.Current?.UserId),
            cancellationToken);

        return Result<UserSummary>.Success(new UserSummary(
            user.Id,
            user.Username,
            user.DisplayName,
            role.Id,
            role.Code,
            role.Name,
            user.IsActive,
            user.LastLoginUtc));
    }
}

/// <summary>
/// Changes the role a user account carries.
/// </summary>
/// <remarks>
/// The last administrator cannot be demoted: the shop would then have nobody who could bring the
/// accounts back.
/// </remarks>
public sealed class ChangeUserRoleCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IClock clock,
    IAuditWriter auditWriter,
    IUserSession session)
    : ICommandHandler<ChangeUserRoleCommand, Result<UserSummary>>
{
    /// <inheritdoc />
    public async Task<Result<UserSummary>> HandleAsync(
        ChangeUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.UserManage, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<UserSummary>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        User? user = await context.Users.FirstOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.NotFound,
                "That account does not exist.");
        }

        Role? role = await context.Roles.FirstOrDefaultAsync(candidate => candidate.Id == command.RoleId, cancellationToken);

        if (role is null)
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.RoleNotFound,
                "That role does not exist.");
        }

        if (await UserAdminRules.IsLastAdministrator(context, user, cancellationToken) && !role.IsAdministrator)
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.LastAdministrator,
                "That is the last administrator, so the role cannot be changed.");
        }

        user.RoleId = role.Id;
        user.UpdatedUtc = clock.UtcNow;

        await context.BeforeWriteAsync(WritePoint.ChangeUserRole, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                UserAdminAudit.RoleChanged,
                nameof(User),
                user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                $"role {role.Code}",
                session.Current?.UserId),
            cancellationToken);

        return Result<UserSummary>.Success(new UserSummary(
            user.Id,
            user.Username,
            user.DisplayName,
            role.Id,
            role.Code,
            role.Name,
            user.IsActive,
            user.LastLoginUtc));
    }
}

/// <summary>
/// Deactivates a user account, or brings it back.
/// </summary>
/// <remarks>
/// An account with history is deactivated, never deleted: the audit trail names the person who did
/// what they did, and that name has to keep meaning something (PRD §3).
/// </remarks>
public sealed class SetUserActiveCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IClock clock,
    IAuditWriter auditWriter,
    IUserSession session)
    : ICommandHandler<SetUserActiveCommand, Result<UserSummary>>
{
    /// <inheritdoc />
    public async Task<Result<UserSummary>> HandleAsync(
        SetUserActiveCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.UserManage, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<UserSummary>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        User? user = await context.Users.FirstOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.NotFound,
                "That account does not exist.");
        }

        if (!command.IsActive && session.Current?.UserId == user.Id)
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.CannotDeactivateSelf,
                "You cannot deactivate the account you are signed in with.");
        }

        if (!command.IsActive && await UserAdminRules.IsLastAdministrator(context, user, cancellationToken))
        {
            return Result<UserSummary>.Failure(
                UserAdminErrorCodes.LastAdministrator,
                "That is the last administrator, so the account cannot be deactivated.");
        }

        user.IsActive = command.IsActive;
        user.UpdatedUtc = clock.UtcNow;

        await context.BeforeWriteAsync(WritePoint.SetUserActive, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                command.IsActive ? UserAdminAudit.Reactivated : UserAdminAudit.Deactivated,
                nameof(User),
                user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                null,
                session.Current?.UserId),
            cancellationToken);

        Role? role = await context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == user.RoleId, cancellationToken);

        return Result<UserSummary>.Success(new UserSummary(
            user.Id,
            user.Username,
            user.DisplayName,
            user.RoleId,
            role?.Code ?? string.Empty,
            role?.Name ?? string.Empty,
            user.IsActive,
            user.LastLoginUtc));
    }

    /// <summary>
    /// True when the account is the last one that can administer the shop.
    /// </summary>
    private static async Task<bool> IsLastAdministrator(
        StoreFlowDbContext context,
        User user,
        CancellationToken cancellationToken) =>
        await UserAdminRules.IsLastAdministrator(context, user, cancellationToken);
}

/// <summary>The rules the user administration shares.</summary>
internal static class UserAdminRules
{
    /// <summary>
    /// True when the account is the last one that can administer the shop. Deactivating or demoting
    /// that account would leave the shop with nobody who could bring the accounts back.
    /// </summary>
    internal static async Task<bool> IsLastAdministrator(
        StoreFlowDbContext context,
        User user,
        CancellationToken cancellationToken)
    {
        Role? role = await context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == user.RoleId, cancellationToken);

        if (role is null || !role.IsAdministrator || !user.IsActive)
        {
            return false;
        }

        int otherAdministrators = await context.Users
            .AsNoTracking()
            .CountAsync(
                candidate => candidate.IsActive &&
                             candidate.Id != user.Id &&
                             candidate.Role!.IsAdministrator,
                cancellationToken);

        return otherAdministrators == 0;
    }
}
