namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Shows the inventory screens' modal dialogs. The view models ask for a dialog; the presenter
/// decides what that looks like, which keeps the windows out of the screen logic.
/// </summary>
public interface IInventoryDialogs
{
    /// <summary>
    /// Opens the purchase editor for a new or an existing document.
    /// </summary>
    /// <param name="purchaseId">The document to edit, or null to create one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the owner saved the document.</returns>
    Task<bool> ShowPurchaseEditorAsync(int? purchaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the owner for a reason.
    /// </summary>
    /// <param name="title">The title of the prompt.</param>
    /// <param name="label">The label on the reason field.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reason, or null when the owner walked away.</returns>
    Task<string?> AskForReasonAsync(string title, string label, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the stock adjustment dialog.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the owner posted an adjustment.</returns>
    Task<bool> ShowStockAdjustmentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the opening stock dialog.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the owner recorded opening stock.</returns>
    Task<bool> ShowOpeningStockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the stock count dialog.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the owner saved or submitted a count.</returns>
    Task<bool> ShowStockCountAsync(CancellationToken cancellationToken = default);
}
