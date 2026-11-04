using StoreFlow.Application.Abstractions;
using StoreFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Persistence;

/// <summary>
/// The application database. One context per unit of work; the write path always runs inside a
/// single explicit transaction.
/// </summary>
/// <param name="options">The provider options.</param>
/// <param name="writeFaultInjector">
/// The seam a test uses to fail one write on purpose. It is optional so that a context built
/// outside the application's container still works; in the container the injector is always
/// resolved from DI, and the one registered there does nothing.
/// </param>
public sealed class StoreFlowDbContext(
    DbContextOptions<StoreFlowDbContext> options,
    IWriteFaultInjector? writeFaultInjector = null) : DbContext(options), IAuditTrail
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

    /// <summary>
    /// Announces that the side effects of <paramref name="point"/> are about to be written.
    ///
    /// The call does nothing in the running shop. A test replaces the injector and the write then
    /// fails at exactly this point, which is what proves that the command either committed
    /// everything or committed nothing.
    /// </summary>
    /// <param name="point">Which write is about to happen.</param>
    /// <param name="cancellationToken">The token the write was asked to honour.</param>
    public Task BeforeWriteAsync(WritePoint point, CancellationToken cancellationToken) =>
        writeFaultInjector?.BeforeWriteAsync(point, cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Announces that the transaction holding <paramref name="point"/> is about to be committed.
    /// </summary>
    /// <param name="point">Which write is about to be committed.</param>
    /// <param name="cancellationToken">The token the commit was asked to honour.</param>
    public Task BeforeCommitAsync(WritePoint point, CancellationToken cancellationToken) =>
        writeFaultInjector?.BeforeCommitAsync(point, cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Adds an audit entry to this write, so that the entry and everything else in the write are
    /// made permanent together or not at all.
    /// </summary>
    /// <param name="entry">The entry to add.</param>
    /// <param name="actorUserId">The actor, or null for a system action.</param>
    /// <param name="timestampUtc">When the action happened.</param>
    public void Append(AuditEntry entry, int? actorUserId, DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(entry);

        AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            BeforeHash = entry.BeforeHash,
            AfterSummary = entry.AfterSummary,
            TimestampUtc = timestampUtc,
        });
    }
}
