using System.Text.Json;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Quantity;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Inventory;

/// <summary>
/// Records opening stock balances. They become auditable opening movements, never a direct edit of a
/// balance (PRD INV-06).
/// </summary>
public sealed class RecordOpeningStockCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session) : ICommandHandler<RecordOpeningStockCommand, Result<int>>
{
    /// <summary>The error code when the command carries no lines.</summary>
    public const string NoLinesCode = "inventory.no_lines";

    /// <summary>The error code when a referenced record does not exist.</summary>
    public const string InvalidReferenceCode = InventoryErrorCodes.InvalidReference;

    /// <summary>The error code when the product already carries stock movements.</summary>
    public const string AlreadyStockedCode = InventoryErrorCodes.AlreadyStocked;

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(RecordOpeningStockCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.StockAdjust, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        if (command.Lines.Count == 0)
        {
            return Result<int>.Failure(NoLinesCode, "Add at least one product to the opening stock.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<int> productIds = command.Lines.Select(line => line.ProductId).Distinct().ToList();

        List<Product> products = await context.Products
            .Include(product => product.Unit)
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        var issues = new List<InventoryValidationIssue>();

        foreach (OpeningStockLine line in command.Lines)
        {
            Product? product = products.FirstOrDefault(candidate => candidate.Id == line.ProductId);

            if (product is null)
            {
                issues.Add(new InventoryValidationIssue(
                    "lines",
                    InvalidReferenceCode,
                    "One of the chosen products no longer exists."));

                continue;
            }

            issues.AddRange(InventoryValidation.ValidateOpeningStock(
                line.Quantity,
                line.UnitCost,
                product.Unit?.Precision ?? 0));
        }

        if (issues.Count > 0)
        {
            return Result<int>.Failure(issues[0].Code, issues[0].Message);
        }

        // Opening stock is the first entry in the life of a product. A product that already carries
        // movements has a history, and its opening balance is already in that history.
        List<int> alreadyStocked = await context.StockMovements
            .Where(movement => productIds.Contains(movement.ProductId))
            .Select(movement => movement.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (alreadyStocked.Count > 0)
        {
            Product product = products.First(candidate => candidate.Id == alreadyStocked[0]);

            return Result<int>.Failure(
                AlreadyStockedCode,
                $"{product.Name} already has stock movements. Use a stock adjustment instead.");
        }

        DateTimeOffset now = clock.UtcNow;
        int movements = 0;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        foreach (OpeningStockLine input in command.Lines)
        {
            Product product = products.First(candidate => candidate.Id == input.ProductId);
            int scale = product.Unit?.Precision ?? 0;

            (StockMovement? movement, string? errorCode, string? errorMessage) = await InventoryLedger.ApplyAsync(
                context,
                product,
                scale,
                StockMovementType.Opening,
                StockDisposition.Sellable,
                new UnitQuantity(input.Quantity, scale).ToScaledLong(),
                input.UnitCost,
                command.BusinessDate,
                command.Notes,
                session.Current?.UserId,
                null,
                null,
                now);

            if (movement is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Result<int>.Failure(errorCode!, errorMessage!);
            }

            movements += 1;
        }

        await context.BeforeWriteAsync(WritePoint.RecordOpeningStock, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.RecordOpeningStockCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                InventoryAudit.OpeningStock,
                nameof(StockMovement),
                null,
                null,
                $"Recorded {movements} opening balances on {command.BusinessDate}",
                session.Current?.UserId),
            cancellationToken);

        return Result<int>.Success(movements);
    }
}

/// <summary>
/// Posts a manual stock adjustment. A large adjustment is routed to a second authorised approver
/// instead of reaching the ledger straight away (PRD §5.3, INV-05).
/// </summary>
public sealed class PostStockAdjustmentCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session) : ICommandHandler<PostStockAdjustmentCommand, Result<StockAdjustmentOutcome>>
{
    /// <summary>The error code when the referenced product does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.NotFound;

    /// <summary>The error code when nobody is signed in to own the request.</summary>
    public const string NoActorCode = InventoryErrorCodes.NoActor;

    /// <inheritdoc />
    public async Task<Result<StockAdjustmentOutcome>> HandleAsync(
        PostStockAdjustmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.StockAdjust, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<StockAdjustmentOutcome>.Failure(permitted.Error);
        }

        int? actorId = session.Current?.UserId;

        if (actorId is null)
        {
            return Result<StockAdjustmentOutcome>.Failure(NoActorCode, "Sign in to continue.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Product? product = await context.Products
            .Include(candidate => candidate.Unit)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result<StockAdjustmentOutcome>.Failure(NotFoundCode, "This product no longer exists.");
        }

        int scale = product.Unit?.Precision ?? 0;

        IReadOnlyList<InventoryValidationIssue> issues = InventoryValidation.ValidateAdjustment(
            command.QuantityDelta,
            command.UnitCost,
            command.Reason);

        if (issues.Count > 0)
        {
            return Result<StockAdjustmentOutcome>.Failure(issues[0].Code, issues[0].Message);
        }

        long deltaScaled = new UnitQuantity(command.QuantityDelta, scale).ToScaledLong();

        Money valueImpact = InventoryLedger.ValueOf(Math.Abs(deltaScaled), scale, command.UnitCost);

        if (InventoryRules.RequiresApproval(command.QuantityDelta, valueImpact))
        {
            var payload = new StockAdjustmentPayload(
                product.Id,
                command.QuantityDelta,
                command.Disposition,
                command.UnitCost.Amount,
                command.Reason.Trim());

            var request = new ApprovalRequest
            {
                Kind = ApprovalKind.StockAdjustment,
                PayloadJson = JsonSerializer.Serialize(payload),
                RequestedByUserId = actorId.Value,
                Status = ApprovalStatus.Pending,
                CreatedUtc = clock.UtcNow,
            };

            context.ApprovalRequests.Add(request);
            await context.BeforeWriteAsync(WritePoint.RequestStockAdjustmentApproval, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            await auditWriter.WriteAsync(
                new AuditEntry(
                    InventoryAudit.AdjustmentRequest,
                    nameof(ApprovalRequest),
                    request.Id.ToString(),
                    null,
                    $"{product.Name}: {command.QuantityDelta} units, value {valueImpact}",
                    actorId),
                cancellationToken);

            return Result<StockAdjustmentOutcome>.Success(new StockAdjustmentOutcome(
                false,
                request.Id,
                null,
                command.QuantityDelta,
                valueImpact));
        }

        DateTimeOffset now = clock.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        (StockMovement? movement, string? errorCode, string? errorMessage) = await InventoryLedger.ApplyAsync(
            context,
            product,
            scale,
            StockMovementType.Adjustment,
            command.Disposition,
            deltaScaled,
            command.UnitCost,
            clock.LocalToday,
            command.Reason.Trim(),
            actorId,
            null,
            null,
            now);

        if (movement is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return Result<StockAdjustmentOutcome>.Failure(errorCode!, errorMessage!);
        }

        await context.BeforeWriteAsync(WritePoint.PostStockAdjustment, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.PostStockAdjustmentCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                InventoryAudit.AdjustmentPost,
                nameof(StockMovement),
                movement.Id.ToString(),
                null,
                $"{product.Name}: {command.QuantityDelta} units, value {valueImpact}, {command.Reason.Trim()}",
                actorId),
            cancellationToken);

        return Result<StockAdjustmentOutcome>.Success(new StockAdjustmentOutcome(
            true,
            null,
            movement.Id,
            command.QuantityDelta,
            valueImpact));
    }
}

/// <summary>
/// Approves a pending large stock adjustment and posts it to the ledger.
/// </summary>
public sealed class ApproveStockAdjustmentCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session) : ICommandHandler<ApproveStockAdjustmentCommand, Result<int>>
{
    /// <summary>The error code when the approval request does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.ApprovalNotFound;

    /// <summary>The error code when the approval request is not pending.</summary>
    public const string NotPendingCode = InventoryErrorCodes.ApprovalNotPending;

    /// <summary>The error code when the approver is the person who asked for the adjustment.</summary>
    public const string SelfApprovalCode = InventoryErrorCodes.SelfApproval;

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(
        ApproveStockAdjustmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.StockApprove, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        int? actorId = session.Current?.UserId;

        if (actorId is null)
        {
            return Result<int>.Failure(InventoryErrorCodes.NoActor, "Sign in to continue.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        ApprovalRequest? request = await context.ApprovalRequests
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ApprovalRequestId, cancellationToken);

        if (request is null)
        {
            return Result<int>.Failure(NotFoundCode, "This approval request no longer exists.");
        }

        if (request.Kind != ApprovalKind.StockAdjustment)
        {
            return Result<int>.Failure(NotPendingCode, "This request is not a stock adjustment.");
        }

        if (request.Status != ApprovalStatus.Pending)
        {
            return Result<int>.Failure(NotPendingCode, "This request has already been decided.");
        }

        if (request.RequestedByUserId == actorId.Value && session.Current?.IsAdministrator != true)
        {
            return Result<int>.Failure(
                SelfApprovalCode,
                "A stock adjustment has to be approved by somebody other than the person who requested it.");
        }

        StockAdjustmentPayload? payload = JsonSerializer.Deserialize<StockAdjustmentPayload>(request.PayloadJson);

        if (payload is null)
        {
            return Result<int>.Failure(NotFoundCode, "This approval request is no longer readable.");
        }

        Product? product = await context.Products
            .Include(candidate => candidate.Unit)
            .FirstOrDefaultAsync(candidate => candidate.Id == payload.ProductId, cancellationToken);

        if (product is null)
        {
            return Result<int>.Failure(InventoryErrorCodes.NotFound, "This product no longer exists.");
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
            actorId,
            null,
            null,
            now);

        if (movement is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return Result<int>.Failure(errorCode!, errorMessage!);
        }

        request.Status = ApprovalStatus.Approved;
        request.ApprovedByUserId = actorId;
        request.DecidedUtc = now;
        request.DecisionReason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();

        await context.BeforeWriteAsync(WritePoint.ApproveStockAdjustment, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.ApproveStockAdjustmentCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                InventoryAudit.AdjustmentApprove,
                nameof(StockMovement),
                movement.Id.ToString(),
                null,
                $"{product.Name}: {payload.QuantityDelta} units approved and posted",
                actorId),
            cancellationToken);

        return Result<int>.Success(movement.Id);
    }
}

/// <summary>The payload of a pending stock adjustment approval.</summary>
/// <param name="ProductId">The product being adjusted.</param>
/// <param name="QuantityDelta">The signed quantity the adjustment carries.</param>
/// <param name="Disposition">Whether the change affects the sellable quantity or the damaged bucket.</param>
/// <param name="UnitCost">The cost per unit used to value the adjustment.</param>
/// <param name="Reason">Why the adjustment is being made.</param>
internal sealed record StockAdjustmentPayload(
    int ProductId,
    decimal QuantityDelta,
    StockDisposition Disposition,
    decimal UnitCost,
    string Reason);
