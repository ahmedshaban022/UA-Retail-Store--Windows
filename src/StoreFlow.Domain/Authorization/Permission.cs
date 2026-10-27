namespace StoreFlow.Domain.Authorization;

/// <summary>
/// Every operation the application can authorise. Authorisation is enforced in the application layer
/// (PRD §3), never by hiding buttons in the UI.
/// </summary>
public enum Permission
{
    /// <summary>View the dashboard and basic KPIs.</summary>
    DashboardView = 0,

    /// <summary>View products, categories, units and suppliers.</summary>
    CatalogView = 1,

    /// <summary>Create, edit and deactivate catalogue records.</summary>
    CatalogEdit = 2,

    /// <summary>Import and export catalogue data.</summary>
    CatalogImportExport = 3,

    /// <summary>View stock balances, movements and counts.</summary>
    InventoryView = 4,

    /// <summary>Post purchases and receive goods.</summary>
    PurchasePost = 5,

    /// <summary>Create and post stock adjustments and stock counts.</summary>
    StockAdjust = 6,

    /// <summary>Approve a stock count or a large adjustment.</summary>
    StockApprove = 7,

    /// <summary>Run a sale at the point of sale.</summary>
    SalesPost = 8,

    /// <summary>Process a return against a posted invoice.</summary>
    SalesReturn = 9,

    /// <summary>Apply a discount beyond the configured policy limit.</summary>
    SalesDiscountOverLimit = 10,

    /// <summary>Override a selling price.</summary>
    SalesPriceOverride = 11,

    /// <summary>View the sales invoice history and reprint receipts.</summary>
    SalesView = 12,

    /// <summary>View reports.</summary>
    ReportView = 13,

    /// <summary>Export reports and data.</summary>
    ReportExport = 14,

    /// <summary>Manage users, roles and permissions.</summary>
    UserManage = 15,

    /// <summary>Change store, currency, tax, printer and locale settings.</summary>
    SettingsManage = 16,

    /// <summary>Create and configure backups.</summary>
    BackupCreate = 17,

    /// <summary>Restore the database from a backup.</summary>
    BackupRestore = 18,

    /// <summary>Read the audit trail.</summary>
    AuditView = 19,
}
