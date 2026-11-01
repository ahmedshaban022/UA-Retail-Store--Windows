namespace StoreFlow.Application.Inventory;

/// <summary>
/// The stable error codes the inventory and purchase layer answers with. The interface maps a code
/// to a localised message, so the wording lives in the resource files and never in the handlers.
/// </summary>
public static class InventoryErrorCodes
{
    /// <summary>The movement would take the sellable quantity below zero.</summary>
    public const string NegativeStock = "inventory.negative_stock";

    /// <summary>The movement would take the damaged quantity below zero.</summary>
    public const string NegativeDamaged = "inventory.negative_damaged";

    /// <summary>The referenced record does not exist.</summary>
    public const string NotFound = "inventory.not_found";

    /// <summary>The document is no longer editable.</summary>
    public const string NotDraft = "inventory.not_draft";

    /// <summary>The document is not posted.</summary>
    public const string NotPosted = "inventory.not_posted";

    /// <summary>The document has already been reversed.</summary>
    public const string AlreadyReversed = "inventory.already_reversed";

    /// <summary>A referenced record does not exist.</summary>
    public const string InvalidReference = "inventory.invalid_reference";

    /// <summary>The discount is negative or larger than the document.</summary>
    public const string InvalidDiscount = "inventory.invalid_discount";

    /// <summary>The document carries no lines.</summary>
    public const string EmptyDocument = "inventory.empty_document";

    /// <summary>The count carries no lines.</summary>
    public const string EmptyCount = "inventory.empty_count";

    /// <summary>The product already carries stock movements.</summary>
    public const string AlreadyStocked = "inventory.already_stocked";

    /// <summary>The count is not waiting for approval.</summary>
    public const string NotAwaitingApproval = "inventory.not_awaiting_approval";

    /// <summary>The count is not approved.</summary>
    public const string NotApproved = "inventory.not_approved";

    /// <summary>The count has already been posted.</summary>
    public const string AlreadyPosted = "inventory.already_posted";

    /// <summary>The approval request does not exist.</summary>
    public const string ApprovalNotFound = "inventory.approval_not_found";

    /// <summary>The approval request has already been decided.</summary>
    public const string ApprovalNotPending = "inventory.approval_not_pending";

    /// <summary>The approver is the person who asked for the change.</summary>
    public const string SelfApproval = "inventory.self_approval";

    /// <summary>Nobody is signed in to own the request.</summary>
    public const string NoActor = "inventory.no_actor";
}
