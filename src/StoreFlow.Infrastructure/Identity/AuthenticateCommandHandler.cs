using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Application.Identity;
using StoreFlow.Application.Security;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Identity;

/// <summary>
/// Verifies a local password and produces the session the rest of the application runs under.
/// </summary>
/// <remarks>
/// The password hash is verified with a constant-time comparison, and a hash written by an older
/// scheme is silently upgraded to the current one after a successful sign-in.
/// </remarks>
public sealed class AuthenticateCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IPasswordHasher passwordHasher,
    IClock clock,
    IAuditWriter auditWriter) : ICommandHandler<AuthenticateCommand, Result<UserSession>>
{
    /// <summary>The error code for an unknown user name or a wrong password.</summary>
    public const string InvalidCredentialsCode = "auth.invalid_credentials";

    /// <summary>The error code for a deactivated account.</summary>
    public const string AccountDisabledCode = "auth.account_disabled";

    /// <inheritdoc />
    public async Task<Result<UserSession>> HandleAsync(
        AuthenticateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Username))
        {
            return Result<UserSession>.Failure(InvalidCredentialsCode, "Enter your user name and password.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        User? user = await context.Users
            .Include(candidate => candidate.Role)
            .ThenInclude(role => role!.RolePermissions)
            .FirstOrDefaultAsync(candidate => candidate.Username == command.Username, cancellationToken);

        if (user is null)
        {
            return Result<UserSession>.Failure(InvalidCredentialsCode, "That user name or password is not correct.");
        }

        PasswordVerificationResult verification = passwordHasher.Verify(command.Password, user.PasswordHash);

        if (!verification.IsVerified)
        {
            await auditWriter.WriteAsync(
                new AuditEntry("auth.sign_in_failed", nameof(User), user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                cancellationToken);

            return Result<UserSession>.Failure(InvalidCredentialsCode, "That user name or password is not correct.");
        }

        if (!user.IsActive)
        {
            return Result<UserSession>.Failure(
                AccountDisabledCode,
                "This account has been deactivated. Ask the owner to reactivate it.");
        }

        if (verification.NeedsRehash)
        {
            user.PasswordHash = passwordHasher.Hash(command.Password);
        }

        user.LastLoginUtc = clock.UtcNow;
        await context.BeforeWriteAsync(WritePoint.Authenticate, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var permissions = user.Role?.RolePermissions
            .Select(rolePermission => rolePermission.Permission)
            .ToHashSet() ?? [];

        var session = new UserSession(
            user.Id,
            user.Username,
            user.DisplayName,
            permissions,
            user.Role?.IsAdministrator ?? false);

        await auditWriter.WriteAsync(
            new AuditEntry("auth.sign_in", nameof(User), user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            cancellationToken);

        return Result<UserSession>.Success(session);
    }
}
