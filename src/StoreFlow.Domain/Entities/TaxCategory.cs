namespace StoreFlow.Domain.Entities;

/// <summary>
/// A tax treatment. Merchandise does not all carry the same tax treatment, so rates live in
/// categories rather than on the store alone. Every rate change is versioned by
/// <see cref="EffectiveFromUtc"/>; posted invoices keep their own snapshot.
/// </summary>
public sealed class TaxCategory
{
    public int Id { get; set; }

    /// <summary>Stable machine-readable code, e.g. <c>standard</c>.</summary>
    public string Code { get; set; } = "standard";

    public string Name { get; set; } = "Standard";

    /// <summary>The tax rate in basis points (1 % = 100 bp).</summary>
    public int RateBasisPoints { get; set; }

    /// <summary>True when the product's selling price already includes the tax.</summary>
    public bool PricesIncludeTax { get; set; }

    public DateTimeOffset EffectiveFromUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; } = new List<Product>();
}
