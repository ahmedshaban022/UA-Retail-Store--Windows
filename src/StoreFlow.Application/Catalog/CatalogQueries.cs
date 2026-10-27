namespace StoreFlow.Application.Catalog;

using StoreFlow.Application.Common;

/// <summary>
/// The queries the catalogue screens issue. A query reads and produces a value; it never changes
/// anything.
/// </summary>

/// <summary>Searches the product list (PRD PRD-02).</summary>
/// <param name="Filter">The filter to apply.</param>
/// <param name="Skip">How many matching products to skip, for paging.</param>
/// <param name="Take">How many matching products to return.</param>
public sealed record SearchProductsQuery(ProductFilter Filter, int Skip, int Take) : IQuery<Result<ProductPage>>;

/// <summary>Reads one product for the editor.</summary>
/// <param name="ProductId">The product to read.</param>
public sealed record GetProductQuery(int ProductId) : IQuery<Result<ProductDetails>>;

/// <summary>Lists the categories.</summary>
/// <param name="IncludeInactive">True to include deactivated categories.</param>
public sealed record ListCategoriesQuery(bool IncludeInactive) : IQuery<Result<IReadOnlyList<CategorySummary>>>;

/// <summary>Lists the selling units.</summary>
/// <param name="IncludeInactive">True to include deactivated units.</param>
public sealed record ListUnitsQuery(bool IncludeInactive) : IQuery<Result<IReadOnlyList<UnitSummary>>>;

/// <summary>Lists the suppliers.</summary>
/// <param name="Search">Free text matched against the name and the Arabic name.</param>
/// <param name="IncludeInactive">True to include deactivated suppliers.</param>
public sealed record ListSuppliersQuery(string? Search, bool IncludeInactive) : IQuery<Result<IReadOnlyList<SupplierSummary>>>;

/// <summary>The reference data the product editor's pickers need.</summary>
/// <param name="Categories">The categories, active ones first.</param>
/// <param name="Units">The selling units.</param>
/// <param name="TaxCategories">The tax categories.</param>
/// <param name="DefaultUnitId">The unit a new product starts with.</param>
/// <param name="DefaultTaxCategoryId">The tax category a new product starts with.</param>
public sealed record CatalogReferenceData(
    IReadOnlyList<CatalogOption> Categories,
    IReadOnlyList<CatalogOption> Units,
    IReadOnlyList<CatalogOption> TaxCategories,
    int DefaultUnitId,
    int DefaultTaxCategoryId);

/// <summary>Reads the reference data the pickers need.</summary>
public sealed record GetCatalogReferenceDataQuery : IQuery<Result<CatalogReferenceData>>;

/// <summary>A problem found in one imported row.</summary>
/// <param name="Field">The field the problem is in.</param>
/// <param name="Code">A stable code, so the interface can react without parsing the message.</param>
/// <param name="Message">An actionable message.</param>
public sealed record ImportIssue(string Field, string Code, string Message);

/// <summary>A row resolved into the values that would be written.</summary>
/// <param name="Sku">The stock-keeping unit code.</param>
/// <param name="Name">The product name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="Description">The description.</param>
/// <param name="CategoryId">The category, or null for uncategorised.</param>
/// <param name="UnitId">The selling unit.</param>
/// <param name="TaxCategoryId">The tax category.</param>
/// <param name="SalePrice">The selling price.</param>
/// <param name="CostPrice">The cost price.</param>
/// <param name="ReorderLevel">The reorder level, in the unit's own terms.</param>
/// <param name="Barcode">The primary barcode.</param>
/// <param name="AdditionalBarcodes">The other barcodes.</param>
/// <param name="IsActive">False for a deactivated product.</param>
public sealed record ResolvedProduct(
    string Sku,
    string Name,
    string? NameAr,
    string? Description,
    int? CategoryId,
    int UnitId,
    int TaxCategoryId,
    decimal SalePrice,
    decimal CostPrice,
    decimal ReorderLevel,
    string? Barcode,
    IReadOnlyList<string> AdditionalBarcodes,
    bool IsActive);

/// <summary>One row of a CSV file, read and checked.</summary>
/// <param name="LineNumber">The line in the file, counting the header as line one.</param>
/// <param name="Raw">The values as they appear in the file, keyed by field.</param>
/// <param name="Issues">The problems found in the row.</param>
/// <param name="IsUpdate">True when the row updates a product that already exists.</param>
/// <param name="ExistingProductId">The product the row updates, when it updates one.</param>
/// <param name="Resolved">The values the row would write, when the row is valid.</param>
public sealed record ProductImportRow(
    int LineNumber,
    IReadOnlyDictionary<string, string> Raw,
    IReadOnlyList<ImportIssue> Issues,
    bool IsUpdate,
    int? ExistingProductId,
    ResolvedProduct? Resolved)
{
    /// <summary>True when the row can be imported.</summary>
    public bool IsValid => Issues.Count == 0 && Resolved is not null;
}

/// <summary>The result of reading a CSV file without writing anything.</summary>
/// <param name="Rows">Every row that was read, valid or not.</param>
/// <param name="UnmappedHeaders">The headings in the file that map to no field.</param>
/// <param name="ValidCount">The number of rows that can be imported.</param>
/// <param name="ErrorCount">The number of rows that cannot be imported.</param>
/// <param name="UpdateCount">The number of valid rows that update an existing product.</param>
/// <param name="CreateCount">The number of valid rows that create a new product.</param>
public sealed record ProductImportPreview(
    IReadOnlyList<ProductImportRow> Rows,
    IReadOnlyList<string> UnmappedHeaders,
    int ValidCount,
    int ErrorCount,
    int UpdateCount,
    int CreateCount);

/// <summary>Reads a CSV file and reports what an import would do, without writing anything (PRD PRD-06).</summary>
/// <param name="FilePath">The CSV file to read.</param>
/// <param name="Mode">How a row whose SKU already exists is treated.</param>
/// <param name="HeaderAliases">The localised heading of each field, so a file this application wrote can be read back.</param>
public sealed record PreviewProductImportQuery(
    string FilePath,
    ProductImportMode Mode,
    IReadOnlyDictionary<string, string> HeaderAliases) : IQuery<Result<ProductImportPreview>>;

/// <summary>What an import actually did.</summary>
/// <param name="Created">The number of products created.</param>
/// <param name="Updated">The number of products updated.</param>
/// <param name="Failed">The number of rows that could not be imported.</param>
public sealed record ImportSummary(int Created, int Updated, int Failed);

/// <summary>Reads the audit trail, newest first.</summary>
/// <param name="EntityType">The entity type to restrict to, or null for every type.</param>
/// <param name="Search">Free text matched against the action, the entity and the summary.</param>
/// <param name="Take">How many entries to return.</param>
public sealed record ListAuditEntriesQuery(string? EntityType, string? Search, int Take)
    : IQuery<Result<IReadOnlyList<AuditEntryView>>>;

/// <summary>Reads the audit trail of one product.</summary>
/// <param name="ProductId">The product whose history is read.</param>
/// <param name="Take">How many entries to return.</param>
public sealed record GetProductAuditQuery(int ProductId, int Take) : IQuery<Result<IReadOnlyList<AuditEntryView>>>;
