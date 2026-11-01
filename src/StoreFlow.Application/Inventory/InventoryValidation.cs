namespace StoreFlow.Application.Inventory;

using StoreFlow.Domain.Money;
using StoreFlow.Domain.Quantity;

/// <summary>A problem found in a value the owner entered on an inventory screen.</summary>
/// <param name="Field">The field the problem is in.</param>
/// <param name="Code">A stable code, so the interface can react without parsing the message.</param>
/// <param name="Message">An actionable message.</param>
public sealed record InventoryValidationIssue(string Field, string Code, string Message);

/// <summary>
/// The rules an inventory or purchase value must satisfy, kept in one place so the editor and the
/// command handlers cannot drift apart. Pure: it reads no database and touches no file.
/// </summary>
public static class InventoryValidation
{
    /// <summary>The longest a reason may be.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>The longest a note may be.</summary>
    public const int MaxNotesLength = 1000;

    /// <summary>The error code for a missing value.</summary>
    public const string RequiredCode = "inventory.required";

    /// <summary>The error code for a value that is too long.</summary>
    public const string TooLongCode = "inventory.too_long";

    /// <summary>The error code for a negative price.</summary>
    public const string NegativePriceCode = "inventory.negative_price";

    /// <summary>The error code for a quantity that is not positive.</summary>
    public const string NonPositiveQuantityCode = "inventory.non_positive_quantity";

    /// <summary>The error code for a negative quantity.</summary>
    public const string NegativeQuantityCode = "inventory.negative_quantity";

    /// <summary>The error code for a quantity that cannot be expressed in the product's unit.</summary>
    public const string QuantityTooPreciseCode = "inventory.quantity_too_precise";

    /// <summary>The error code for a document with no lines.</summary>
    public const string NoLinesCode = "inventory.no_lines";

    /// <summary>The error code for the same product appearing twice on one document.</summary>
    public const string DuplicateProductCode = "inventory.duplicate_product";

    /// <summary>
    /// Checks one purchase line.
    /// </summary>
    /// <param name="quantity">The quantity on the line.</param>
    /// <param name="unitCost">The acquisition cost per unit.</param>
    /// <param name="tax">The tax on the line.</param>
    /// <param name="unitPrecision">The precision of the product's selling unit.</param>
    /// <returns>Every problem found, in field order.</returns>
    public static IReadOnlyList<InventoryValidationIssue> ValidatePurchaseLine(
        decimal quantity,
        Money unitCost,
        Money tax,
        int unitPrecision)
    {
        var issues = new List<InventoryValidationIssue>();

        if (quantity <= 0m)
        {
            issues.Add(new InventoryValidationIssue(
                "quantity",
                NonPositiveQuantityCode,
                "The quantity must be greater than zero."));
        }
        else if (!FitsScale(quantity, unitPrecision))
        {
            issues.Add(new InventoryValidationIssue(
                "quantity",
                QuantityTooPreciseCode,
                "The quantity cannot be expressed in this product's unit."));
        }

        if (unitCost.IsNegative)
        {
            issues.Add(new InventoryValidationIssue(
                "unitCost",
                NegativePriceCode,
                "The unit cost cannot be negative."));
        }

        if (tax.IsNegative)
        {
            issues.Add(new InventoryValidationIssue(
                "tax",
                NegativePriceCode,
                "The tax cannot be negative."));
        }

        return issues;
    }

    /// <summary>
    /// Checks a whole purchase document.
    /// </summary>
    /// <param name="lines">The lines on the document.</param>
    /// <returns>Every problem found, in field order.</returns>
    public static IReadOnlyList<InventoryValidationIssue> ValidatePurchase(IReadOnlyList<PurchaseLineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var issues = new List<InventoryValidationIssue>();

        if (lines.Count == 0)
        {
            issues.Add(new InventoryValidationIssue(
                "lines",
                NoLinesCode,
                "Add at least one line to the document."));

            return issues;
        }

        var seen = new HashSet<int>();

        foreach (PurchaseLineInput line in lines)
        {
            if (!seen.Add(line.ProductId))
            {
                issues.Add(new InventoryValidationIssue(
                    "lines",
                    DuplicateProductCode,
                    "The same product cannot appear on two lines. Merge the quantities instead."));
            }
        }

        return issues;
    }

    /// <summary>
    /// Checks a manual stock adjustment.
    /// </summary>
    /// <param name="quantityDelta">The signed quantity the adjustment carries.</param>
    /// <param name="unitCost">The cost per unit used to value the adjustment.</param>
    /// <param name="reason">The reason for the adjustment.</param>
    /// <returns>Every problem found, in field order.</returns>
    public static IReadOnlyList<InventoryValidationIssue> ValidateAdjustment(
        decimal quantityDelta,
        Money unitCost,
        string? reason)
    {
        var issues = new List<InventoryValidationIssue>();

        if (quantityDelta == 0m)
        {
            issues.Add(new InventoryValidationIssue(
                "quantityDelta",
                NonPositiveQuantityCode,
                "Enter a quantity other than zero."));
        }

        if (unitCost.IsNegative)
        {
            issues.Add(new InventoryValidationIssue(
                "unitCost",
                NegativePriceCode,
                "The unit cost cannot be negative."));
        }

        issues.AddRange(ValidateReason(reason));

        return issues;
    }

    /// <summary>
    /// Checks a stock count line.
    /// </summary>
    /// <param name="countedQuantity">The physical quantity the counter observed.</param>
    /// <param name="unitPrecision">The precision of the product's selling unit.</param>
    /// <returns>Every problem found, in field order.</returns>
    public static IReadOnlyList<InventoryValidationIssue> ValidateCountLine(
        decimal countedQuantity,
        int unitPrecision)
    {
        var issues = new List<InventoryValidationIssue>();

        if (countedQuantity < 0m)
        {
            issues.Add(new InventoryValidationIssue(
                "countedQuantity",
                NegativeQuantityCode,
                "The counted quantity cannot be negative."));
        }
        else if (!FitsScale(countedQuantity, unitPrecision))
        {
            issues.Add(new InventoryValidationIssue(
                "countedQuantity",
                QuantityTooPreciseCode,
                "The counted quantity cannot be expressed in this product's unit."));
        }

        return issues;
    }

    /// <summary>
    /// Checks the reason on an adjustment, a count or a reversal. The reason is what makes the entry
    /// auditable, so it is never optional.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Every problem found.</returns>
    public static IReadOnlyList<InventoryValidationIssue> ValidateReason(string? reason)
    {
        var issues = new List<InventoryValidationIssue>();

        if (string.IsNullOrWhiteSpace(reason))
        {
            issues.Add(new InventoryValidationIssue(
                "reason",
                RequiredCode,
                "Enter a reason so the entry can be traced later."));

            return issues;
        }

        if (reason.Trim().Length > MaxReasonLength)
        {
            issues.Add(new InventoryValidationIssue(
                "reason",
                TooLongCode,
                $"The reason cannot be longer than {MaxReasonLength} characters."));
        }

        return issues;
    }

    /// <summary>
    /// Checks an opening stock entry.
    /// </summary>
    /// <param name="quantity">The opening quantity.</param>
    /// <param name="unitCost">The opening cost per unit.</param>
    /// <param name="unitPrecision">The precision of the product's selling unit.</param>
    /// <returns>Every problem found, in field order.</returns>
    public static IReadOnlyList<InventoryValidationIssue> ValidateOpeningStock(
        decimal quantity,
        Money unitCost,
        int unitPrecision)
    {
        var issues = new List<InventoryValidationIssue>();

        if (quantity <= 0m)
        {
            issues.Add(new InventoryValidationIssue(
                "quantity",
                NonPositiveQuantityCode,
                "The opening quantity must be greater than zero."));
        }
        else if (!FitsScale(quantity, unitPrecision))
        {
            issues.Add(new InventoryValidationIssue(
                "quantity",
                QuantityTooPreciseCode,
                "The opening quantity cannot be expressed in this product's unit."));
        }

        if (unitCost.IsNegative)
        {
            issues.Add(new InventoryValidationIssue(
                "unitCost",
                NegativePriceCode,
                "The opening cost cannot be negative."));
        }

        return issues;
    }

    /// <summary>
    /// True when the quantity can be written exactly in the product's unit. A quantity the unit cannot
    /// express is refused rather than silently rounded away.
    /// </summary>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrecision">The precision of the product's selling unit.</param>
    /// <returns>True when the quantity fits.</returns>
    public static bool FitsScale(decimal quantity, int unitPrecision)
    {
        if (unitPrecision is < 0 or > QuantityScale.MaxScale)
        {
            return false;
        }

        decimal scaled = quantity * QuantityScale.PowerOfTen(unitPrecision);

        if (decimal.Abs(scaled) > long.MaxValue)
        {
            return false;
        }

        return decimal.Truncate(scaled) == scaled;
    }
}
