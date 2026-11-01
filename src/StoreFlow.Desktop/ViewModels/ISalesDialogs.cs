using StoreFlow.Application.Sales;
using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Shows the sales register's dialogs. The window is created here, so the screen logic never has to
/// know what a dialog looks like.
/// </summary>
public interface ISalesDialogs
{
    /// <summary>
    /// Asks which lines of an invoice are coming back, and on what terms.
    /// </summary>
    /// <param name="invoice">The invoice being returned against.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the operator chose, or null when they changed their mind.</returns>
    Task<ReturnDialogResult?> AskForReturnAsync(SalesInvoiceDetails invoice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for the terms of the reversal of an invoice.
    /// </summary>
    /// <param name="invoice">The invoice being reversed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the operator chose, or null when they changed their mind.</returns>
    Task<ReverseDialogResult?> AskForReverseAsync(SalesInvoiceDetails invoice, CancellationToken cancellationToken = default);
}

/// <summary>One line of the return the operator is putting together.</summary>
/// <param name="SalesInvoiceLineId">The line of the original invoice.</param>
/// <param name="Quantity">The quantity coming back, in the product's own selling unit.</param>
/// <param name="IsResellable">True when the goods go back on the shelf.</param>
public sealed record ReturnDialogLine(int SalesInvoiceLineId, decimal Quantity, bool IsResellable);

/// <summary>What the operator chose in the return dialog.</summary>
/// <param name="Lines">The lines that are coming back.</param>
/// <param name="RefundMethod">How the money goes back.</param>
/// <param name="Reason">Why the goods came back.</param>
public sealed record ReturnDialogResult(
    IReadOnlyList<ReturnDialogLine> Lines,
    PaymentMethod RefundMethod,
    string Reason);

/// <summary>What the operator chose in the reversal dialog.</summary>
/// <param name="RefundMethod">How the money goes back.</param>
/// <param name="Reason">Why the invoice is being reversed.</param>
/// <param name="IsResellable">True when the goods go back on the shelf.</param>
public sealed record ReverseDialogResult(
    PaymentMethod RefundMethod,
    string Reason,
    bool IsResellable);
