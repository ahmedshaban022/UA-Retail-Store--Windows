namespace StoreFlow.Domain.Enumerations;

/// <summary>Lifecycle of a posted-capable business document.</summary>
public enum DocumentStatus
{
    /// <summary>Editable. No stock, money or ledger effect.</summary>
    Draft = 0,

    /// <summary>Committed to the ledger. Immutable; corrected only by a linked reversing document.</summary>
    Posted = 1,

    /// <summary>Posted and then reversed by another document.</summary>
    Reversed = 2,

    /// <summary>Explicitly cancelled while still a draft.</summary>
    Cancelled = 3,
}

/// <summary>How a customer paid. This records a declaration, never a processor authorisation.</summary>
public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Other = 2,
}

/// <summary>The kind of stock movement written to the ledger.</summary>
public enum StockMovementType
{
    /// <summary>Opening balance recorded at go-live.</summary>
    Opening = 0,

    /// <summary>Goods received against a posted purchase document.</summary>
    PurchaseReceipt = 1,

    /// <summary>Stock issued by a posted sales invoice.</summary>
    Sale = 2,

    /// <summary>Sold stock brought back as resellable.</summary>
    ReturnRestock = 3,

    /// <summary>Sold stock brought back but not resellable.</summary>
    ReturnDamaged = 4,

    /// <summary>An authorised manual correction.</summary>
    Adjustment = 5,

    /// <summary>The signed difference posted by an approved stock count.</summary>
    CountCorrection = 6,

    /// <summary>A reversing entry against a previously posted movement.</summary>
    Reversal = 7,
}

/// <summary>Whether a movement affects the sellable quantity or the damaged bucket.</summary>
public enum StockDisposition
{
    /// <summary>Affects the quantity available to sell.</summary>
    Sellable = 0,

    /// <summary>Recorded separately; never inflates the quantity available to sell (PRD INV-08).</summary>
    Damaged = 1,
}

/// <summary>The kind of operation that may need a second authorised approver.</summary>
public enum ApprovalKind
{
    DiscountOverLimit = 0,
    PriceOverride = 1,
    StockAdjustment = 2,
    DatabaseRestore = 3,
    UserManagement = 4,
}

/// <summary>State of an approval request.</summary>
public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
    Expired = 4,
}

/// <summary>State of an idempotent operation record.</summary>
public enum RequestOutcomeStatus
{
    /// <summary>The operation started; its commit may not have completed.</summary>
    Pending = 0,

    /// <summary>The operation committed successfully.</summary>
    Committed = 1,

    /// <summary>The operation failed and rolled back.</summary>
    Failed = 2,
}

/// <summary>State of a stock count session.</summary>
public enum StockCountStatus
{
    Draft = 0,
    AwaitingApproval = 1,
    Approved = 2,
    Posted = 3,
    Cancelled = 4,
}

/// <summary>Outcome of a backup or restore attempt.</summary>
public enum BackupResult
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2,
}
