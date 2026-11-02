using StoreFlow.Application.Common;
using StoreFlow.Domain.Pricing;

namespace StoreFlow.Application.Pos;

/// <summary>
/// The commands the till issues. Every command carries intent only: the handler authorises,
/// validates and commits exactly one transaction (PRD §3, §8.1).
/// </summary>

/// <summary>
/// Posts a sale: one invoice, its lines, its payments and the stock movements that go with them,
/// all committed together or not at all.
/// </summary>
/// <param name="ClientOperationId">
/// The idempotency key of the checkout. The same key posted twice is the same sale, so a double
/// click or a retry can never create two invoices (PRD POS-07).
/// </param>
/// <param name="BusinessDate">The user-facing document date.</param>
/// <param name="Lines">The lines being sold.</param>
/// <param name="InvoiceDiscount">The discount granted on the whole sale.</param>
/// <param name="Payments">The payments the customer made.</param>
/// <param name="Notes">The free-text note on the invoice.</param>
/// <param name="ApprovalRequestId">
/// The approval that lets a discount over the shop's limit through. Without it the sale is held
/// for a manager to decide, and no invoice is written.
/// </param>
public sealed record PostSaleCommand(
    Guid ClientOperationId,
    DateOnly BusinessDate,
    IReadOnlyList<SaleLineInput> Lines,
    DiscountSpec InvoiceDiscount,
    IReadOnlyList<PaymentInput> Payments,
    string? Notes = null,
    int? ApprovalRequestId = null) : ICommand<Result<SaleReceipt>>;

/// <summary>
/// Suspends the cart so the cashier can serve the next customer and come back to it. A suspended
/// cart reserves nothing and deducts nothing: it is a note, not a movement (PRD POS-09).
/// </summary>
/// <param name="Label">The name the cashier gave the cart.</param>
/// <param name="Lines">The lines to suspend.</param>
/// <param name="InvoiceDiscount">The discount granted on the whole sale.</param>
public sealed record HoldCartCommand(
    string Label,
    IReadOnlyList<SaleLineInput> Lines,
    DiscountSpec InvoiceDiscount) : ICommand<Result<HeldCartSummary>>;

/// <summary>Takes a suspended cart back onto the till.</summary>
/// <param name="HeldCartId">The suspended cart to take back.</param>
public sealed record ResumeHeldCartCommand(int HeldCartId) : ICommand<Result<HeldCartDetails>>;

/// <summary>Throws a suspended cart away without selling it.</summary>
/// <param name="HeldCartId">The suspended cart to discard.</param>
public sealed record ClearHeldCartCommand(int HeldCartId) : ICommand<Result>;
