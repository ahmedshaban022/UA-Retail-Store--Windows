namespace StoreFlow.Domain.Entities;

/// <summary>A supplier of goods. Purchases record goods receipt and cost, not accounts payable.</summary>
public sealed class Supplier
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional Arabic display name.</summary>
    public string? NameAr { get; set; }

    public string? Phone { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public ICollection<PurchaseDocument> PurchaseDocuments { get; } = new List<PurchaseDocument>();
}
