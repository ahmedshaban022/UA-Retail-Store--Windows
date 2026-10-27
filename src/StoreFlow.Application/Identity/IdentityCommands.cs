using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;

namespace StoreFlow.Application.Identity;

/// <summary>
/// Signs a user in with a local password.
/// </summary>
/// <param name="Username">The login name.</param>
/// <param name="Password">The password typed by the user.</param>
public sealed record AuthenticateCommand(string Username, string Password) : ICommand<Result<UserSession>>;

/// <summary>
/// Changes the signed-in user's own password.
/// </summary>
/// <param name="CurrentPassword">The current password, required to prove ownership.</param>
/// <param name="NewPassword">The new password.</param>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword)
    : ICommand<Result>;

/// <summary>The signed-in user's own account, without the password hash.</summary>
/// <param name="UserId">The stable actor id.</param>
/// <param name="Username">The login name.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
public sealed record CurrentUserDto(int UserId, string Username, string DisplayName);
