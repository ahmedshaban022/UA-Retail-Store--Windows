namespace StoreFlow.Application.Inventory;

using StoreFlow.Application.Common;

/// <summary>
/// The queries the purchase and inventory screens issue. A query reads and never changes anything.
/// </summary>

/// <summary>Reads the purchase register.</summary>
/// <param name="Filter">The filter to honour.</param>
/// <param name="Limit">The largest number of documents to return.</param>
public sealed record ListPurchasesQuery(PurchaseFilter Filter, int Limit = 500) : IQuery<Result<IReadOnlyList<PurchaseSummary>>>;

/// <summary>Reads one purchase document with its lines.</summary>
/// <param name="PurchaseId">The document to read.</param>
public sealed record GetPurchaseQuery(int PurchaseId) : IQuery<Result<PurchaseDetails>>;

/// <summary>Reads the stock balances.</summary>
/// <param name="Search">Free text matched against the name, the Arabic name and the stock-keeping unit code.</param>
/// <param name="OnlyLowStock">True to keep only the products at or below their reorder level.</param>
/// <param name="IncludeInactive">True to include deactivated products.</param>
public sealed record ListStockBalancesQuery(
    string? Search = null,
    bool OnlyLowStock = false,
    bool IncludeInactive = false) : IQuery<Result<IReadOnlyList<StockBalanceSummary>>>;

/// <summary>Reads the stock ledger.</summary>
/// <param name="ProductId">The product to restrict to, or null for every product.</param>
/// <param name="From">The first business date to include, or null for no lower bound.</param>
/// <param name="To">The last business date to include, or null for no upper bound.</param>
/// <param name="Limit">The largest number of movements to return.</param>
public sealed record ListStockMovementsQuery(
    int? ProductId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Limit = 1000) : IQuery<Result<IReadOnlyList<StockMovementSummary>>>;

/// <summary>Reads the products at or below their reorder level (PRD INV-03).</summary>
public sealed record ListLowStockQuery() : IQuery<Result<IReadOnlyList<LowStockItem>>>;

/// <summary>Reads the inventory valuation at a report date (PRD INV-07).</summary>
/// <param name="AsOfDate">The report date, or null for today.</param>
public sealed record GetInventoryValuationQuery(DateOnly? AsOfDate = null) : IQuery<Result<InventoryValuation>>;

/// <summary>Reads the stock count sessions.</summary>
public sealed record ListStockCountsQuery() : IQuery<Result<IReadOnlyList<StockCountSummary>>>;

/// <summary>Reads one stock count session with its lines.</summary>
/// <param name="StockCountId">The session to read.</param>
public sealed record GetStockCountQuery(int StockCountId) : IQuery<Result<StockCountDetails>>;

/// <summary>Reads the products and suppliers the inventory screens offer as pickers.</summary>
public sealed record GetInventoryReferenceDataQuery() : IQuery<Result<InventoryReferenceData>>;
