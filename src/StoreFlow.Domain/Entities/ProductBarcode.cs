namespace StoreFlow.Domain.Entities;

/// <summary>
/// An additional barcode for a product. A barcode belongs to at most one active product, so the
/// column carries a unique index (PRD §5.2 PRD-08).
/// </summary>
public sealed class ProductBarcode
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Stored as text; leading zeros are preserved.</summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>True for the barcode shown on the product label and used as the primary lookup.</summary>
    public bool IsPrimary { get; set; }
}
