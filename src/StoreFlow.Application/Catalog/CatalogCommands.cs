namespace StoreFlow.Application.Catalog;

using StoreFlow.Application.Common;
using StoreFlow.Domain.Money;

/// <summary>
/// The commands the catalogue screens issue. Every command carries intent only: the handler
/// authorises, validates and commits exactly one transaction (PRD §3, §8.1).
/// </summary>

/// <summary>
/// The filter applied to the products list. Every list and export honours the same filter, so what
/// the owner sees is exactly what a CSV export contains.
/// </summary>
/// <param name="Search">Free text matched against the name, the Arabic name, the SKU and the barcodes.</param>
/// <param name="CategoryId">The category to restrict to, or null for every category.</param>
/// <param name="IncludeInactive">True to include deactivated products.</param>
public sealed record ProductFilter(string? Search = null, int? CategoryId = null, bool IncludeInactive = false);

/// <summary>
/// Creates or updates a product. Passing a product identifier updates that product; passing none
/// creates a new one.
/// </summary>
/// <param name="ProductId">The product to update, or null to create one.</param>
/// <param name="Sku">The stock-keeping unit code.</param>
/// <param name="Name">The product name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="Description">The free-text description.</param>
/// <param name="CategoryId">The category, or null for uncategorised.</param>
/// <param name="UnitId">The selling unit.</param>
/// <param name="TaxCategoryId">The tax category.</param>
/// <param name="SalePrice">The selling price.</param>
/// <param name="CostPrice">The cost price.</param>
/// <param name="ReorderLevel">The reorder level, in the unit's own terms.</param>
/// <param name="Barcode">The primary barcode, when one is set.</param>
/// <param name="AdditionalBarcodes">The other barcodes the product answers to.</param>
/// <param name="ImagePath">The stored image path, relative to the media folder.</param>
/// <param name="IsActive">False creates or leaves a deactivated product.</param>
public sealed record SaveProductCommand(
    int? ProductId,
    string Sku,
    string Name,
    string? NameAr,
    string? Description,
    int? CategoryId,
    int UnitId,
    int TaxCategoryId,
    Money SalePrice,
    Money CostPrice,
    decimal ReorderLevel,
    string? Barcode,
    IReadOnlyList<string> AdditionalBarcodes,
    string? ImagePath,
    bool IsActive) : ICommand<Result<int>>;

/// <summary>Activates or deactivates a product (PRD PRD-01).</summary>
/// <param name="ProductId">The product to change.</param>
/// <param name="IsActive">The new state.</param>
public sealed record SetProductActiveCommand(int ProductId, bool IsActive) : ICommand<Result>;

/// <summary>Creates or updates a category.</summary>
/// <param name="CategoryId">The category to update, or null to create one.</param>
/// <param name="Name">The category name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="IsActive">False creates or leaves a deactivated category.</param>
public sealed record SaveCategoryCommand(
    int? CategoryId,
    string Name,
    string? NameAr,
    bool IsActive) : ICommand<Result<int>>;

/// <summary>Activates or deactivates a category.</summary>
/// <param name="CategoryId">The category to change.</param>
/// <param name="IsActive">The new state.</param>
public sealed record SetCategoryActiveCommand(int CategoryId, bool IsActive) : ICommand<Result>;

/// <summary>Creates or updates a selling unit.</summary>
/// <param name="UnitId">The unit to update, or null to create one.</param>
/// <param name="Name">The unit name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="Precision">The number of decimal places a quantity in this unit may carry.</param>
/// <param name="IsActive">False creates or leaves a deactivated unit.</param>
public sealed record SaveUnitCommand(
    int? UnitId,
    string Name,
    string? NameAr,
    int Precision,
    bool IsActive) : ICommand<Result<int>>;

/// <summary>Activates or deactivates a selling unit.</summary>
/// <param name="UnitId">The unit to change.</param>
/// <param name="IsActive">The new state.</param>
public sealed record SetUnitActiveCommand(int UnitId, bool IsActive) : ICommand<Result>;

/// <summary>Creates or updates a supplier (PRD PUR-01).</summary>
/// <param name="SupplierId">The supplier to update, or null to create one.</param>
/// <param name="Name">The supplier name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="Phone">The contact number.</param>
/// <param name="Notes">The free-text notes.</param>
/// <param name="IsActive">False creates or leaves a deactivated supplier.</param>
public sealed record SaveSupplierCommand(
    int? SupplierId,
    string Name,
    string? NameAr,
    string? Phone,
    string? Notes,
    bool IsActive) : ICommand<Result<int>>;

/// <summary>Activates or deactivates a supplier.</summary>
/// <param name="SupplierId">The supplier to change.</param>
/// <param name="IsActive">The new state.</param>
public sealed record SetSupplierActiveCommand(int SupplierId, bool IsActive) : ICommand<Result>;

/// <summary>How an import treats a row whose SKU already exists (PRD PRD-06).</summary>
public enum ProductImportMode
{
    /// <summary>A row whose SKU already exists is refused.</summary>
    CreateOnly = 0,

    /// <summary>A row whose SKU already exists updates that product.</summary>
    UpsertBySku = 1,
}

/// <summary>
/// Imports products from a CSV file. The file is validated row by row before anything is written, and
/// the rows that cannot be imported are reported rather than silently dropped.
/// </summary>
/// <param name="FilePath">The CSV file to import.</param>
/// <param name="Mode">How a row whose SKU already exists is treated.</param>
/// <param name="HeaderAliases">The localised heading of each field, so a file this application wrote can be read back.</param>
public sealed record ImportProductsCommand(
    string FilePath,
    ProductImportMode Mode,
    IReadOnlyDictionary<string, string> HeaderAliases) : ICommand<Result<ImportSummary>>;

/// <summary>
/// Exports the filtered product list to a CSV file (PRD PRD-06, REP-10).
/// </summary>
/// <param name="FilePath">The file to write.</param>
/// <param name="Filter">The filter to honour; the export contains exactly what the list shows.</param>
/// <param name="Headers">The localised heading of each field.</param>
public sealed record ExportProductsCommand(
    string FilePath,
    ProductFilter Filter,
    IReadOnlyDictionary<string, string> Headers) : ICommand<Result<int>>;
