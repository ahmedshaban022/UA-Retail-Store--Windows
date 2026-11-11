using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Approvals;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Quantity;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Pos;

namespace StoreFlow.Infrastructure.Approvals;

/// <summary>The actions the approval flow writes to the audit trail.</summary>
public static class ApprovalAudit
{
    /// <summary>A discount over the limit was held for a manager.</summary>
    public const string Requested = "approval.requested";

    /// <summary>A held discount was approved.</summary>
    public const string Approved = "approval.approved";

    /// <summary>A held discount was rejected.</summary>
    public const string Rejected = "approval.rejected";

    /// <summary>An approval was used to let a discount through.</summary>
    public const string Applied = "approval.applied";
}

/// <summary>
/// Holds a discount over the shop's limit for a manager to decide.
/// </summary>
/// <remarks>
/// <para>Nothing is written to the ledger when the request is made: the till keeps the cart, the
/// request only says "this discount is waiting for a yes". The sale happens when the request is
/// approved and the cashier confirms again (PRD §3, §5.7).</para>
/// <para>Asking twice for the same cart does not make two requests: the request that is already
/// waiting is the answer, so a manager is not asked the same thing twice.</para>
/// </remarks>
public sealed class RequestDiscountApprovalCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session)
    : ICommandHandler<RequestDiscountApprovalCommand, Result<ApprovalRequestSummary>>
{
    /// <inheritdoc />
    public async Task<Result<ApprovalRequestSummary>> HandleAsync(
        RequestDiscountApprovalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.SalesPost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<ApprovalRequestSummary>.Failure(permitted.Error);
        }

        int userId = session.Current?.UserId ?? 0;

        if (userId == 0)
        {
            return Result<ApprovalRequestSummary>.Failure(
                ApprovalErrorCodes.NotFound,
                "Nobody is signed in, so there is nobody to ask.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        string fingerprint = DiscountApproval.CartFingerprint(command.Lines, command.InvoiceDiscount);

        // A request that is already waiting for this exact cart is the answer; the manager is not
        // asked the same thing twice.
        List<ApprovalRequest> waiting = await context.ApprovalRequests
            .AsNoTracking()
            .Where(request => request.Kind == ApprovalKind.DiscountOverLimit &&
                              request.Status == ApprovalStatus.Pending &&
                              request.RequestedByUserId == userId)
            .ToListAsync(cancellationToken);

        foreach (ApprovalRequest candidate in waiting)
        {
            DiscountApprovalPayload? candidatePayload = DiscountApproval.ReadPayload(candidate.PayloadJson);

            if (candidatePayload is not null && candidatePayload.Fingerprint == fingerprint)
            {
                string waitingName = await ApprovalReaders.ReadUserNameAsync(
                    context, candidate.RequestedByUserId, cancellationToken);

                return Result<ApprovalRequestSummary>.Success(
                    ApprovalReaders.ToSummary(candidate, waitingName, null));
            }
        }

        int[] productIds = command.Lines.Select(line => line.ProductId).Distinct().ToArray();

        Dictionary<int, PosProductFacts> products = await PosProductReader.LoadAsync(
            context, productIds, cancellationToken);

        PosCart cart = PosCartBuilder.Build(command.Lines, products, command.InvoiceDiscount);

        if (!cart.IsValid)
        {
            PosIssue issue = cart.Issues[0];

            return Result<ApprovalRequestSummary>.Failure(issue.Code, issue.Message);
        }

        decimal percent = PosCartBuilder.EffectiveDiscountPercent(cart.Totals.Subtotal, cart.Totals.Discount);

        var payload = new DiscountApprovalPayload(
            fingerprint,
            percent,
            cart.Totals.Discount.ToMinorUnits(),
            cart.Totals.Total.ToMinorUnits(),
            cart.Lines.Count);

        var request = new ApprovalRequest
        {
            Kind = ApprovalKind.DiscountOverLimit,
            PayloadJson = DiscountApproval.WritePayload(payload),
            RequestedByUserId = userId,
            Status = ApprovalStatus.Pending,
            CreatedUtc = clock.UtcNow,
        };

        context.ApprovalRequests.Add(request);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.BeforeWriteAsync(WritePoint.RequestDiscountApproval, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // A new request has no id until its row has been saved, so the audit entry joins the same
        // transaction and is made permanent together with the request it describes.
        auditWriter.AddTo(
            context,
            new AuditEntry(
                ApprovalAudit.Requested,
                nameof(ApprovalRequest),
                request.Id.ToString(CultureInfo.InvariantCulture),
                null,
                $"{percent.ToString("0.##", CultureInfo.InvariantCulture)}% on a cart of " +
                $"{cart.Totals.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture)}",
                userId));

        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        string requestedByName = await ApprovalReaders.ReadUserNameAsync(context, userId, cancellationToken);

        return Result<ApprovalRequestSummary>.Success(ApprovalReaders.ToSummary(request, requestedByName, null));
    }
}

/// <summary>
/// Decides on a request. The approver may not be the cashier who asked, so that the second pair of
/// eyes really is a second pair of eyes.
/// </summary>
/// <remarks>
/// <para>Not every request is decided by writing a status. A held discount is decided by the status
/// alone; the sale happens later, when the cashier confirms the cart again. A stock adjustment is
/// different: the whole point of the request is that the stock has not moved yet, so approving it
/// has to move the stock. The two kinds are therefore handled apart, and the permission that lets
/// somebody decide is the one that belongs to the kind (PRD §5.3, §5.7).</para>
/// </remarks>
public sealed class DecideApprovalRequestCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session)
    : ICommandHandler<DecideApprovalRequestCommand, Result<ApprovalRequestSummary>>
{
    /// <inheritdoc />
    public async Task<Result<ApprovalRequestSummary>> HandleAsync(
        DecideApprovalRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        int approverId = session.Current?.UserId ?? 0;

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        ApprovalRequest? request = await context.ApprovalRequests
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ApprovalRequestId, cancellationToken);

        if (request is null)
        {
            return Result<ApprovalRequestSummary>.Failure(
                ApprovalErrorCodes.NotFound,
                "That request is not waiting for anybody.");
        }

        if (request.Status != ApprovalStatus.Pending)
        {
            return Result<ApprovalRequestSummary>.Failure(
                ApprovalErrorCodes.AlreadyDecided,
                "That request has already been decided.");
        }

        // The permission that opens this request depends on what the request is about. A stock
        // adjustment is the shop's stock and is guarded by the stock approver, not by the
        // permission that guards a discount over the limit.
        Result permitted = await authorization.RequireAsync(
            PermissionFor(request.Kind),
            cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<ApprovalRequestSummary>.Failure(permitted.Error);
        }

        if (request.RequestedByUserId == approverId)
        {
            return Result<ApprovalRequestSummary>.Failure(
                ApprovalErrorCodes.SelfApproval,
                "You asked for this approval, so somebody else has to give it.");
        }

        if (request.Kind == ApprovalKind.StockAdjustment && command.Approve)
        {
            return await ApproveStockAdjustmentAsync(context, request, command, approverId, cancellationToken);
        }

        request.Status = command.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        request.ApprovedByUserId = approverId;
        request.DecisionReason = command.Reason;
        request.DecidedUtc = clock.UtcNow;

        auditWriter.AddTo(
            context,
            new AuditEntry(
                command.Approve ? ApprovalAudit.Approved : ApprovalAudit.Rejected,
                nameof(ApprovalRequest),
                request.Id.ToString(CultureInfo.InvariantCulture),
                null,
                command.Reason,
                approverId));

        await context.BeforeWriteAsync(WritePoint.DecideApprovalRequest, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        string requestedByName = await ApprovalReaders.ReadUserNameAsync(
            context, request.RequestedByUserId, cancellationToken);

        string approverName = await ApprovalReaders.ReadUserNameAsync(context, approverId, cancellationToken);

        return Result<ApprovalRequestSummary>.Success(
            ApprovalReaders.ToSummary(request, requestedByName, approverName));
    }

    /// <summary>
    /// The permission that lets somebody decide a request of the given kind.
    /// </summary>
    /// <param name="kind">The kind of request.</param>
    /// <returns>The permission the approver has to hold.</returns>
    private static Permission PermissionFor(ApprovalKind kind) => kind switch
    {
        ApprovalKind.StockAdjustment => Permission.StockApprove,
        _ => Permission.SalesDiscountOverLimit,
    };

    /// <summary>
    /// Approves a stock adjustment and writes the movement to the ledger, in the same transaction as
    /// the decision. The stock has not moved while the request was waiting, so the shelf and the
    /// books only agree again once this has run.
    /// </summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="request">The request being decided.</param>
    /// <param name="command">The decision.</param>
    /// <param name="approverId">The person deciding.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decided request.</returns>
    private async Task<Result<ApprovalRequestSummary>> ApproveStockAdjustmentAsync(
        StoreFlowDbContext context,
        ApprovalRequest request,
        DecideApprovalRequestCommand command,
        int approverId,
        CancellationToken cancellationToken)
    {
        StockAdjustmentPayload? payload = JsonSerializer.Deserialize<StockAdjustmentPayload>(request.PayloadJson);

        if (payload is null)
        {
            return Result<ApprovalRequestSummary>.Failure(
                ApprovalErrorCodes.NotFound,
                "This approval request is no longer readable.");
        }

        Product? product = await context.Products
            .Include(candidate => candidate.Unit)
            .FirstOrDefaultAsync(candidate => candidate.Id == payload.ProductId, cancellationToken);

        if (product is null)
        {
            return Result<ApprovalRequestSummary>.Failure(
                InventoryErrorCodes.NotFound,
                "This product no longer exists.");
        }

        int scale = product.Unit?.Precision ?? 0;
        DateTimeOffset now = clock.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        (StockMovement? movement, string? errorCode, string? errorMessage) = await InventoryLedger.ApplyAsync(
            context,
            product,
            scale,
            StockMovementType.Adjustment,
            payload.Disposition,
            new UnitQuantity(payload.QuantityDelta, scale).ToScaledLong(),
            new Money(payload.UnitCost),
            clock.LocalToday,
            payload.Reason,
            approverId,
            null,
            null,
            now);

        if (movement is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return Result<ApprovalRequestSummary>.Failure(errorCode!, errorMessage!);
        }

        request.Status = ApprovalStatus.Approved;
        request.ApprovedByUserId = approverId;
        request.DecisionReason = command.Reason;
        request.DecidedUtc = now;

        auditWriter.AddTo(
            context,
            new AuditEntry(
                ApprovalAudit.Approved,
                nameof(ApprovalRequest),
                request.Id.ToString(CultureInfo.InvariantCulture),
                null,
                command.Reason,
                approverId));

        // The movement only has an id once it has been saved, so the audit entry joins the same
        // transaction and is made permanent together with the movement it describes.
        auditWriter.AddTo(
            context,
            new AuditEntry(
                InventoryAudit.AdjustmentApprove,
                nameof(StockMovement),
                movement.Id.ToString(CultureInfo.InvariantCulture),
                null,
                $"{product.Name}: {payload.QuantityDelta} units approved and posted",
                approverId));

        await context.BeforeWriteAsync(WritePoint.ApproveStockAdjustment, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.ApproveStockAdjustmentCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        string requestedByName = await ApprovalReaders.ReadUserNameAsync(
            context, request.RequestedByUserId, cancellationToken);

        string approverName = await ApprovalReaders.ReadUserNameAsync(context, approverId, cancellationToken);

        return Result<ApprovalRequestSummary>.Success(
            ApprovalReaders.ToSummary(request, requestedByName, approverName));
    }
}

/// <summary>Reads the requests, the newest first.</summary>
public sealed class ListApprovalRequestsQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListApprovalRequestsQuery, Result<IReadOnlyList<ApprovalRequestSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ApprovalRequestSummary>>> HandleAsync(
        ListApprovalRequestsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesDiscountOverLimit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<ApprovalRequestSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<ApprovalRequest> requests = context.ApprovalRequests.AsNoTracking();

        if (query.Status is ApprovalStatus status)
        {
            requests = requests.Where(request => request.Status == status);
        }

        List<ApprovalRequest> rows = await requests
            // The requests are written in the order they were asked for, and the identifier is that
            // order. SQLite cannot sort by a date-and-time column, and sorting by the identifier
            // gives the same order without asking the database to do something it cannot.
            .OrderByDescending(request => request.Id)
            .Take(Math.Clamp(query.Limit, 1, 1000))
            .ToListAsync(cancellationToken);

        var summaries = new List<ApprovalRequestSummary>(rows.Count);

        foreach (ApprovalRequest request in rows)
        {
            string requestedByName = await ApprovalReaders.ReadUserNameAsync(
                context, request.RequestedByUserId, cancellationToken);

            string? approverName = request.ApprovedByUserId is int approverId
                ? await ApprovalReaders.ReadUserNameAsync(context, approverId, cancellationToken)
                : null;

            summaries.Add(ApprovalReaders.ToSummary(request, requestedByName, approverName));
        }

        return Result<IReadOnlyList<ApprovalRequestSummary>>.Success(summaries);
    }
}

/// <summary>Reads one request.</summary>
public sealed class GetApprovalRequestQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<GetApprovalRequestQuery, Result<ApprovalRequestSummary>>
{
    /// <inheritdoc />
    public async Task<Result<ApprovalRequestSummary>> HandleAsync(
        GetApprovalRequestQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesDiscountOverLimit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<ApprovalRequestSummary>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        ApprovalRequest? request = await context.ApprovalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.ApprovalRequestId, cancellationToken);

        if (request is null)
        {
            return Result<ApprovalRequestSummary>.Failure(
                ApprovalErrorCodes.NotFound,
                "That request is not waiting for anybody.");
        }

        string requestedByName = await ApprovalReaders.ReadUserNameAsync(
            context, request.RequestedByUserId, cancellationToken);

        string? approverName = request.ApprovedByUserId is int approverId
            ? await ApprovalReaders.ReadUserNameAsync(context, approverId, cancellationToken)
            : null;

        return Result<ApprovalRequestSummary>.Success(
            ApprovalReaders.ToSummary(request, requestedByName, approverName));
    }
}

/// <summary>The reading the approval handlers share.</summary>
internal static class ApprovalReaders
{
    /// <summary>Turns a request into the summary the screens show.</summary>
    internal static ApprovalRequestSummary ToSummary(
        ApprovalRequest request,
        string requestedByName,
        string? approvedByName)
    {
        DiscountApprovalPayload? payload = DiscountApproval.ReadPayload(request.PayloadJson);

        Money amount = Money.FromMinorUnits(payload?.DiscountMinor ?? 0);
        decimal percent = payload?.DiscountPercent ?? 0m;
        Money cartTotal = Money.FromMinorUnits(payload?.CartTotalMinor ?? 0);
        string subject = string.Empty;

        if (request.Kind == ApprovalKind.StockAdjustment)
        {
            // A stock adjustment is not a discount, so the columns about percentages and cart totals
            // would say nothing about it. The request says what it is about instead, and what it is
            // worth stands in for the cart total.
            StockAdjustmentPayload? adjustment = JsonSerializer.Deserialize<StockAdjustmentPayload>(request.PayloadJson);

            subject = adjustment?.Description ?? string.Empty;
            amount = Money.FromMinorUnits(adjustment?.ValueMinor ?? 0);
            percent = 0m;
            cartTotal = amount;
        }

        return new ApprovalRequestSummary(
            request.Id,
            request.Kind,
            request.Status,
            request.RequestedByUserId,
            requestedByName,
            request.ApprovedByUserId,
            approvedByName,
            request.CreatedUtc,
            request.DecidedUtc,
            request.DecisionReason,
            amount,
            percent,
            cartTotal,
            subject);
    }

    /// <summary>Reads a user's name.</summary>
    internal static async Task<string> ReadUserNameAsync(
        StoreFlowDbContext context,
        int userId,
        CancellationToken cancellationToken)
    {
        string? name = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        return name ?? string.Empty;
    }
}
