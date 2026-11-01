using StoreFlow.Application.Common;
using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Application.Sales;

/// <summary>
/// The commands the sales register issues. Every command carries intent only: the handler
/// authorises, validates and commits exactly one transaction (PRD §3, §8.1).
/// </summary>

/// <summary>
/// Gives part of a posted sale back to the customer: a return document, the stock that comes back,
/// and the refund that goes with it.
/// </summary>
/// <param name="OriginalInvoiceId">The posted invoice being returned against.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="Lines">The lines that are coming back.</param>
/// <param name="RefundMethod">How the money goes back.</param>
/// <param name="Reason">Why the goods came back.</param>
/// <param name="Notes">An optional free-text note on the return.</param>
public sealed record PostReturnCommand(
    int OriginalInvoiceId,
    DateOnly BusinessDate,
    IReadOnlyList<ReturnLineInput> Lines,
    PaymentMethod RefundMethod,
    string Reason,
    string? Notes = null) : ICommand<Result<RefundReceipt>>;

/// <summary>
/// Reverses a posted sale in full: the invoice is undone by a linked document, never by editing
/// it (PRD §5.7, SAL-06).
/// </summary>
/// <param name="InvoiceId">The posted invoice to reverse.</param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="RefundMethod">How the money goes back.</param>
/// <param name="Reason">Why the invoice is being reversed.</param>
/// <param name="IsResellable">True when the goods go back on the shelf; false when they are damaged.</param>
/// <param name="Notes">An optional free-text note on the reversal.</param>
public sealed record ReverseInvoiceCommand(
    int InvoiceId,
    DateOnly BusinessDate,
    PaymentMethod RefundMethod,
    string Reason,
    bool IsResellable = true,
    string? Notes = null) : ICommand<Result<RefundReceipt>>;
