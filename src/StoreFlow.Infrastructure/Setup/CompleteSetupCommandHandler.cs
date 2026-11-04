using System.Text.Json;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Application.Security;
using StoreFlow.Application.Settings;
using StoreFlow.Application.Setup;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Setup;

/// <summary>
/// Completes the first-run setup: creates the administrator account and records the store's
/// details and behavioural settings.
/// </summary>
public sealed class CompleteSetupCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IPasswordHasher passwordHasher,
    IClock clock,
    IAuditWriter auditWriter) : ICommandHandler<CompleteSetupCommand, Result>
{
    /// <summary>The error code when a user name is already taken.</summary>
    public const string DuplicateUserCode = "setup.duplicate_user";

    /// <summary>The error code when the password is too weak.</summary>
    public const string WeakPasswordCode = "setup.weak_password";

    /// <summary>The error code when the installation has already been set up.</summary>
    public const string AlreadyCompleteCode = "setup.already_complete";

    /// <inheritdoc />
    public async Task<Result> HandleAsync(CompleteSetupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        bool alreadyConfigured = await context.Users.AnyAsync(cancellationToken);

        if (alreadyConfigured)
        {
            return Result.Failure(AlreadyCompleteCode, "This installation has already been set up.");
        }

        if (string.IsNullOrWhiteSpace(command.Username))
        {
            return Result.Failure(DuplicateUserCode, "Choose a user name for the administrator account.");
        }

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 8)
        {
            return Result.Failure(WeakPasswordCode, "The administrator password must be at least 8 characters long.");
        }

        Role? ownerRole = await context.Roles
            .FirstOrDefaultAsync(role => role.Code == DatabaseInitializer.OwnerRoleCode, cancellationToken);

        if (ownerRole is null)
        {
            return Result.Failure(AlreadyCompleteCode, "The database has not been initialised yet.");
        }

        context.Users.Add(new User
        {
            Username = command.Username.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(command.DisplayName) ? command.Username.Trim() : command.DisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(command.Password),
            RoleId = ownerRole.Id,
            IsActive = true,
            CreatedUtc = clock.UtcNow,
            UpdatedUtc = clock.UtcNow,
        });

        Setting? detailsSetting = await context.Settings
            .FirstOrDefaultAsync(setting => setting.Key == DatabaseInitializer.StoreDetailsSettingKey, cancellationToken);

        if (detailsSetting is null)
        {
            context.Settings.Add(new Setting
            {
                Key = DatabaseInitializer.StoreDetailsSettingKey,
                Value = JsonSerializer.Serialize(command.StoreDetails),
                UpdatedUtc = clock.UtcNow,
            });
        }
        else
        {
            detailsSetting.Value = JsonSerializer.Serialize(command.StoreDetails);
            detailsSetting.UpdatedUtc = clock.UtcNow;
        }

        Setting? settingsSetting = await context.Settings
            .FirstOrDefaultAsync(setting => setting.Key == DatabaseInitializer.StoreSettingsSettingKey, cancellationToken);

        if (settingsSetting is null)
        {
            context.Settings.Add(new Setting
            {
                Key = DatabaseInitializer.StoreSettingsSettingKey,
                Value = JsonSerializer.Serialize(command.Settings),
                UpdatedUtc = clock.UtcNow,
            });
        }
        else
        {
            settingsSetting.Value = JsonSerializer.Serialize(command.Settings);
            settingsSetting.UpdatedUtc = clock.UtcNow;
        }

        auditWriter.AddTo(context, new AuditEntry("setup.complete", nameof(StoreDetails)));

        await context.BeforeWriteAsync(WritePoint.CompleteSetup, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
