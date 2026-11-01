namespace StoreFlow.Application.Inventory;

using StoreFlow.Application.Common;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;

/// <summary>
/// The commands the purchase and inventory screens issue. Every command carries intent only: the
/// handler authorises, validates and commits exactly one transaction (PRD §3, §8.1).
/// </summary>

/// <summary>
/// The filter applied to the purchase register. Every list and export honours the same filter, so
/// what the owner sees is exactly what a CSV export contains (PRD PUR-05).
/// </summary>
/// <param name="Search">Free text matched against the document number and the supplier's name.</param>
/// <param name="SupplierId">The supplier to restrict to, or null for every supplier.</param>
/// <param name="From">The first business date to include, or null for no lower bound.</param>
/// <param name="To">The last business date to include, or null for no upper bound.</param>
/// <param name="Status">The document status to restrict to, or null for every status.</param>
public sealed record PurchaseFilter(
    string? Search = null,
    int? SupplierId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    DocumentStatus? Status = null);

/// <summary>
/// Creates or updates a draft purchase document. Saving a draft changes nothing in stock; only
/// posting it does (PRD PUR-02).
/// </summary>
/// <param name="PurchaseId">The document to update, or null to create one.</param>
/// <param name="SupplierId">The supplier, or null for an unidentified supplier.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="Lines">The lines on the document.</param>
/// <param name="Discount">The discount granted on the whole document.</param>
/// <param name="Notes">The free-text note on the document.</param>
public sealed record SavePurchaseCommand(
    int? PurchaseId,
    int? SupplierId,
    DateOnly BusinessDate,
    IReadOnlyList<PurchaseLineInput> Lines,
    Money Discount,
    string? Notes) : ICommand<Result<int>>;

/// <summary>
/// Posts a draft purchase document: stock increases exactly once and every movement references the
/// document (PRD PUR-03, PUR-04).
/// </summary>
/// <param name="PurchaseId">The document to post.</param>
public sealed record PostPurchaseCommand(int PurchaseId) : ICommand<Result<int>>;

/// <summary>
/// Reverses a posted purchase document. The correction is a linked reversing document, never a
/// deletion of history (PRD PUR-06).
/// </summary>
/// <param name="PurchaseId">The posted document to reverse.</param>
/// <param name="Reason">Why the purchase is being reversed.</param>
public sealed record ReversePurchaseCommand(int PurchaseId, string Reason) : ICommand<Result<int>>;

/// <summary>
/// Exports the filtered purchase register to a CSV file (PRD PUR-05, REP-10).
/// </summary>
/// <param name="FilePath">The file to write.</param>
/// <param name="Filter">The filter to honour; the export contains exactly what the list shows.</param>
/// <param name="Headers">The localised heading of each field.</param>
public sealed record ExportPurchasesCommand(
    string FilePath,
    PurchaseFilter Filter,
    IReadOnlyDictionary<string, string> Headers) : ICommand<Result<int>>;

/// <summary>
/// Records opening stock balances. They become auditable opening movements, never a direct edit of a
/// balance (PRD INV-06).
/// </summary>
/// <param name="Lines">The opening quantities and costs.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="Notes">The free-text note on the entry.</param>
public sealed record RecordOpeningStockCommand(
    IReadOnlyList<OpeningStockLine> Lines,
    DateOnly BusinessDate,
    string? Notes) : ICommand<Result<int>>;

/// <summary>One product's opening quantity and cost.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Quantity">The opening quantity, in the product's own unit.</param>
/// <param name="UnitCost">The opening cost per unit.</param>
public sealed record OpeningStockLine(int ProductId, decimal Quantity, Money UnitCost);

/// <summary>
/// Posts a manual stock adjustment. A large adjustment is routed to a second authorised approver
/// instead of reaching the ledger straight away (PRD §5.3, INV-05).
/// </summary>
/// <param name="ProductId">The product to adjust.</param>
/// <param name="QuantityDelta">The signed quantity to add or remove.</param>
/// <param name="Disposition">Whether the change affects the sellable quantity or the damaged bucket.</param>
/// <param name="UnitCost">The cost per unit used to value the adjustment.</param>
/// <param name="Reason">Why the adjustment is being made.</param>
public sealed record PostStockAdjustmentCommand(
    int ProductId,
    decimal QuantityDelta,
    StockDisposition Disposition,
    Money UnitCost,
    string Reason) : ICommand<Result<StockAdjustmentOutcome>>;

/// <summary>
/// Approves a pending large stock adjustment and posts it to the ledger.
/// </summary>
/// <param name="ApprovalRequestId">The approval request to decide.</param>
/// <param name="Reason">The approver's note.</param>
public sealed record ApproveStockAdjustmentCommand(
    int ApprovalRequestId,
    string? Reason = null) : ICommand<Result<int>>;

/// <summary>
/// Creates or updates a stock count session. Counting never changes posted stock; the difference
/// becomes an auditable adjustment only after approval (PRD INV-05).
/// </summary>
/// <param name="StockCountId">The session to update, or null to create one.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="Lines">The counted products and their observed quantities.</param>
public sealed record SaveStockCountCommand(
    int? StockCountId,
    DateOnly BusinessDate,
    IReadOnlyList<StockCountLineInput> Lines) : ICommand<Result<int>>;

/// <summary>
/// Submits a draft count for approval. The count keeps its book quantities until it is approved and
/// posted.
/// </summary>
/// <param name="StockCountId">The session to submit.</param>
public sealed record SubmitStockCountCommand(int StockCountId) : ICommand<Result>;

/// <summary>
/// Approves a submitted count. The approver must hold the stock-approval permission and must not be
/// the person who counted (PRD §5.9).
/// </summary>
/// <param name="StockCountId">The session to approve.</param>
/// <param name="Reason">The reason for the adjustment, required at approval.</param>
public sealed record ApproveStockCountCommand(int StockCountId, string Reason) : ICommand<Result>;

/// <summary>
/// Posts an approved count, writing one correction movement per line that differs from the book
/// quantity.
/// </summary>
/// <param name="StockCountId">The session to post.</param>
public sealed record PostStockCountCommand(int StockCountId) : ICommand<Result<int>>;

/// <summary>Cancels a count that has not been posted yet.</summary>
/// <param name="StockCountId">The session to cancel.</param>
public sealed record CancelStockCountCommand(int StockCountId) : ICommand<Result>;
