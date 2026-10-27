namespace StoreFlow.Domain.Entities;

using StoreFlow.Domain.Enumerations;

/// <summary>
/// A request for a second authorised approver, used for discounts over the policy limit, large stock
/// adjustments, price overrides and database restores (PRD §3).
/// </summary>
public sealed class ApprovalRequest
{
    public int Id { get; set; }

    public ApprovalKind Kind { get; set; }

    /// <summary>JSON describing what is being approved.</summary>
    public string PayloadJson { get; set; } = "{}";

    public int RequestedByUserId { get; set; }

    public User? RequestedByUser { get; set; }

    public int? ApprovedByUserId { get; set; }

    public User? ApprovedByUser { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    public string? DecisionReason { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? DecidedUtc { get; set; }
}
