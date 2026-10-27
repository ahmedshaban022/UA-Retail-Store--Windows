namespace StoreFlow.Application.Catalog;

using StoreFlow.Domain.Money;

/// <summary>
/// The read models the catalogue screens bind to. They are deliberately flat: a grid row is a value
/// to read, not an object graph to navigate.
/// </summary>

/// <summary>One row of the products list.</summary>
/// <param name="Id">The product identifier.</param>
/// <param name="Sku">The stock-keeping unit code.</param>
/// <param name="Name">The product name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="CategoryName">The category name, or null when the product is uncategorised.</param>
/// <param name="UnitName">The selling unit name.</param>
/// <param name="SalePrice">The current selling price.</param>
/// <param name="CostPrice">The current cost price.</param>
/// <param name="Barcode">The primary barcode, when one is set.</param>
/// <param name="BarcodeCount">The number of additional barcodes.</param>
/// <param name="ImagePath">The stored image path, when the product has a picture.</param>
/// <param name="IsActive">False for a deactivated product.</param>
public sealed record ProductListItem(
    int Id,
    string Sku,
    string Name,
    string? NameAr,
    string? CategoryName,
    string? UnitName,
    Money SalePrice,
    Money CostPrice,
    string? Barcode,
    int BarcodeCount,
    string? ImagePath,
    bool IsActive);

/// <summary>Everything the product editor needs to open a product.</summary>
/// <param name="Id">The product identifier.</param>
/// <param name="Sku">The stock-keeping unit code.</param>
/// <param name="Name">The product name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="Description">The free-text description.</param>
/// <param name="CategoryId">The category, or null for uncategorised.</param>
/// <param name="UnitId">The selling unit.</param>
/// <param name="TaxCategoryId">The tax category.</param>
/// <param name="SalePrice">The current selling price.</param>
/// <param name="CostPrice">The current cost price.</param>
/// <param name="ReorderLevel">The reorder level, in the unit's own terms.</param>
/// <param name="Barcode">The primary barcode, when one is set.</param>
/// <param name="AdditionalBarcodes">The other barcodes the product answers to.</param>
/// <param name="ImagePath">The stored image path, when the product has a picture.</param>
/// <param name="IsActive">False for a deactivated product.</param>
/// <param name="CreatedUtc">When the product was created.</param>
/// <param name="UpdatedUtc">When the product was last changed.</param>
public sealed record ProductDetails(
    int Id,
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
    bool IsActive,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>A page of products, with the total number of matches behind it.</summary>
/// <param name="Items">The products on this page.</param>
/// <param name="TotalCount">The number of products matching the filter in total.</param>
public sealed record ProductPage(IReadOnlyList<ProductListItem> Items, int TotalCount);

/// <summary>A category, with the number of products that use it.</summary>
/// <param name="Id">The category identifier.</param>
/// <param name="Name">The category name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="IsActive">False for a deactivated category.</param>
/// <param name="ProductCount">The number of products in this category.</param>
public sealed record CategorySummary(int Id, string Name, string? NameAr, bool IsActive, int ProductCount);

/// <summary>A selling unit, with the number of products that use it.</summary>
/// <param name="Id">The unit identifier.</param>
/// <param name="Name">The unit name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="Precision">The number of decimal places a quantity in this unit may carry.</param>
/// <param name="IsActive">False for a deactivated unit.</param>
/// <param name="ProductCount">The number of products sold in this unit.</param>
public sealed record UnitSummary(int Id, string Name, string? NameAr, int Precision, bool IsActive, int ProductCount);

/// <summary>A supplier.</summary>
/// <param name="Id">The supplier identifier.</param>
/// <param name="Name">The supplier name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="Phone">The contact number.</param>
/// <param name="Notes">The free-text notes.</param>
/// <param name="IsActive">False for a deactivated supplier.</param>
public sealed record SupplierSummary(int Id, string Name, string? NameAr, string? Phone, string? Notes, bool IsActive);

/// <summary>A reference record shown in a picker.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Name">The English name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="Precision">The number of decimal places a quantity in this unit may carry; zero for anything that is not a unit.</param>
public sealed record CatalogOption(int Id, string Name, string? NameAr, int Precision = 0);

/// <summary>One entry of the audit trail.</summary>
/// <param name="Id">The entry identifier.</param>
/// <param name="TimestampUtc">When the action happened.</param>
/// <param name="Actor">The display name of the user who acted.</param>
/// <param name="Action">The action, for example <c>product.update</c>.</param>
/// <param name="EntityType">The type of the entity the action applied to.</param>
/// <param name="EntityId">The identifier of that entity.</param>
/// <param name="Summary">A short, non-sensitive summary of the change.</param>
public sealed record AuditEntryView(
    long Id,
    DateTimeOffset TimestampUtc,
    string Actor,
    string Action,
    string EntityType,
    string? EntityId,
    string? Summary);
