using StoreFlow.Application.Common;
using StoreFlow.Application.Settings;

namespace StoreFlow.Application.Setup;

/// <summary>
/// Completes the first-run setup: creates the administrator account and records the store's
/// details, currency and tax policy.
/// </summary>
/// <param name="Username">The administrator's login name.</param>
/// <param name="Password">The administrator's password.</param>
/// <param name="DisplayName">The administrator's display name.</param>
/// <param name="StoreDetails">The shop's details, shown on invoices and receipts.</param>
/// <param name="Settings">The behavioural settings to apply.</param>
public sealed record CompleteSetupCommand(
    string Username,
    string Password,
    string DisplayName,
    StoreDetails StoreDetails,
    StoreSettings Settings) : ICommand<Result>;

/// <summary>
/// Reports whether the installation has been set up. The first-run wizard runs while this is false.
/// </summary>
public sealed record SetupStatusQuery : IQuery<Result<SetupStatus>>;

/// <summary>What the installation currently knows about itself.</summary>
/// <param name="IsComplete">True once an administrator account exists.</param>
/// <param name="HasUsers">True when at least one user account exists.</param>
/// <param name="StoreDetails">The store details, when they have been recorded.</param>
public sealed record SetupStatus(bool IsComplete, bool HasUsers, StoreDetails? StoreDetails);
