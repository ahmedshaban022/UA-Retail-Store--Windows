namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Money;

/// <summary>
/// A line on a sales invoice. Every commercial value on the line is an immutable snapshot taken at
/// posting time, so a later price or cost change can never rewrite history (PRD §5.5).
/// </summary>
public sealed class SalesInvoiceLine
{
    public int Id { get; set; }

    public int SalesInvoiceId { get; set; }

    public SalesInvoice? SalesInvoice { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>The product name at posting time.</summary>
    public string NameSnapshot { get; set; } = string.Empty;

    /// <summary>The SKU at posting time.</summary>
    public string SkuSnapshot { get; set; } = string.Empty;

    /// <summary>The barcode at posting time.</summary>
    public string? BarcodeSnapshot { get; set; }

    /// <summary>The selling unit name at posting time.</summary>
    public string UnitSnapshot { get; set; } = string.Empty;

    public long QuantityScaled { get; set; }

    public int QuantityScale { get; set; }

    /// <summary>The unit price charged.</summary>
    public Money UnitPrice { get; set; }

    /// <summary>The discount amount applied to this line.</summary>
    public Money Discount { get; set; }

    /// <summary>
    /// The part of the whole-sale discount that was spread onto this line. It is kept beside the
    /// line's own discount so that the invoice foots: the sum of the line discounts and the sum of
    /// the invoice-discount shares add up to the discount on the invoice header.
    /// </summary>
    public Money InvoiceDiscountShare { get; set; }

    /// <summary>The discount percentage that produced <see cref="Discount"/>, scaled by 10 000.</summary>
    public long DiscountPercentScaled { get; set; }

    /// <summary>The tax charged on this line.</summary>
    public Money Tax { get; set; }

    /// <summary>The line total: quantity times unit price, less discount, plus tax.</summary>
    public Money Total { get; set; }

    /// <summary>
    /// The cost of goods sold per unit, captured from the moving average at the moment of sale.
    /// </summary>
    public Money UnitCostSnapshot { get; set; }

    /// <summary>The tax rate in basis points that produced <see cref="Tax"/>.</summary>
    public int TaxRateSnapshotBasisPoints { get; set; }

    /// <summary>Whether the unit price included tax at posting time.</summary>
    public bool TaxPriceIncludedSnapshot { get; set; }

    /// <summary>
    /// The cumulative quantity already returned against this line. Kept as a counter so the
    /// "cumulative returns cannot exceed the original sold quantity" rule is a single comparison
    /// inside the posting transaction.
    /// </summary>
    public long ReturnedQuantityScaled { get; set; }

    public ICollection<SalesReturnLine> ReturnLines { get; } = new List<SalesReturnLine>();
}
