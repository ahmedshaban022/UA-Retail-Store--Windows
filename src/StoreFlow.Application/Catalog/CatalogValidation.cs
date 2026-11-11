namespace StoreFlow.Application.Catalog;

using StoreFlow.Domain.Money;
using StoreFlow.Domain.Quantity;

/// <summary>A problem found in a value the owner entered.</summary>
/// <param name="Field">The field the problem is in.</param>
/// <param name="Code">A stable code, so the interface can react without parsing the message.</param>
/// <param name="Message">An actionable message.</param>
public sealed record ValidationIssue(string Field, string Code, string Message);

/// <summary>
/// The rules a catalogue record must satisfy, kept in one place so the editor, the importer and the
/// command handlers cannot drift apart. Pure: it reads no database and touches no file.
/// </summary>
public static class CatalogValidation
{
    /// <summary>The longest a stock-keeping unit code may be.</summary>
    public const int MaxSkuLength = 64;

    /// <summary>The longest a product name may be.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The longest an Arabic name may be.</summary>
    public const int MaxArabicNameLength = 200;

    /// <summary>The longest a barcode may be.</summary>
    public const int MaxBarcodeLength = 64;

    /// <summary>The longest a category name may be.</summary>
    public const int MaxCategoryNameLength = 120;

    /// <summary>The longest a unit name may be.</summary>
    public const int MaxUnitNameLength = 60;

    /// <summary>The longest a supplier name may be.</summary>
    public const int MaxSupplierNameLength = 160;

    /// <summary>The longest a contact number may be.</summary>
    public const int MaxPhoneLength = 40;

    /// <summary>The longest a description may be.</summary>
    public const int MaxDescriptionLength = 1000;

    /// <summary>The longest a note may be.</summary>
    public const int MaxNotesLength = 1000;

    /// <summary>The error code for a missing name.</summary>
    public const string RequiredCode = "catalog.required";

    /// <summary>The error code for a value that is too long.</summary>
    public const string TooLongCode = "catalog.too_long";

    /// <summary>The error code for a value that is not a number.</summary>
    public const string NotANumberCode = "catalog.not_a_number";

    /// <summary>The error code for a negative price.</summary>
    public const string NegativePriceCode = "catalog.negative_price";

    /// <summary>The error code for a negative quantity.</summary>
    public const string NegativeQuantityCode = "catalog.negative_quantity";

    /// <summary>The error code for a malformed barcode.</summary>
    public const string InvalidBarcodeCode = "catalog.invalid_barcode";

    /// <summary>The error code for a duplicate barcode.</summary>
    public const string DuplicateBarcodeCode = "catalog.duplicate_barcode";

    /// <summary>The error code for a duplicate stock-keeping unit code.</summary>
    public const string DuplicateSkuCode = "catalog.duplicate_sku";

    /// <summary>The error code for an out-of-range unit precision.</summary>
    public const string InvalidPrecisionCode = "catalog.invalid_precision";

    /// <summary>The error code for a reference record that does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <summary>
    /// The error code for a change to the selling unit of a product that already carries stock.
    /// </summary>
    /// <remarks>
    /// The stock a product holds is written down as a scaled number whose meaning depends on the
    /// precision of the selling unit. Changing the unit after the goods have moved would leave
    /// every number in the shop's books standing for a different quantity — a shelf of ten
    /// kilograms read as ten thousand pieces — so the change is refused.
    /// </remarks>
    public const string UnitLockedCode = "catalog.unit_locked";

    /// <summary>Checks a product.</summary>
    /// <param name="sku">The stock-keeping unit code.</param>
    /// <param name="name">The product name.</param>
    /// <param name="nameAr">The Arabic name.</param>
    /// <param name="description">The description.</param>
    /// <param name="salePrice">The selling price.</param>
    /// <param name="costPrice">The cost price.</param>
    /// <param name="reorderLevel">The reorder level, in the unit's own terms.</param>
    /// <param name="barcode">The primary barcode.</param>
    /// <param name="additionalBarcodes">The other barcodes.</param>
    /// <param name="unitPrecision">The precision of the selling unit.</param>
    /// <returns>Every problem found, in field order.</returns>
    public static IReadOnlyList<ValidationIssue> ValidateProduct(
        string? sku,
        string? name,
        string? nameAr,
        string? description,
        Money salePrice,
        Money costPrice,
        decimal reorderLevel,
        string? barcode,
        IEnumerable<string> additionalBarcodes,
        int unitPrecision)
    {
        var issues = new List<ValidationIssue>();

        issues.AddRange(ValidateRequiredText("sku", sku, MaxSkuLength, "stock-keeping unit code"));
        issues.AddRange(ValidateRequiredText("name", name, MaxNameLength, "product name"));
        issues.AddRange(ValidateOptionalText("nameAr", nameAr, MaxArabicNameLength));
        issues.AddRange(ValidateOptionalText("description", description, MaxDescriptionLength));

        if (salePrice.IsNegative)
        {
            issues.Add(new ValidationIssue("salePrice", NegativePriceCode, "The selling price cannot be negative."));
        }

        if (costPrice.IsNegative)
        {
            issues.Add(new ValidationIssue("costPrice", NegativePriceCode, "The cost price cannot be negative."));
        }

        if (reorderLevel < 0m)
        {
            issues.Add(new ValidationIssue(
                "reorderLevel",
                NegativeQuantityCode,
                "The reorder level cannot be negative."));
        }
        else if (unitPrecision >= 0 && unitPrecision <= QuantityScale.MaxScale)
        {
            // The reorder level is stored scaled, so a value the unit cannot express is a problem the
            // owner has to fix rather than one that is silently rounded away.
            decimal scaled = Math.Round(reorderLevel * QuantityScale.PowerOfTen(unitPrecision), 0, MidpointRounding.AwayFromZero);

            if (decimal.Abs(scaled) > long.MaxValue)
            {
                issues.Add(new ValidationIssue(
                    "reorderLevel",
                    NegativeQuantityCode,
                    "The reorder level is too large for this unit."));
            }
        }

        issues.AddRange(ValidateBarcodes(barcode, additionalBarcodes));

        return issues;
    }

    /// <summary>Checks a category.</summary>
    /// <param name="name">The category name.</param>
    /// <param name="nameAr">The Arabic name.</param>
    /// <returns>Every problem found.</returns>
    public static IReadOnlyList<ValidationIssue> ValidateCategory(string? name, string? nameAr)
    {
        var issues = new List<ValidationIssue>();

        issues.AddRange(ValidateRequiredText("name", name, MaxCategoryNameLength, "category name"));
        issues.AddRange(ValidateOptionalText("nameAr", nameAr, MaxCategoryNameLength));

        return issues;
    }

    /// <summary>Checks a selling unit.</summary>
    /// <param name="name">The unit name.</param>
    /// <param name="nameAr">The Arabic name.</param>
    /// <param name="precision">The number of decimal places a quantity in this unit may carry.</param>
    /// <returns>Every problem found.</returns>
    public static IReadOnlyList<ValidationIssue> ValidateUnit(string? name, string? nameAr, int precision)
    {
        var issues = new List<ValidationIssue>();

        issues.AddRange(ValidateRequiredText("name", name, MaxUnitNameLength, "unit name"));
        issues.AddRange(ValidateOptionalText("nameAr", nameAr, MaxUnitNameLength));

        if (precision < 0 || precision > QuantityScale.MaxScale)
        {
            issues.Add(new ValidationIssue(
                "precision",
                InvalidPrecisionCode,
                $"The unit precision must be between 0 and {QuantityScale.MaxScale} decimal places."));
        }

        return issues;
    }

    /// <summary>Checks a supplier.</summary>
    /// <param name="name">The supplier name.</param>
    /// <param name="nameAr">The Arabic name.</param>
    /// <param name="phone">The contact number.</param>
    /// <param name="notes">The notes.</param>
    /// <returns>Every problem found.</returns>
    public static IReadOnlyList<ValidationIssue> ValidateSupplier(
        string? name,
        string? nameAr,
        string? phone,
        string? notes)
    {
        var issues = new List<ValidationIssue>();

        issues.AddRange(ValidateRequiredText("name", name, MaxSupplierNameLength, "supplier name"));
        issues.AddRange(ValidateOptionalText("nameAr", nameAr, MaxSupplierNameLength));
        issues.AddRange(ValidateOptionalText("phone", phone, MaxPhoneLength));
        issues.AddRange(ValidateOptionalText("notes", notes, MaxNotesLength));

        return issues;
    }

    /// <summary>
    /// Checks a barcode and the extra barcodes of a product. A barcode is stored as text, so leading
    /// zeros survive; what it must not carry is whitespace or a control character, because a
    /// scanner never sends one and a typo that contains one is always a typo.
    /// </summary>
    /// <param name="barcode">The primary barcode.</param>
    /// <param name="additionalBarcodes">The other barcodes.</param>
    /// <returns>Every problem found.</returns>
    public static IReadOnlyList<ValidationIssue> ValidateBarcodes(
        string? barcode,
        IEnumerable<string> additionalBarcodes)
    {
        ArgumentNullException.ThrowIfNull(additionalBarcodes);

        var issues = new List<ValidationIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            ValidateSingleBarcode("barcode", barcode, seen, issues);
        }

        int index = 0;

        foreach (string candidate in additionalBarcodes)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                index++;
                continue;
            }

            ValidateSingleBarcode($"barcode[{index}]", candidate, seen, issues);
            index++;
        }

        return issues;
    }

    private static void ValidateSingleBarcode(
        string field,
        string barcode,
        HashSet<string> seen,
        List<ValidationIssue> issues)
    {
        string trimmed = barcode.Trim();

        if (trimmed.Length > MaxBarcodeLength)
        {
            issues.Add(new ValidationIssue(
                field,
                TooLongCode,
                $"A barcode cannot be longer than {MaxBarcodeLength} characters."));
            return;
        }

        foreach (char character in trimmed)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                issues.Add(new ValidationIssue(
                    field,
                    InvalidBarcodeCode,
                    "A barcode cannot contain spaces or control characters."));
                return;
            }
        }

        if (!seen.Add(trimmed))
        {
            issues.Add(new ValidationIssue(
                field,
                DuplicateBarcodeCode,
                $"The barcode {trimmed} is listed twice for this product."));
        }
    }

    private static IEnumerable<ValidationIssue> ValidateRequiredText(
        string field,
        string? value,
        int maxLength,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return new ValidationIssue(field, RequiredCode, $"Enter a {label}.");
            yield break;
        }

        foreach (ValidationIssue issue in ValidateOptionalText(field, value, maxLength))
        {
            yield return issue;
        }
    }

    private static IEnumerable<ValidationIssue> ValidateOptionalText(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        if (value.Trim().Length > maxLength)
        {
            yield return new ValidationIssue(
                field,
                TooLongCode,
                $"This value cannot be longer than {maxLength} characters.");
        }
    }
}
