using StoreFlow.Application.Pos;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Shows the till's dialogs. The window is created here, so the screen logic never has to know
/// what a dialog looks like.
/// </summary>
public interface IPosDialogs
{
    /// <summary>
    /// Shows the receipt of a sale, and lets the cashier print it again. Printing the receipt never
    /// sells anything.
    /// </summary>
    /// <param name="receipt">The sale the receipt is for.</param>
    /// <param name="isCopy">True when the receipt is a reprint.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the receipt is closed.</returns>
    Task ShowReceiptAsync(SaleReceipt receipt, bool isCopy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the cashier for the name to put on a cart that is being set aside.
    /// </summary>
    /// <param name="title">The title of the prompt.</param>
    /// <param name="label">The label of the field.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The name, or null when the cashier changed their mind.</returns>
    Task<string?> AskForLabelAsync(string title, string label, CancellationToken cancellationToken = default);
}
