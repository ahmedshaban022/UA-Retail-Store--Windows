namespace StoreFlow.Domain.Entities;

/// <summary>
/// A selling unit. Its <see cref="Precision"/> decides the number of decimal places a quantity of
/// this unit may carry, and therefore how quantities are scaled in the ledger.
/// </summary>
public sealed class Unit
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional Arabic display name.</summary>
    public string? NameAr { get; set; }

    /// <summary>Decimal places allowed for quantities in this unit (0 = whole pieces).</summary>
    public int Precision { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; } = new List<Product>();
}
