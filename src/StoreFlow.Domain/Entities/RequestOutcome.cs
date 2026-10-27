using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Domain.Entities;

/// <summary>
/// The record of an idempotent operation. The client operation id is the primary key, so a repeated
/// key can never produce a second sale (PRD POS-07), and a row left in
/// <see cref="RequestOutcomeStatus.Pending"/> after a crash reveals an uncertain commit that the
/// next start-up can resolve.
/// </summary>
public sealed class RequestOutcome
{
    /// <summary>The client-supplied idempotency key; the primary key.</summary>
    public string ClientOperationId { get; set; } = string.Empty;

    /// <summary>The kind of operation, for example <c>PostSale</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>A fingerprint of the request payload, used to detect key reuse with a different body.</summary>
    public string PayloadHash { get; set; } = string.Empty;

    public RequestOutcomeStatus Status { get; set; } = RequestOutcomeStatus.Pending;

    /// <summary>The JSON-encoded result, so a replay can return the original outcome.</summary>
    public string? ResultJson { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }
}
