using StoreFlow.Domain.Authorization;

namespace StoreFlow.Application.Abstractions;

/// <summary>The signed-in user's identity and grants.</summary>
/// <param name="UserId">The stable actor id used in the audit trail.</param>
/// <param name="Username">The login name.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
/// <param name="Permissions">The permissions the current role grants.</param>
/// <param name="IsAdministrator">True when the role bypasses grant checks.</param>
public sealed record UserSession(
    int UserId,
    string Username,
    string DisplayName,
    IReadOnlySet<Permission> Permissions,
    bool IsAdministrator);

/// <summary>
/// The current session. Protected operations require an active, authorised session (PRD §5.1 GEN-03).
/// </summary>
public interface IUserSession
{
    /// <summary>The signed-in user, or null when nobody is signed in.</summary>
    UserSession? Current { get; }

    /// <summary>True when a user is signed in and the session is not locked.</summary>
    bool IsAuthenticated { get; }

    /// <summary>True when the session is locked and requires re-authentication.</summary>
    bool IsLocked { get; }

    /// <summary>Grants a session after a successful sign-in.</summary>
    void SignIn(UserSession session);

    /// <summary>Clears the session at sign-out.</summary>
    void SignOut();

    /// <summary>Locks the session, hiding protected content until it is unlocked.</summary>
    void Lock();

    /// <summary>Unlocks the session after re-authentication.</summary>
    void Unlock();
}
