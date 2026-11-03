using System.Text.Json;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Settings;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Infrastructure.Backup;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Persistence;

/// <summary>
/// Brings the database up to date and lays down the data every installation needs: the roles, the
/// selling units, a standard tax category and the default settings.
/// </summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IClock clock,
    IMigrationBackupHook? backupHook = null)
{
    /// <summary>The settings key that holds the store details.</summary>
    public const string StoreDetailsSettingKey = "store.details";

    /// <summary>The settings key that holds the behavioural settings.</summary>
    public const string StoreSettingsSettingKey = "store.settings";

    /// <summary>The code of the built-in administrator role.</summary>
    public const string OwnerRoleCode = "owner";

    /// <summary>The code of the built-in manager role.</summary>
    public const string ManagerRoleCode = "manager";

    /// <summary>The code of the built-in cashier role.</summary>
    public const string CashierRoleCode = "cashier";

    /// <summary>The code of the built-in inventory clerk role.</summary>
    public const string InventoryClerkRoleCode = "inventory_clerk";

    /// <summary>Creates the database if needed, applies the migrations and the triggers, and seeds.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await BackUpBeforeMigrateAsync(context, cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(SchemaTriggers.Script, cancellationToken);

        await SeedAsync(context, cancellationToken);
    }

    /// <summary>
    /// Keeps a copy of the shop before a migration that might change the shape of the data
    /// (PRD §8.4, NFR-12).
    /// </summary>
    /// <remarks>
    /// The backup is taken before the migration runs, not after, because afterwards the old shape
    /// is gone. If the backup cannot be taken the migration is still applied: a shop that cannot
    /// open its till cannot trade, and the failure is written to the backup log where the owner can
    /// see it.
    /// </remarks>
    private async Task BackUpBeforeMigrateAsync(
        StoreFlowDbContext context,
        CancellationToken cancellationToken)
    {
        if (backupHook is null)
        {
            return;
        }

        IReadOnlyList<string> pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            return;
        }

        try
        {
            await backupHook.BackUpAsync(pending, cancellationToken);
        }
        catch (BackupCaptureException ex)
        {
            await RecordFailedBackupAsync(context, ex.Message, cancellationToken);
        }
        catch (IOException ex)
        {
            await RecordFailedBackupAsync(context, ex.Message, cancellationToken);
        }
    }

    private async Task RecordFailedBackupAsync(
        StoreFlowDbContext context,
        string message,
        CancellationToken cancellationToken)
    {
        context.BackupLogs.Add(new BackupLog
        {
            TargetKind = "PreMigration",
            CreatedUtc = clock.UtcNow,
            Result = BackupResult.Failed,
            Message = message.Length > 2000 ? message[..2000] : message,
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Runs the integrity check that tells the user whether the database is healthy.</summary>
    public async Task<IReadOnlyList<string>> CheckIntegrityAsync(CancellationToken cancellationToken = default)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The pragma is exposed as a table-valued function, so the check can be read as rows.
        List<string> messages = await context.Database
            .SqlQueryRaw<string>("SELECT \"integrity_check\" AS \"Value\" FROM pragma_integrity_check")
            .ToListAsync(cancellationToken);

        return messages;
    }

    private async Task SeedAsync(StoreFlowDbContext context, CancellationToken cancellationToken)
    {
        await SeedRolesAsync(context, cancellationToken);
        await SeedUnitsAsync(context, cancellationToken);
        await SeedTaxCategoryAsync(context, cancellationToken);
        await SeedSettingsAsync(context, cancellationToken);
    }

    private static async Task SeedRolesAsync(StoreFlowDbContext context, CancellationToken cancellationToken)
    {
        var roles = new[]
        {
            new Role
            {
                Code = OwnerRoleCode,
                Name = "Owner / Administrator",
                NameAr = "المالك / مدير النظام",
                IsAdministrator = true,
            },
            new Role
            {
                Code = ManagerRoleCode,
                Name = "Manager",
                NameAr = "مدير",
                IsAdministrator = false,
            },
            new Role
            {
                Code = CashierRoleCode,
                Name = "Cashier",
                NameAr = "كاشير",
                IsAdministrator = false,
            },
            new Role
            {
                Code = InventoryClerkRoleCode,
                Name = "Inventory clerk",
                NameAr = "أمين المخزون",
                IsAdministrator = false,
            },
        };

        foreach (Role role in roles)
        {
            bool exists = await context.Roles.AnyAsync(existing => existing.Code == role.Code, cancellationToken);

            if (!exists)
            {
                context.Roles.Add(role);
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        await SeedRolePermissionsAsync(context, cancellationToken);
    }

    private static async Task SeedRolePermissionsAsync(StoreFlowDbContext context, CancellationToken cancellationToken)
    {
        var permissionsByRole = new Dictionary<string, Permission[]>
        {
            [ManagerRoleCode] =
            [
                Permission.DashboardView, Permission.CatalogView, Permission.CatalogEdit,
                Permission.CatalogImportExport, Permission.InventoryView, Permission.PurchasePost,
                Permission.StockAdjust, Permission.StockApprove, Permission.SalesPost, Permission.SalesReturn,
                Permission.SalesDiscountOverLimit, Permission.SalesPriceOverride, Permission.SalesView,
                Permission.ReportView, Permission.ReportExport, Permission.SettingsManage, Permission.BackupCreate,
                Permission.AuditView,
            ],
            [CashierRoleCode] =
            [
                Permission.DashboardView, Permission.CatalogView, Permission.InventoryView,
                Permission.SalesPost, Permission.SalesView,
            ],
            [InventoryClerkRoleCode] =
            [
                Permission.DashboardView, Permission.CatalogView, Permission.InventoryView,
                Permission.PurchasePost, Permission.StockAdjust,
            ],
        };

        var ownerRole = await context.Roles
            .Include(role => role.RolePermissions)
            .FirstOrDefaultAsync(role => role.Code == OwnerRoleCode, cancellationToken);

        if (ownerRole is not null)
        {
            foreach (Permission permission in Enum.GetValues<Permission>())
            {
                if (!ownerRole.RolePermissions.Any(existing => existing.Permission == permission))
                {
                    context.RolePermissions.Add(new RolePermission
                    {
                        RoleId = ownerRole.Id,
                        Permission = permission,
                    });
                }
            }
        }

        foreach ((string roleCode, Permission[] permissions) in permissionsByRole)
        {
            Role? role = await context.Roles.FirstOrDefaultAsync(existing => existing.Code == roleCode, cancellationToken);

            if (role is null)
            {
                continue;
            }

            var existing = await context.RolePermissions
                .Where(rolePermission => rolePermission.RoleId == role.Id)
                .Select(rolePermission => rolePermission.Permission)
                .ToListAsync(cancellationToken);

            foreach (Permission permission in permissions)
            {
                if (!existing.Contains(permission))
                {
                    context.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        Permission = permission,
                    });
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedUnitsAsync(StoreFlowDbContext context, CancellationToken cancellationToken)
    {
        if (!await context.Units.AnyAsync(cancellationToken))
        {
            context.Units.Add(new Unit
            {
                Name = "Piece",
                NameAr = "قطعة",
                Precision = 0,
            });
        }

        if (!await context.Units.AnyAsync(unit => unit.Name == "Kilogram", cancellationToken))
        {
            context.Units.Add(new Unit
            {
                Name = "Kilogram",
                NameAr = "كيلوجرام",
                Precision = 3,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedTaxCategoryAsync(StoreFlowDbContext context, CancellationToken cancellationToken)
    {
        if (!await context.TaxCategories.AnyAsync(cancellationToken))
        {
            context.TaxCategories.Add(new TaxCategory
            {
                Code = "standard",
                Name = "Standard",
                RateBasisPoints = 0,
                PricesIncludeTax = false,
                EffectiveFromUtc = DateTimeOffset.UnixEpoch,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSettingsAsync(StoreFlowDbContext context, CancellationToken cancellationToken)
    {
        var storeDetails = new StoreDetails(
            StoreName: "StoreFlow",
            StoreNameAr: "ستور فلو",
            Address: null,
            Phone: null,
            TaxNumber: null,
            LogoPath: null);

        await InsertSettingIfMissingAsync(
            context,
            StoreDetailsSettingKey,
            JsonSerializer.Serialize(storeDetails),
            cancellationToken);

        await InsertSettingIfMissingAsync(
            context,
            StoreSettingsSettingKey,
            JsonSerializer.Serialize(StoreSettings.Defaults),
            cancellationToken);
    }

    /// <summary>
    /// Writes a setting only when the shop does not have one yet.
    /// </summary>
    /// <remarks>
    /// The settings belong to the shop, not to the installation. They are laid down once, when the
    /// shop is new, and from then on the shop's own values are what count. A shop that moves its
    /// backup folder, or changes the discount it allows, must still have those settings after the
    /// next restart — so the installer never writes over a setting that is already there.
    /// </remarks>
    /// <param name="context">The context.</param>
    /// <param name="key">The setting's key.</param>
    /// <param name="value">The value to lay down.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task.</returns>
    private async Task InsertSettingIfMissingAsync(
        StoreFlowDbContext context,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        bool exists = await context.Settings.AnyAsync(setting => setting.Key == key, cancellationToken);

        if (exists)
        {
            return;
        }

        context.Settings.Add(new Setting
        {
            Key = key,
            Value = value,
            UpdatedUtc = clock.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);
    }
}
