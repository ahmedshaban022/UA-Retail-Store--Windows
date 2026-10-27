namespace StoreFlow.Application.Settings;

/// <summary>
/// The store's own details, shown on invoices and receipts (PRD §5.1 GEN-01).
/// </summary>
/// <param name="StoreName">The shop's name.</param>
/// <param name="StoreNameAr">The Arabic name, when the shop uses one.</param>
/// <param name="Address">The street address.</param>
/// <param name="Phone">The contact telephone number.</param>
/// <param name="TaxNumber">The store's tax registration number, when it has one.</param>
/// <param name="LogoPath">The logo used on printed output.</param>
public sealed record StoreDetails(
    string StoreName,
    string? StoreNameAr,
    string? Address,
    string? Phone,
    string? TaxNumber,
    string? LogoPath);

/// <summary>How the application behaves during a shift.</summary>
/// <param name="AutoLockMinutes">
/// How long the session stays unlocked without activity. Zero disables the automatic lock.
/// </param>
/// <param name="MaximumDiscountPercent">
/// The discount percentage a user may apply without a second authorised approver.
/// </param>
/// <param name="PrintCopies">The number of copies printed by default.</param>
/// <param name="ScanSuffix">The key that terminates a barcode scan.</param>
/// <param name="ScanInterKeyDelayMs">The longest gap between scanned characters.</param>
/// <param name="BackupDirectory">Where local backups are written.</param>
/// <param name="DailyBackupEnabled">Whether the daily backup runs.</param>
/// <param name="WeeklyBackupEnabled">Whether the weekly backup runs.</param>
/// <param name="DailyBackupRetention">How many daily backups to keep.</param>
/// <param name="WeeklyBackupRetention">How many weekly backups to keep.</param>
public sealed record StoreSettings(
    int AutoLockMinutes,
    decimal MaximumDiscountPercent,
    int PrintCopies,
    string ScanSuffix,
    int ScanInterKeyDelayMs,
    string? BackupDirectory,
    bool DailyBackupEnabled,
    bool WeeklyBackupEnabled,
    int DailyBackupRetention,
    int WeeklyBackupRetention)
{
    /// <summary>The settings a fresh installation starts with.</summary>
    public static StoreSettings Defaults { get; } = new(
        AutoLockMinutes: 15,
        MaximumDiscountPercent: 10m,
        PrintCopies: 1,
        ScanSuffix: "Enter",
        ScanInterKeyDelayMs: 120,
        BackupDirectory: null,
        DailyBackupEnabled: true,
        WeeklyBackupEnabled: true,
        DailyBackupRetention: 7,
        WeeklyBackupRetention: 4);
}
