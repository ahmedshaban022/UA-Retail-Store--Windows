using StoreFlow.Application.Abstractions;

namespace StoreFlow.Infrastructure.Services;

/// <summary>
/// The signed-in session, held in memory for the life of the process. Signing out or locking clears
/// it immediately, so a locked window cannot act on the previous user's grants.
/// </summary>
public sealed class InMemoryUserSession : IUserSession
{
    /// <inheritdoc />
    public UserSession? Current { get; private set; }

    /// <inheritdoc />
    public bool IsAuthenticated => Current is not null && !IsLocked;

    /// <inheritdoc />
    public bool IsLocked { get; private set; }

    /// <inheritdoc />
    public void SignIn(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Current = session;
        IsLocked = false;
    }

    /// <inheritdoc />
    public void SignOut()
    {
        Current = null;
        IsLocked = false;
    }

    /// <inheritdoc />
    public void Lock() => IsLocked = true;

    /// <inheritdoc />
    public void Unlock() => IsLocked = false;
}
