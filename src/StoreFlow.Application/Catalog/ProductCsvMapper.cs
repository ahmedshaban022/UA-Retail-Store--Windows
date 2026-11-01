namespace StoreFlow.Application.Catalog;

using System.Globalization;

/// <summary>
/// The catalogue as it stands at the moment an import is planned. The mapper reads it so a row can
/// be told apart as a creation or an update, and so a barcode that already belongs to another
/// product is caught before anything is written.
/// </summary>
/// <param name="ProductIdsBySku">Existing stock-keeping unit codes, matched without regard to case.</param>
/// <param name="BarcodeOwners">Every barcode in use, matched without regard to case, and the product that owns it.</param>
/// <param name="CategoryIdsByName">Category names, matched without regard to case.</param>
/// <param name="UnitIdsByName">Unit names, matched without regard to case.</param>
/// <param name="TaxCategoryIdsByName">Tax category names, matched without regard to case.</param>
/// <param name="DefaultUnitId">The unit a row without a unit is given.</param>
/// <param name="DefaultTaxCategoryId">The tax category a row without one is given.</param>
public sealed record CatalogSnapshot(
    IReadOnlyDictionary<string, int> ProductIdsBySku,
    IReadOnlyDictionary<string, int> BarcodeOwners,
    IReadOnlyDictionary<string, int> CategoryIdsByName,
    IReadOnlyDictionary<string, int> UnitIdsByName,
    IReadOnlyDictionary<string, int> TaxCategoryIdsByName,
    int DefaultUnitId,
    int DefaultTaxCategoryId);

/// <summary>The headings of a file, resolved against the fields the importer understands.</summary>
/// <param name="ColumnIndexes">The column index of each recognised field.</param>
/// <param name="UnmappedHeaders">The headings in the file that map to no field.</param>
public sealed record CsvColumnMap(
    IReadOnlyDictionary<string, int> ColumnIndexes,
    IReadOnlyList<string> UnmappedHeaders);

/// <summary>
/// Turns the rows of a CSV file into the products an import would write, and reports every row that
/// cannot be written and why.
/// </summary>
/// <remarks>
/// The same mapper serves the preview and the import, so what the owner is shown before pressing
/// import is exactly what the import does.
/// </remarks>
public static class ProductCsvMapper
{
    /// <summary>The field key of the stock-keeping unit code.</summary>
    public const string FieldSku = "sku";

    /// <summary>The field key of the product name.</summary>
    public const string FieldName = "name";

    /// <summary>The field key of the Arabic name.</summary>
    public const string FieldNameAr = "nameAr";

    /// <summary>The field key of the category.</summary>
    public const string FieldCategory = "category";

    /// <summary>The field key of the selling unit.</summary>
    public const string FieldUnit = "unit";

    /// <summary>The field key of the tax category.</summary>
    public const string FieldTaxCategory = "taxCategory";

    /// <summary>The field key of the selling price.</summary>
    public const string FieldSalePrice = "salePrice";

    /// <summary>The field key of the cost price.</summary>
    public const string FieldCostPrice = "costPrice";

    /// <summary>The field key of the reorder level.</summary>
    public const string FieldReorderLevel = "reorderLevel";

    /// <summary>The field key of the primary barcode.</summary>
    public const string FieldBarcode = "barcode";

    /// <summary>The field key of the additional barcodes.</summary>
    public const string FieldAdditionalBarcodes = "additionalBarcodes";

    /// <summary>The field key of the description.</summary>
    public const string FieldDescription = "description";

    /// <summary>The field key of the active flag.</summary>
    public const string FieldIsActive = "isActive";

    /// <summary>The error code for a heading the importer does not recognise.</summary>
    public const string UnknownFieldCode = "catalog.import.unknown_field";

    /// <summary>The error code for a missing required value.</summary>
    public const string MissingValueCode = "catalog.import.missing_value";

    /// <summary>The error code for a value that is not a number.</summary>
    public const string NotANumberCode = "catalog.import.not_a_number";

    /// <summary>The error code for a category that does not exist.</summary>
    public const string UnknownCategoryCode = "catalog.import.unknown_category";

    /// <summary>The error code for a unit that does not exist.</summary>
    public const string UnknownUnitCode = "catalog.import.unknown_unit";

    /// <summary>The error code for a tax category that does not exist.</summary>
    public const string UnknownTaxCategoryCode = "catalog.import.unknown_tax_category";

    /// <summary>The error code for a barcode that already belongs to another product.</summary>
    public const string BarcodeInUseCode = "catalog.import.barcode_in_use";

    /// <summary>The error code for a stock-keeping unit code that appears twice in the file.</summary>
    public const string DuplicateSkuInFileCode = "catalog.import.duplicate_sku";

    /// <summary>The error code for a stock-keeping unit code that already exists.</summary>
    public const string SkuExistsCode = "catalog.import.sku_exists";

    /// <summary>The error code for a value that is not a yes-or-no answer.</summary>
    public const string NotABooleanCode = "catalog.import.not_a_boolean";

    /// <summary>The error code for a barcode that appears twice in the file.</summary>
    public const string DuplicateBarcodeInFileCode = "catalog.import.duplicate_barcode";

    /// <summary>The fields an import understands, in the order they are exported.</summary>
    public static IReadOnlyList<string> CanonicalFields { get; } =
    [
        FieldSku,
        FieldName,
        FieldNameAr,
        FieldCategory,
        FieldUnit,
        FieldTaxCategory,
        FieldSalePrice,
        FieldCostPrice,
        FieldReorderLevel,
        FieldBarcode,
        FieldAdditionalBarcodes,
        FieldDescription,
        FieldIsActive,
    ];

    /// <summary>
    /// The headings the importer accepts for each field, beyond the canonical name. A shop owner's
    /// spreadsheet rarely uses our exact wording, and a file this application wrote carries the
    /// localised heading, which the caller passes in.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Synonyms = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [FieldSku] = ["sku", "stockkeepingunit", "code", "itemcode", "productcode"],
        [FieldName] = ["name", "productname", "product", "itemname"],
        [FieldNameAr] = ["namear", "arabicname", "arabic", "الاسم"],
        [FieldCategory] = ["category", "cat", "group", "فئة"],
        [FieldUnit] = ["unit", "uom", "sellingunit", "وحدة"],
        [FieldTaxCategory] = ["taxcategory", "tax", "الضريبة"],
        [FieldSalePrice] = ["saleprice", "price", "sellingprice", "retailprice", "سعر"],
        [FieldCostPrice] = ["costprice", "cost", "buyingprice", "purchaseprice", "تكلفة"],
        [FieldReorderLevel] = ["reorderlevel", "reorder", "minstock", "minimumstock", "حد"],
        [FieldBarcode] = ["barcode", "barcodes", "ean", "upc", "باركود"],
        [FieldAdditionalBarcodes] = ["additionalbarcodes", "otherbarcodes", "altbarcodes", "alternatebarcodes"],
        [FieldDescription] = ["description", "desc", "notes"],
        [FieldIsActive] = ["isactive", "active", "enabled", "نشط"],
    };

    /// <summary>
    /// Resolves the headings of a file against the fields the importer understands.
    /// </summary>
    /// <param name="headers">The headings read from the file.</param>
    /// <param name="aliases">The localised heading of each field, so a file this application wrote is read back.</param>
    /// <returns>The column index of every recognised field, and the headings that map to none.</returns>
    public static CsvColumnMap ResolveHeaders(
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(aliases);

        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string field in CanonicalFields)
        {
            lookup[Normalise(field)] = field;

            foreach (string synonym in Synonyms[field])
            {
                lookup.TryAdd(Normalise(synonym), field);
            }

            if (aliases.TryGetValue(field, out string? localised) && !string.IsNullOrWhiteSpace(localised))
            {
                lookup[Normalise(localised)] = field;
            }
        }

        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var unmapped = new List<string>();

        for (int index = 0; index < headers.Count; index++)
        {
            string header = headers[index];

            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (lookup.TryGetValue(Normalise(header), out string? field))
            {
                indexes.TryAdd(field, index);
            }
            else
            {
                unmapped.Add(header.Trim());
            }
        }

        return new CsvColumnMap(indexes, unmapped);
    }

    /// <summary>
    /// Reads the rows of a file into importable products, and reports the problems in each row.
    /// </summary>
    /// <param name="fileRows">Every row of the file, the heading first.</param>
    /// <param name="columnMap">The resolved headings.</param>
    /// <param name="snapshot">The catalogue as it stands now.</param>
    /// <param name="mode">How a row whose stock-keeping unit code already exists is treated.</param>
    /// <returns>One entry per data row, in file order.</returns>
    public static IReadOnlyList<ProductImportRow> BuildRows(
        IReadOnlyList<string[]> fileRows,
        CsvColumnMap columnMap,
        CatalogSnapshot snapshot,
        ProductImportMode mode)
    {
        ArgumentNullException.ThrowIfNull(fileRows);
        ArgumentNullException.ThrowIfNull(columnMap);
        ArgumentNullException.ThrowIfNull(snapshot);

        var rows = new List<ProductImportRow>();
        var skusInFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var barcodesInFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int index = 1; index < fileRows.Count; index++)
        {
            string[] cells = fileRows[index];
            int lineNumber = index + 1;

            var raw = new Dictionary<string, string>(StringComparer.Ordinal);
            var issues = new List<ImportIssue>();

            foreach (string field in CanonicalFields)
            {
                raw[field] = columnMap.ColumnIndexes.TryGetValue(field, out int cellIndex)
                    ? Cell(cells, cellIndex)
                    : string.Empty;
            }

            // A row that carries nothing at all is a blank line, not an error.
            if (raw.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string sku = raw[FieldSku].Trim();
            string name = raw[FieldName].Trim();

            if (sku.Length == 0)
            {
                issues.Add(new ImportIssue(FieldSku, MissingValueCode, "The stock-keeping unit code is missing."));
            }
            else if (skusInFile.TryGetValue(sku, out int firstLine))
            {
                issues.Add(new ImportIssue(
                    FieldSku,
                    DuplicateSkuInFileCode,
                    $"The stock-keeping unit code {sku} already appears on line {firstLine}.",
                    sku,
                    firstLine));
            }
            else
            {
                skusInFile[sku] = lineNumber;
            }

            if (name.Length == 0)
            {
                issues.Add(new ImportIssue(FieldName, MissingValueCode, "The product name is missing."));
            }

            bool isUpdate = false;
            int? existingProductId = null;

            if (sku.Length > 0 && snapshot.ProductIdsBySku.TryGetValue(sku, out int existingId))
            {
                existingProductId = existingId;

                if (mode == ProductImportMode.CreateOnly)
                {
                    issues.Add(new ImportIssue(
                        FieldSku,
                        SkuExistsCode,
                        $"A product with the stock-keeping unit code {sku} already exists.",
                        sku));
                }
                else
                {
                    isUpdate = true;
                }
            }

            int? categoryId = null;
            string category = raw[FieldCategory].Trim();

            if (category.Length > 0)
            {
                if (snapshot.CategoryIdsByName.TryGetValue(category, out int categoryIdValue))
                {
                    categoryId = categoryIdValue;
                }
                else
                {
                    issues.Add(new ImportIssue(
                        FieldCategory,
                        UnknownCategoryCode,
                        $"There is no category called {category}. Create it first, or leave the column empty.",
                        category));
                }
            }

            int unitId = snapshot.DefaultUnitId;
            string unit = raw[FieldUnit].Trim();

            if (unit.Length > 0)
            {
                if (snapshot.UnitIdsByName.TryGetValue(unit, out int unitIdValue))
                {
                    unitId = unitIdValue;
                }
                else
                {
                    issues.Add(new ImportIssue(
                        FieldUnit,
                        UnknownUnitCode,
                        $"There is no unit called {unit}. Create it first, or leave the column empty.",
                        unit));
                }
            }
            else if (unitId == 0)
            {
                issues.Add(new ImportIssue(
                    FieldUnit,
                    UnknownUnitCode,
                    "No selling unit is configured, so products cannot be imported yet."));
            }

            int taxCategoryId = snapshot.DefaultTaxCategoryId;
            string taxCategory = raw[FieldTaxCategory].Trim();

            if (taxCategory.Length > 0)
            {
                if (snapshot.TaxCategoryIdsByName.TryGetValue(taxCategory, out int taxCategoryIdValue))
                {
                    taxCategoryId = taxCategoryIdValue;
                }
                else
                {
                    issues.Add(new ImportIssue(
                        FieldTaxCategory,
                        UnknownTaxCategoryCode,
                        $"There is no tax category called {taxCategory}. Leave the column empty to use the default.",
                        taxCategory));
                }
            }
            else if (taxCategoryId == 0)
            {
                issues.Add(new ImportIssue(
                    FieldTaxCategory,
                    UnknownTaxCategoryCode,
                    "No tax category is configured, so products cannot be imported yet."));
            }

            decimal salePrice = 0m;

            if (!TryParseMoney(raw[FieldSalePrice], out salePrice))
            {
                issues.Add(new ImportIssue(
                    FieldSalePrice,
                    string.IsNullOrWhiteSpace(raw[FieldSalePrice]) ? MissingValueCode : NotANumberCode,
                    string.IsNullOrWhiteSpace(raw[FieldSalePrice])
                        ? "The selling price is missing."
                        : "The selling price is not a number."));
            }
            else if (salePrice < 0m)
            {
                // The same rule the product editor applies. An import is another way into the
                // catalogue, so it is held to the same standard.
                issues.Add(new ImportIssue(
                    FieldSalePrice,
                    CatalogValidation.NegativePriceCode,
                    "The selling price cannot be negative.",
                    salePrice.ToString(CultureInfo.InvariantCulture)));
            }

            decimal costPrice = 0m;

            if (!string.IsNullOrWhiteSpace(raw[FieldCostPrice]))
            {
                if (!TryParseMoney(raw[FieldCostPrice], out costPrice))
                {
                    issues.Add(new ImportIssue(
                        FieldCostPrice,
                        NotANumberCode,
                        "The cost price is not a number."));
                }
                else if (costPrice < 0m)
                {
                    issues.Add(new ImportIssue(
                        FieldCostPrice,
                        CatalogValidation.NegativePriceCode,
                        "The cost price cannot be negative.",
                        costPrice.ToString(CultureInfo.InvariantCulture)));
                }
            }

            decimal reorderLevel = 0m;

            if (!string.IsNullOrWhiteSpace(raw[FieldReorderLevel]))
            {
                if (!TryParseMoney(raw[FieldReorderLevel], out reorderLevel))
                {
                    issues.Add(new ImportIssue(
                        FieldReorderLevel,
                        NotANumberCode,
                        "The reorder level is not a number."));
                }
                else if (reorderLevel < 0m)
                {
                    issues.Add(new ImportIssue(
                        FieldReorderLevel,
                        CatalogValidation.NegativeQuantityCode,
                        "The reorder level cannot be negative.",
                        reorderLevel.ToString(CultureInfo.InvariantCulture)));
                }
            }

            string? barcode = null;
            string barcodeCell = raw[FieldBarcode].Trim();

            if (barcodeCell.Length > 0)
            {
                barcode = barcodeCell;
                CheckBarcode(barcode, FieldBarcode, lineNumber, barcodesInFile, snapshot, existingProductId, issues);
            }

            var additionalBarcodes = new List<string>();

            foreach (string part in SplitBarcodes(raw[FieldAdditionalBarcodes]))
            {
                additionalBarcodes.Add(part);
                CheckBarcode(part, FieldAdditionalBarcodes, lineNumber, barcodesInFile, snapshot, existingProductId, issues);
            }

            bool isActive = true;

            if (!string.IsNullOrWhiteSpace(raw[FieldIsActive]) && !TryParseBoolean(raw[FieldIsActive], out isActive))
            {
                issues.Add(new ImportIssue(
                    FieldIsActive,
                    NotABooleanCode,
                    "The active column must read yes or no."));
            }

            ResolvedProduct? resolved = null;

            if (issues.Count == 0)
            {
                resolved = new ResolvedProduct(
                    sku,
                    name,
                    NullIfEmpty(raw[FieldNameAr]),
                    NullIfEmpty(raw[FieldDescription]),
                    categoryId,
                    unitId,
                    taxCategoryId,
                    salePrice,
                    costPrice,
                    reorderLevel,
                    barcode,
                    additionalBarcodes,
                    isActive);
            }

            rows.Add(new ProductImportRow(
                lineNumber,
                raw,
                issues,
                isUpdate,
                existingProductId,
                resolved));
        }

        return rows;
    }

    /// <summary>
    /// Reduces a heading to a comparable form, so <c>Sale Price</c>, <c>sale_price</c> and
    /// <c>SALEPRICE</c> all name the same field.
    /// </summary>
    /// <param name="header">The heading to normalise.</param>
    /// <returns>The normalised heading.</returns>
    public static string Normalise(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var builder = new System.Text.StringBuilder(header.Length);

        foreach (char character in header)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static void CheckBarcode(
        string barcode,
        string field,
        int lineNumber,
        Dictionary<string, int> barcodesInFile,
        CatalogSnapshot snapshot,
        int? existingProductId,
        List<ImportIssue> issues)
    {
        if (barcodesInFile.TryGetValue(barcode, out int firstLine))
        {
            issues.Add(new ImportIssue(
                field,
                DuplicateBarcodeInFileCode,
                $"The barcode {barcode} already appears on line {firstLine}.",
                barcode,
                firstLine));
            return;
        }

        barcodesInFile[barcode] = lineNumber;

        if (snapshot.BarcodeOwners.TryGetValue(barcode, out int ownerId) && ownerId != existingProductId)
        {
            issues.Add(new ImportIssue(
                field,
                BarcodeInUseCode,
                $"The barcode {barcode} already belongs to another product.",
                barcode));
        }
    }

    private static IEnumerable<string> SplitBarcodes(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (string part in value.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0)
            {
                yield return part;
            }
        }
    }

    private static bool TryParseMoney(string text, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        // A shop owner often types the price with its currency attached. Dropping everything that
        // cannot be part of a number reads "450.00 EGP" as 450, which is what they meant.
        var builder = new System.Text.StringBuilder(trimmed.Length);

        foreach (char character in trimmed)
        {
            if (char.IsDigit(character) || character is '.' or '-' or '+')
            {
                builder.Append(character);
            }
        }

        string cleaned = builder.ToString();

        return cleaned.Length > 0
            && decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseBoolean(string text, out bool value)
    {
        value = true;

        string trimmed = text.Trim().ToLowerInvariant();

        switch (trimmed)
        {
            case "true":
            case "yes":
            case "y":
            case "1":
            case "نعم":
            case "فعال":
                value = true;
                return true;
            case "false":
            case "no":
            case "n":
            case "0":
            case "لا":
            case "غير فعال":
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static string Cell(string[] cells, int index) =>
        index >= 0 && index < cells.Length ? cells[index] : string.Empty;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
