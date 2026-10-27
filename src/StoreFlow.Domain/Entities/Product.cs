namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Money;

/// <summary>
/// A stock-tracked product. A product referenced by a posted financial or inventory document is never
/// hard-deleted — it is deactivated (PRD §5.2 business rules).
/// </summary>
public sealed class Product
{
    public int Id { get; set; }

    /// <summary>Unique stock-keeping unit code.</summary>
    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional Arabic display name.</summary>
    public string? NameAr { get; set; }

    public string? Description { get; set; }

    public int? CategoryId { get; set; }

    public Category? Category { get; set; }

    public int UnitId { get; set; }

    public Unit? Unit { get; set; }

    public int TaxCategoryId { get; set; }

    public TaxCategory? TaxCategory { get; set; }

    /// <summary>The current selling price. Changing it never alters a posted invoice.</summary>
    public Money SalePrice { get; set; }

    /// <summary>
    /// The opening or most recently entered acquisition cost. It is a reference for new stock only;
    /// posted invoices carry their own cost snapshot.
    /// </summary>
    public Money CostPrice { get; set; }

    /// <summary>Reorder level, scaled by the product unit's precision.</summary>
    public long ReorderLevelScaled { get; set; }

    /// <summary>The primary barcode, stored as text so leading zeros survive.</summary>
    public string? Barcode { get; set; }

    /// <summary>Relative path of the locally stored product image, included in backup scope.</summary>
    public string? ImagePath { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public ICollection<ProductBarcode> Barcodes { get; } = new List<ProductBarcode>();
}
