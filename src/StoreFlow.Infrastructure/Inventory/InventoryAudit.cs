namespace StoreFlow.Infrastructure.Inventory;

/// <summary>
/// The audit actions the inventory and purchase screens write. The action names are stable so the
/// audit trail can be filtered without parsing free text (PRD §5.9).
/// </summary>
public static class InventoryAudit
{
    /// <summary>A purchase document was drafted or edited.</summary>
    public const string PurchaseSave = "purchase.save";

    /// <summary>A purchase document was posted and goods were received.</summary>
    public const string PurchasePost = "purchase.post";

    /// <summary>A posted purchase document was reversed.</summary>
    public const string PurchaseReverse = "purchase.reverse";

    /// <summary>The purchase register was exported.</summary>
    public const string PurchaseExport = "purchase.export";

    /// <summary>Opening stock was recorded.</summary>
    public const string OpeningStock = "inventory.opening_stock";

    /// <summary>A stock adjustment was posted straight to the ledger.</summary>
    public const string AdjustmentPost = "inventory.adjustment_post";

    /// <summary>A large stock adjustment was routed for approval.</summary>
    public const string AdjustmentRequest = "inventory.adjustment_request";

    /// <summary>A large stock adjustment was approved and posted.</summary>
    public const string AdjustmentApprove = "inventory.adjustment_approve";

    /// <summary>A stock count session was drafted or edited.</summary>
    public const string CountSave = "inventory.count_save";

    /// <summary>A stock count session was submitted for approval.</summary>
    public const string CountSubmit = "inventory.count_submit";

    /// <summary>A stock count session was approved.</summary>
    public const string CountApprove = "inventory.count_approve";

    /// <summary>A stock count session was posted, writing its corrections.</summary>
    public const string CountPost = "inventory.count_post";

    /// <summary>A stock count session was cancelled.</summary>
    public const string CountCancel = "inventory.count_cancel";
}
