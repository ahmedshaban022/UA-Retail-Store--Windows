using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Authorization;

namespace StoreFlow.Application.Authorization;

/// <summary>
/// Decides whether the current session may perform an operation.
/// </summary>
/// <remarks>
/// Authorisation lives in the application layer, not in the UI. Hiding a button is a convenience;
/// refusing the command is the control (PRD §3).
/// </remarks>
public interface IAuthorizationService
{
    /// <summary>True when the current session holds the permission.</summary>
    bool IsAllowed(Permission permission);

    /// <summary>
    /// Fails when the current session does not hold the permission. Call this at the start of every
    /// handler, before any data is read or written.
    /// </summary>
    Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default authorisation service, backed by the signed-in session.
/// </summary>
public sealed class AuthorizationService(IUserSession session) : IAuthorizationService
{
    /// <summary>The error code returned when a permission is missing.</summary>
    public const string NotAuthorizedCode = "auth.not_authorized";

    /// <summary>The error code returned when nobody is signed in.</summary>
    public const string NotAuthenticatedCode = "auth.not_authenticated";

    public bool IsAllowed(Permission permission)
    {
        UserSession? current = session.Current;

        if (current is null || session.IsLocked)
        {
            return false;
        }

        return current.IsAdministrator || current.Permissions.Contains(permission);
    }

    public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (session.Current is null)
        {
            return Task.FromResult(Result.Failure(NotAuthenticatedCode, "Sign in to continue."));
        }

        if (session.IsLocked)
        {
            return Task.FromResult(Result.Failure(NotAuthenticatedCode, "Unlock the session to continue."));
        }

        return IsAllowed(permission)
            ? Task.FromResult(Result.Success())
            : Task.FromResult(Result.Failure(
                NotAuthorizedCode,
                "Your role does not allow this action."));
    }
}
