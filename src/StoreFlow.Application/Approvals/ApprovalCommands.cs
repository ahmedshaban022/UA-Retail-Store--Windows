namespace StoreFlow.Application.Approvals;

using StoreFlow.Application.Common;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;

/// <summary>The codes the approval flow answers with.</summary>
public static class ApprovalErrorCodes
{
    /// <summary>The request does not exist.</summary>
    public const string NotFound = "approval.not_found";

    /// <summary>The request has already been decided.</summary>
    public const string AlreadyDecided = "approval.already_decided";

    /// <summary>The request is not the one the cashier is waiting for.</summary>
    public const string Mismatch = "approval.mismatch";

    /// <summary>The request has not been approved yet.</summary>
    public const string NotApproved = "approval.not_approved";

    /// <summary>The approver may not approve their own request.</summary>
    public const string SelfApproval = "approval.self_approval";
}

/// <summary>
/// An approval request, as the screens show it.
/// </summary>
/// <param name="Id">The request's id.</param>
/// <param name="Kind">What kind of approval it is.</param>
/// <param name="Status">Where the request stands.</param>
/// <param name="RequestedByUserId">Who asked for it.</param>
/// <param name="RequestedByName">The name of the person who asked for it.</param>
/// <param name="ApprovedByUserId">Who decided it, when it has been decided.</param>
/// <param name="ApprovedByName">The name of the person who decided it.</param>
/// <param name="CreatedUtc">When it was asked for.</param>
/// <param name="DecidedUtc">When it was decided, when it has been decided.</param>
/// <param name="DecisionReason">Why it was decided the way it was.</param>
/// <param name="Amount">The money that needs approving.</param>
/// <param name="Percent">The discount, as a percentage.</param>
/// <param name="CartTotal">The total of the cart the approval applies to.</param>
public sealed record ApprovalRequestSummary(
    int Id,
    ApprovalKind Kind,
    ApprovalStatus Status,
    int RequestedByUserId,
    string RequestedByName,
    int? ApprovedByUserId,
    string? ApprovedByName,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? DecidedUtc,
    string? DecisionReason,
    Money Amount,
    decimal Percent,
    Money CartTotal);

/// <summary>
/// Asks a manager to approve a discount that is over the shop's limit. The request is held; no
/// invoice is written and the cashier cannot finish the checkout until the request is approved.
/// </summary>
/// <param name="Lines">The lines of the cart the discount applies to.</param>
/// <param name="InvoiceDiscount">The discount granted on the whole sale.</param>
public sealed record RequestDiscountApprovalCommand(
    IReadOnlyList<SaleLineInput> Lines,
    DiscountSpec InvoiceDiscount) : ICommand<Result<ApprovalRequestSummary>>;

/// <summary>
/// Decides on an approval request. Only somebody who may approve a discount over the limit may
/// decide one, and never on their own request.
/// </summary>
/// <param name="ApprovalRequestId">The request to decide.</param>
/// <param name="Approve">True to approve, false to reject.</param>
/// <param name="Reason">Why, when the reason is worth writing down.</param>
public sealed record DecideApprovalRequestCommand(
    int ApprovalRequestId,
    bool Approve,
    string? Reason = null) : ICommand<Result<ApprovalRequestSummary>>;

/// <summary>Lists the approval requests, the newest first.</summary>
/// <param name="Status">The status to narrow the list to, when the list is narrowed.</param>
/// <param name="Limit">How many requests to read at most.</param>
public sealed record ListApprovalRequestsQuery(ApprovalStatus? Status = null, int Limit = 200)
    : IQuery<Result<IReadOnlyList<ApprovalRequestSummary>>>;

/// <summary>Reads one approval request.</summary>
/// <param name="ApprovalRequestId">The request to read.</param>
public sealed record GetApprovalRequestQuery(int ApprovalRequestId)
    : IQuery<Result<ApprovalRequestSummary>>;
