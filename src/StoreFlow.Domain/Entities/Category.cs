namespace StoreFlow.Domain.Entities;

/// <summary>A product classification. Every product has a primary category or the explicit
/// "Uncategorized" value.</summary>
public sealed class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional Arabic display name.</summary>
    public string? NameAr { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public ICollection<Product> Products { get; } = new List<Product>();
}
