namespace StoreFlow.Application.Inventory;

using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

/// <summary>
/// The read models the inventory and purchase screens bind to. Every value is already formatted for
/// display: quantities are decimals in the product's own unit, and money is an exact amount.
/// </summary>

/// <summary>A line the owner typed on a purchase document.</summary>
/// <param name="ProductId">The product the line is for.</param>
/// <param name="Quantity">The quantity received, in the product's own unit.</param>
/// <param name="UnitCost">The acquisition cost per unit for this line.</param>
/// <param name="Tax">The tax charged on this line.</param>
/// <param name="Notes">The free-text note on the line.</param>
public sealed record PurchaseLineInput(
    int ProductId,
    decimal Quantity,
    Money UnitCost,
    Money Tax,
    string? Notes = null);

/// <summary>A line on a saved purchase document.</summary>
/// <param name="Id">The line identifier.</param>
/// <param name="ProductId">The product the line is for.</param>
/// <param name="Sku">The product's stock-keeping unit code.</param>
/// <param name="ProductName">The product's name.</param>
/// <param name="Quantity">The quantity received, in the product's own unit.</param>
/// <param name="QuantityScale">The precision of the product's unit.</param>
/// <param name="UnitCost">The acquisition cost per unit.</param>
/// <param name="Tax">The tax charged on the line.</param>
/// <param name="Total">The line total: quantity times unit cost, plus tax.</param>
/// <param name="Notes">The free-text note on the line.</param>
public sealed record PurchaseLineView(
    int Id,
    int ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    int QuantityScale,
    Money UnitCost,
    Money Tax,
    Money Total,
    string? Notes);

/// <summary>A purchase document in the register.</summary>
/// <param name="Id">The document identifier.</param>
/// <param name="Number">The sequential document number.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="SupplierId">The supplier, when one is set.</param>
/// <param name="SupplierName">The supplier's name.</param>
/// <param name="Status">The document's lifecycle state.</param>
/// <param name="LineCount">How many lines the document carries.</param>
/// <param name="TotalQuantity">The sum of the line quantities.</param>
/// <param name="Total">The document total.</param>
/// <param name="PostedUtc">When the document was posted, or null while it is a draft.</param>
/// <param name="ReversesDocumentId">The document this one reverses, when it is a reversal.</param>
public sealed record PurchaseSummary(
    int Id,
    string Number,
    DateOnly BusinessDate,
    int? SupplierId,
    string? SupplierName,
    DocumentStatus Status,
    int LineCount,
    decimal TotalQuantity,
    Money Total,
    DateTimeOffset? PostedUtc,
    int? ReversesDocumentId);

/// <summary>A purchase document with its lines.</summary>
public sealed record PurchaseDetails(
    int Id,
    string Number,
    DateOnly BusinessDate,
    int? SupplierId,
    string? SupplierName,
    DocumentStatus Status,
    Money Subtotal,
    Money Discount,
    Money Tax,
    Money Total,
    DateTimeOffset? PostedUtc,
    string? Notes,
    int? ReversesDocumentId,
    IReadOnlyList<PurchaseLineView> Lines);

/// <summary>A product's stock position.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">The product's stock-keeping unit code.</param>
/// <param name="Name">The product's name.</param>
/// <param name="NameAr">The product's Arabic name.</param>
/// <param name="UnitName">The selling unit's name.</param>
/// <param name="QuantityScale">The precision of the selling unit.</param>
/// <param name="QuantityOnHand">The sellable quantity on hand.</param>
/// <param name="DamagedQuantity">The damaged quantity, tracked separately.</param>
/// <param name="AverageUnitCost">The moving weighted average cost per base unit.</param>
/// <param name="StockValue">The quantity times the average cost.</param>
/// <param name="ReorderLevel">The configured reorder level.</param>
/// <param name="IsBelowReorderLevel">True when the quantity has reached the reorder level.</param>
/// <param name="IsActive">False when the product is deactivated.</param>
public sealed record StockBalanceSummary(
    int ProductId,
    string Sku,
    string Name,
    string? NameAr,
    string UnitName,
    int QuantityScale,
    decimal QuantityOnHand,
    decimal DamagedQuantity,
    Money AverageUnitCost,
    Money StockValue,
    decimal ReorderLevel,
    bool IsBelowReorderLevel,
    bool IsActive);

/// <summary>A row of the stock ledger.</summary>
public sealed record StockMovementSummary(
    int Id,
    int ProductId,
    string Sku,
    string ProductName,
    StockMovementType MovementType,
    StockDisposition Disposition,
    DateOnly BusinessDate,
    decimal QuantityDelta,
    int QuantityScale,
    Money UnitCost,
    decimal RunningBalance,
    string? Reason,
    DateTimeOffset PostedUtc,
    int? DocumentId);

/// <summary>A product at or below its reorder level.</summary>
public sealed record LowStockItem(
    int ProductId,
    string Sku,
    string Name,
    string? NameAr,
    string UnitName,
    int QuantityScale,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    Money AverageUnitCost,
    Money StockValue);

/// <summary>The inventory valuation at a report date.</summary>
/// <param name="AsOfDate">The report date.</param>
/// <param name="ProductCount">How many products carry a balance.</param>
/// <param name="TotalQuantity">The total sellable quantity across every product.</param>
/// <param name="TotalValue">The total stock value at the moving average cost.</param>
/// <param name="LowStockCount">How many products are at or below their reorder level.</param>
public sealed record InventoryValuation(
    DateOnly AsOfDate,
    int ProductCount,
    decimal TotalQuantity,
    Money TotalValue,
    int LowStockCount);

/// <summary>A counted product inside a stock count session.</summary>
public sealed record StockCountLineInput(
    int ProductId,
    decimal CountedQuantity,
    string? Reason = null);

/// <summary>A counted product, as shown on the count screen.</summary>
public sealed record StockCountLineView(
    int Id,
    int ProductId,
    string Sku,
    string ProductName,
    decimal ExpectedQuantity,
    decimal CountedQuantity,
    decimal Difference,
    int QuantityScale,
    Money UnitCost,
    Money DifferenceValue,
    string? Reason);

/// <summary>A stock count session in the list.</summary>
public sealed record StockCountSummary(
    int Id,
    string Number,
    DateOnly BusinessDate,
    StockCountStatus Status,
    int LineCount,
    decimal DifferenceQuantity,
    Money DifferenceValue,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ApprovedUtc,
    string? Reason);

/// <summary>A stock count session with its lines.</summary>
public sealed record StockCountDetails(
    int Id,
    string Number,
    DateOnly BusinessDate,
    StockCountStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ApprovedUtc,
    string? Reason,
    IReadOnlyList<StockCountLineView> Lines);

/// <summary>The result of asking for a stock adjustment.</summary>
/// <param name="Posted">True when the adjustment reached the ledger straight away.</param>
/// <param name="ApprovalRequestId">The pending approval, when one was raised.</param>
/// <param name="MovementId">The ledger movement, when the adjustment was posted.</param>
/// <param name="QuantityDelta">The signed quantity the adjustment carries.</param>
/// <param name="ValueImpact">The signed value the adjustment carries.</param>
public sealed record StockAdjustmentOutcome(
    bool Posted,
    int? ApprovalRequestId,
    int? MovementId,
    decimal QuantityDelta,
    Money ValueImpact);

/// <summary>A product the owner can pick on an inventory screen.</summary>
/// <param name="Id">The product identifier.</param>
/// <param name="Sku">The stock-keeping unit code.</param>
/// <param name="Name">The product name.</param>
/// <param name="NameAr">The Arabic name, when one is set.</param>
/// <param name="UnitName">The selling unit's name.</param>
/// <param name="QuantityScale">The precision of the selling unit.</param>
/// <param name="CostPrice">The current cost price, used as the default opening and adjustment cost.</param>
/// <param name="QuantityOnHand">
/// How much of the product the books say is on the shelf, in the selling unit's own scale.
/// </param>
public sealed record InventoryReferenceProduct(
    int Id,
    string Sku,
    string Name,
    string? NameAr,
    string UnitName,
    int QuantityScale,
    Money CostPrice,
    decimal QuantityOnHand = 0m);

/// <summary>A supplier the owner can pick on a purchase document.</summary>
public sealed record InventoryReferenceSupplier(
    int Id,
    string Name,
    string? NameAr);

/// <summary>The pickers the inventory screens need.</summary>
public sealed record InventoryReferenceData(
    IReadOnlyList<InventoryReferenceProduct> Products,
    IReadOnlyList<InventoryReferenceSupplier> Suppliers);
