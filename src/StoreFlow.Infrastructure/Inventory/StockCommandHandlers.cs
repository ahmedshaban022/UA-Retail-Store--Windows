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

        auditWriter.AddTo(
            context,
            new AuditEntry(
                InventoryAudit.OpeningStock,
                nameof(StockMovement),
                null,
                null,
                $"Recorded {movements} opening balances on {command.BusinessDate}",
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.RecordOpeningStock, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.RecordOpeningStockCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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
            command.Reason,
            scale);

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
                command.Reason.Trim(),
                $"{product.Name}: {command.QuantityDelta} units",
                valueImpact.ToMinorUnits());

            var request = new ApprovalRequest
            {
                Kind = ApprovalKind.StockAdjustment,
                PayloadJson = JsonSerializer.Serialize(payload),
                RequestedByUserId = actorId.Value,
                Status = ApprovalStatus.Pending,
                CreatedUtc = clock.UtcNow,
            };

            context.ApprovalRequests.Add(request);

            await using var approvalTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

            await context.BeforeWriteAsync(WritePoint.RequestStockAdjustmentApproval, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // A new request has no id until its row has been saved, so the audit entry joins the same
            // transaction and is made permanent together with the request it describes.
            auditWriter.AddTo(
                context,
                new AuditEntry(
                    InventoryAudit.AdjustmentRequest,
                    nameof(ApprovalRequest),
                    request.Id.ToString(),
                    null,
                    $"{product.Name}: {command.QuantityDelta} units, value {valueImpact}",
                    actorId));

            await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await approvalTransaction.CommitAsync(cancellationToken);

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

        // The movement only has an id once it has been saved, so the audit entry joins the same
        // transaction and is made permanent together with the movement it describes.
        auditWriter.AddTo(
            context,
            new AuditEntry(
                InventoryAudit.AdjustmentPost,
                nameof(StockMovement),
                movement.Id.ToString(),
                null,
                $"{product.Name}: {command.QuantityDelta} units, value {valueImpact}, {command.Reason.Trim()}",
                actorId));

        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.PostStockAdjustmentCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result<StockAdjustmentOutcome>.Success(new StockAdjustmentOutcome(
            true,
            null,
            movement.Id,
            command.QuantityDelta,
            valueImpact));
    }
}

/// <summary>The payload of a pending stock adjustment approval.</summary>
/// <param name="ProductId">The product being adjusted.</param>
/// <param name="QuantityDelta">The signed quantity the adjustment carries.</param>
/// <param name="Disposition">Whether the change affects the sellable quantity or the damaged bucket.</param>
/// <param name="UnitCost">The cost per unit used to value the adjustment.</param>
/// <param name="Reason">Why the adjustment is being made.</param>
/// <param name="Description">What the adjustment is about, in words the manager can read.</param>
/// <param name="ValueMinor">What the adjustment is worth, in minor units.</param>
internal sealed record StockAdjustmentPayload(
    int ProductId,
    decimal QuantityDelta,
    StockDisposition Disposition,
    decimal UnitCost,
    string Reason,
    string Description,
    long ValueMinor);
