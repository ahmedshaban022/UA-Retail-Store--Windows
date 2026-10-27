namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Money;

/// <summary>A counted product inside a stock count session.</summary>
public sealed class StockCountLine
{
    public int Id { get; set; }

    public int StockCountId { get; set; }

    public StockCount? StockCount { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>The book quantity captured when the count started.</summary>
    public long ExpectedQuantityScaled { get; set; }

    /// <summary>The physical quantity the counter observed.</summary>
    public long CountedQuantityScaled { get; set; }

    public int QuantityScale { get; set; }

    /// <summary>Counted minus expected.</summary>
    public long DifferenceScaled { get; set; }

    /// <summary>The unit cost used to value the difference.</summary>
    public Money UnitCost { get; set; }

    /// <summary>The estimated value of the difference.</summary>
    public Money DifferenceValue { get; set; }

    public string? Reason { get; set; }
}
