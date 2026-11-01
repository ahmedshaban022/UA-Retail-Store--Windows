using StoreFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Persistence;

/// <summary>
/// The application database. One context per unit of work; the write path always runs inside a
/// single explicit transaction.
/// </summary>
public sealed class StoreFlowDbContext(DbContextOptions<StoreFlowDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<TaxCategory> TaxCategories => Set<TaxCategory>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<User> Users => Set<User>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();

    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();

    public DbSet<SalesReturnLine> SalesReturnLines => Set<SalesReturnLine>();

    public DbSet<HeldCart> HeldCarts => Set<HeldCart>();

    public DbSet<PurchaseDocument> PurchaseDocuments => Set<PurchaseDocument>();

    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    public DbSet<StockCount> StockCounts => Set<StockCount>();

    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<BackupLog> BackupLogs => Set<BackupLog>();

    public DbSet<RequestOutcome> RequestOutcomes => Set<RequestOutcome>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreFlowDbContext).Assembly);
    }
}
