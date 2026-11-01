namespace StoreFlow.Application.Inventory;

using StoreFlow.Domain.Money;

/// <summary>
/// The inventory policies that are numbers rather than code paths: when an adjustment needs a second
/// approver, and how document numbers are shaped. They live here so the handlers, the tests and the
/// interface all agree on one set of values.
/// </summary>
public static class InventoryRules
{
    /// <summary>
    /// The absolute quantity, in base units, at which a manual adjustment stops being routine and
    /// needs a second authorised approver (PRD §5.3, §5.9).
    /// </summary>
    public const decimal LargeAdjustmentQuantityThreshold = 100m;

    /// <summary>
    /// The absolute value, in the store currency, at which a manual adjustment needs a second
    /// authorised approver. The quantity threshold and this one are independent: a small quantity of
    /// an expensive item is still a large adjustment.
    /// </summary>
    public const decimal LargeAdjustmentValueThreshold = 1_000m;

    /// <summary>The prefix of a purchase document number.</summary>
    public const string PurchaseNumberPrefix = "PUR";

    /// <summary>The prefix of a stock count number.</summary>
    public const string CountNumberPrefix = "CNT";

    /// <summary>
    /// True when an adjustment of this size has to be approved by a second authorised person before it
    /// can reach the ledger.
    /// </summary>
    /// <param name="quantityDelta">The signed quantity the adjustment carries.</param>
    /// <param name="valueImpact">The signed value the adjustment carries.</param>
    /// <returns>True when the adjustment needs approval.</returns>
    public static bool RequiresApproval(decimal quantityDelta, Money valueImpact)
    {
        if (Math.Abs(quantityDelta) >= LargeAdjustmentQuantityThreshold)
        {
            return true;
        }

        return Math.Abs(valueImpact.Amount) >= LargeAdjustmentValueThreshold;
    }

    /// <summary>
    /// Builds a sequential document number, for example <c>PUR-2026-0001</c>.
    /// </summary>
    /// <param name="prefix">The document family, for example <c>PUR</c>.</param>
    /// <param name="businessDate">The document date, which decides the year segment.</param>
    /// <param name="sequence">The sequence number within that year, starting at one.</param>
    /// <returns>The document number.</returns>
    public static string BuildNumber(string prefix, DateOnly businessDate, int sequence) =>
        $"{prefix}-{businessDate.Year:D4}-{sequence:D4}";
}
