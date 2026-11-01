namespace StoreFlow.Application.Sales;

/// <summary>
/// The stable codes the sales register and the returns answer with, so the interface can localise
/// the message instead of guessing at the wording of an English sentence.
/// </summary>
public static class SalesErrorCodes
{
    /// <summary>The invoice does not exist.</summary>
    public const string InvoiceNotFound = "sales.invoice_not_found";

    /// <summary>The invoice is not a posted sale, so there is nothing to correct.</summary>
    public const string InvoiceNotPosted = "sales.invoice_not_posted";

    /// <summary>The invoice has already been reversed; a posted sale is reversed only once.</summary>
    public const string InvoiceAlreadyReversed = "sales.invoice_already_reversed";

    /// <summary>The document is itself a reversal, so it cannot be reversed again.</summary>
    public const string InvoiceIsReversal = "sales.invoice_is_reversal";

    /// <summary>The return has no lines, so there is nothing to give back.</summary>
    public const string NoLines = "sales.no_lines";

    /// <summary>The line is not one of the lines of the invoice being returned.</summary>
    public const string LineNotOnInvoice = "sales.line_not_on_invoice";

    /// <summary>The quantity is not one the product's selling unit can express.</summary>
    public const string InvalidQuantity = "sales.invalid_quantity";

    /// <summary>The quantity is larger than the quantity that was sold on that line.</summary>
    public const string QuantityExceedsSold = "sales.quantity_exceeds_sold";

    /// <summary>
    /// The quantity is larger than what is left to return on that line, because part of it has
    /// already come back.
    /// </summary>
    public const string QuantityExceedsRemaining = "sales.quantity_exceeds_remaining";

    /// <summary>The return carries no reason, and a return without a reason is not auditable.</summary>
    public const string ReasonRequired = "sales.reason_required";

    /// <summary>Nothing on the invoice is left to return.</summary>
    public const string NothingToReturn = "sales.nothing_to_return";

    /// <summary>The reversal is not allowed because the invoice cannot be reversed.</summary>
    public const string ReversalNotAllowed = "sales.reversal_not_allowed";

    /// <summary>The refund cannot be larger than what the customer paid.</summary>
    public const string RefundTooLarge = "sales.refund_too_large";

    /// <summary>The stock could not be put back on the shelf.</summary>
    public const string StockNotRestored = "sales.stock_not_restored";
}
