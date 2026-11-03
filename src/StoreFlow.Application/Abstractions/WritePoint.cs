namespace StoreFlow.Application.Abstractions;

/// <summary>
/// Every point in the product where something is written to the database.
///
/// A write point is not a table. It is a moment in a command: the sale's invoice, the sale's
/// stock movements, the commit that makes either of them permanent. A command that touches three
/// of these has three ways to fail, and each of them has to leave the store in a state the shop
/// can keep trading in.
///
/// The catalogue exists so that a test can aim a failure at one of them by name (PRD NFR-05).
/// </summary>
public enum WritePoint
{
    /// <summary>A sale's invoice, its lines and its payment. Nothing has moved yet.</summary>
    PostSaleInvoice,

    /// <summary>The stock movements a sale causes, and the invoice being marked committed.</summary>
    PostSaleStockMovement,

    /// <summary>The sale audit entry saved inside the sale transaction.</summary>
    PostSaleAudit,

    /// <summary>The commit that makes a sale permanent.</summary>
    PostSaleCommit,

    /// <summary>A return's header, its lines and its refund.</summary>
    PostReturnDocuments,

    /// <summary>The stock movements a return causes.</summary>
    PostReturnRestock,

    /// <summary>The commit that makes a return permanent.</summary>
    PostReturnCommit,

    /// <summary>The reversing invoice, its lines and its payment.</summary>
    ReverseInvoiceDocuments,

    /// <summary>The stock movements a reversal causes.</summary>
    ReverseInvoiceStockMovement,

    /// <summary>The commit that makes a reversal permanent.</summary>
    ReverseInvoiceCommit,

    /// <summary>The goods arriving, and the stock they add.</summary>
    PostPurchaseStock,

    /// <summary>The commit that makes a receipt permanent.</summary>
    PostPurchaseCommit,

    /// <summary>A purchase being recorded before it is posted.</summary>
    SavePurchase,

    /// <summary>The reversing purchase document and its lines.</summary>
    ReversePurchaseDocument,

    /// <summary>The stock movements a purchase reversal causes.</summary>
    ReversePurchaseStock,

    /// <summary>The commit that makes a purchase reversal permanent.</summary>
    ReversePurchaseCommit,

    /// <summary>A stock count being recorded, before it is submitted.</summary>
    SaveStockCount,

    /// <summary>A stock count being submitted for approval.</summary>
    SubmitStockCount,

    /// <summary>A stock count being approved.</summary>
    ApproveStockCount,

    /// <summary>A stock count being cancelled.</summary>
    CancelStockCount,

    /// <summary>The movements a stock count produces.</summary>
    PostStockCountMovements,

    /// <summary>The commit that makes a stock count permanent.</summary>
    PostStockCountCommit,

    /// <summary>The opening stock of a product being recorded.</summary>
    RecordOpeningStock,

    /// <summary>The commit that makes an opening stock permanent.</summary>
    RecordOpeningStockCommit,

    /// <summary>A stock adjustment that needs approval being asked for.</summary>
    RequestStockAdjustmentApproval,

    /// <summary>A manual stock adjustment being written.</summary>
    PostStockAdjustment,

    /// <summary>The commit that makes a stock adjustment permanent.</summary>
    PostStockAdjustmentCommit,

    /// <summary>A stock adjustment being approved.</summary>
    ApproveStockAdjustment,

    /// <summary>The commit that makes an approved adjustment permanent.</summary>
    ApproveStockAdjustmentCommit,

    /// <summary>A cart being put aside.</summary>
    HoldCart,

    /// <summary>A cart being taken back up.</summary>
    ResumeHeldCart,

    /// <summary>A cart being cleared.</summary>
    ClearHeldCart,

    /// <summary>A product being created or edited.</summary>
    SaveProduct,

    /// <summary>A product being deactivated or reactivated.</summary>
    SetProductActive,

    /// <summary>Products being imported from a file.</summary>
    ImportProducts,

    /// <summary>A category being created or edited.</summary>
    SaveCategory,

    /// <summary>A category being deactivated or reactivated.</summary>
    SetCategoryActive,

    /// <summary>A unit of measure being created or edited.</summary>
    SaveUnit,

    /// <summary>A unit of measure being deactivated or reactivated.</summary>
    SetUnitActive,

    /// <summary>A supplier being created or edited.</summary>
    SaveSupplier,

    /// <summary>A supplier being deactivated or reactivated.</summary>
    SetSupplierActive,

    /// <summary>A user account being created.</summary>
    CreateUser,

    /// <summary>A user's role being changed.</summary>
    ChangeUserRole,

    /// <summary>A user being deactivated or reactivated.</summary>
    SetUserActive,

    /// <summary>A sign-in being recorded.</summary>
    Authenticate,

    /// <summary>A cash movement being recorded in the drawer.</summary>
    RecordDrawerCash,

    /// <summary>A discount over the limit being sent for approval.</summary>
    RequestDiscountApproval,

    /// <summary>An approval request being approved or turned down.</summary>
    DecideApprovalRequest,

    /// <summary>The store's settings being saved.</summary>
    SaveStoreSettings,

    /// <summary>The backup schedule being saved.</summary>
    SaveBackupSchedule,

    /// <summary>The cloud backup target's address being saved.</summary>
    SaveCloudAddress,

    /// <summary>The first-run setup being completed.</summary>
    CompleteSetup,

    /// <summary>An audit entry being written.</summary>
    WriteAuditEntry,

    /// <summary>A backup log row being written.</summary>
    WriteBackupLog,
}
