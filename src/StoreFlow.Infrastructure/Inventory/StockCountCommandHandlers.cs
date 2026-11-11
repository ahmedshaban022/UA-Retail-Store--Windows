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
/// Creates or updates a stock count session. Counting never changes posted stock; the difference
/// becomes an auditable adjustment only after approval (PRD INV-05).
/// </summary>
public sealed class SaveStockCountCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session) : ICommandHandler<SaveStockCountCommand, Result<int>>
{
    /// <summary>The error code when the session does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.NotFound;

    /// <summary>The error code when the session is no longer editable.</summary>
    public const string NotDraftCode = InventoryErrorCodes.NotDraft;

    /// <summary>The error code when the command carries no lines.</summary>
    public const string NoLinesCode = "inventory.no_lines";

    /// <summary>The error code when a referenced record does not exist.</summary>
    public const string InvalidReferenceCode = InventoryErrorCodes.InvalidReference;

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(SaveStockCountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.StockAdjust, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        if (command.Lines.Count == 0)
        {
            return Result<int>.Failure(NoLinesCode, "Count at least one product.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        StockCount? count = null;

        if (command.StockCountId is int stockCountId)
        {
            count = await context.StockCounts
                .Include(existing => existing.Lines)
                .FirstOrDefaultAsync(existing => existing.Id == stockCountId, cancellationToken);

            if (count is null)
            {
                return Result<int>.Failure(NotFoundCode, "This stock count no longer exists.");
            }

            if (count.Status != StockCountStatus.Draft)
            {
                return Result<int>.Failure(
                    NotDraftCode,
                    "This stock count can no longer be edited.");
            }
        }

        List<int> productIds = command.Lines.Select(line => line.ProductId).Distinct().ToList();

        List<Product> products = await context.Products
            .Include(product => product.Unit)
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        var issues = new List<InventoryValidationIssue>();

        var seen = new HashSet<int>();

        foreach (StockCountLineInput input in command.Lines)
        {
            Product? product = products.FirstOrDefault(candidate => candidate.Id == input.ProductId);

            if (product is null)
            {
                issues.Add(new InventoryValidationIssue(
                    "lines",
                    InvalidReferenceCode,
                    "One of the chosen products no longer exists."));

                continue;
            }

            if (!seen.Add(input.ProductId))
            {
                issues.Add(new InventoryValidationIssue(
                    "lines",
                    "inventory.duplicate_product",
                    "The same product cannot be counted twice."));

                continue;
            }

            issues.AddRange(InventoryValidation.ValidateCountLine(
                input.CountedQuantity,
                product.Unit?.Precision ?? 0));
        }

        if (issues.Count > 0)
        {
            return Result<int>.Failure(issues[0].Code, issues[0].Message);
        }

        DateTimeOffset now = clock.UtcNow;
        bool isNew = count is null;

        count ??= new StockCount
        {
            Number = await StockCountNumber.NextAsync(context, command.BusinessDate, cancellationToken),
            Status = StockCountStatus.Draft,
            UserId = session.Current?.UserId,
            CreatedUtc = now,
        };

        count.BusinessDate = command.BusinessDate;

        context.StockCountLines.RemoveRange(count.Lines);
        count.Lines.Clear();

        List<StockBalance> balances = await context.StockBalances
            .Where(balance => productIds.Contains(balance.ProductId))
            .ToListAsync(cancellationToken);

        foreach (StockCountLineInput input in command.Lines)
        {
            Product product = products.First(candidate => candidate.Id == input.ProductId);
            int scale = product.Unit?.Precision ?? 0;

            StockBalance? balance = balances.FirstOrDefault(candidate => candidate.ProductId == input.ProductId);

            long expected = balance?.QuantityOnHandScaled ?? 0;
            long counted = new UnitQuantity(input.CountedQuantity, scale).ToScaledLong();
            long difference = counted - expected;

            Money unitCost = balance?.AverageUnitCost ?? Money.Zero;

            var line = new StockCountLine
            {
                StockCount = count,
                ProductId = input.ProductId,
                ExpectedQuantityScaled = expected,
                CountedQuantityScaled = counted,
                QuantityScale = scale,
                DifferenceScaled = difference,
                UnitCost = unitCost,
                DifferenceValue = InventoryLedger.ValueOf(Math.Abs(difference), scale, unitCost),
                Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim(),
            };

            count.Lines.Add(line);
        }

        if (isNew)
        {
            context.StockCounts.Add(count);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await context.BeforeWriteAsync(WritePoint.SaveStockCount, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // A new count has no id until its row has been saved, so the audit entry joins the same
            // transaction and is made permanent together with the count it describes.
            auditWriter.AddTo(
                context,
                new AuditEntry(
                    InventoryAudit.CountSave,
                    nameof(StockCount),
                    count.Id.ToString(),
                    null,
                    $"{count.Number} with {count.Lines.Count} counted products",
                    session.Current?.UserId));

            await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (UniqueConstraintClash.IsClash(exception))
        {
            // The number is worked out before the transaction opens, so two counters saving at the
            // same moment can work out the same number, and the index refuses the second. The
            // refusal is rolled back and the shop is asked to save again: the next number is then
            // free, and the count the counter wrote is still on their screen.
            await transaction.RollbackAsync(cancellationToken);

            return Result<int>.Failure(
                InventoryErrorCodes.NumberClash,
                "Another count was saved at the same moment and took this number. Save the count again and it will be given the next number.");
        }

        return Result<int>.Success(count.Id);
    }
}

/// <summary>
/// Submits a draft count for approval. The count keeps its book quantities until it is approved and
/// posted.
/// </summary>
public sealed class SubmitStockCountCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IUserSession session) : ICommandHandler<SubmitStockCountCommand, Result>
{
    /// <summary>The error code when the session does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.NotFound;

    /// <summary>The error code when the session is not a draft.</summary>
    public const string NotDraftCode = InventoryErrorCodes.NotDraft;

    /// <summary>The error code when the session carries no lines.</summary>
    public const string EmptyCountCode = InventoryErrorCodes.EmptyCount;

    /// <inheritdoc />
    public async Task<Result> HandleAsync(SubmitStockCountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.StockAdjust, cancellationToken);

        if (permitted.IsFailure)
        {
            return permitted;
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        StockCount? count = await context.StockCounts
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockCountId, cancellationToken);

        if (count is null)
        {
            return Result.Failure(NotFoundCode, "This stock count no longer exists.");
        }

        if (count.Status != StockCountStatus.Draft)
        {
            return Result.Failure(NotDraftCode, "This stock count has already been submitted.");
        }

        if (count.Lines.Count == 0)
        {
            return Result.Failure(EmptyCountCode, "Count at least one product before submitting.");
        }

        count.Status = StockCountStatus.AwaitingApproval;

        auditWriter.AddTo(
            context,
            new AuditEntry(
                InventoryAudit.CountSubmit,
                nameof(StockCount),
                count.Id.ToString(),
                null,
                $"{count.Number} submitted for approval",
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.SubmitStockCount, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Approves a submitted count. The approver must hold the stock-approval permission and must not be
/// the person who counted (PRD §5.9).
/// </summary>
public sealed class ApproveStockCountCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session) : ICommandHandler<ApproveStockCountCommand, Result>
{
    /// <summary>The error code when the session does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.NotFound;

    /// <summary>The error code when the session is not waiting for approval.</summary>
    public const string NotAwaitingApprovalCode = InventoryErrorCodes.NotAwaitingApproval;

    /// <summary>The error code when the approver is the person who counted.</summary>
    public const string SelfApprovalCode = InventoryErrorCodes.SelfApproval;

    /// <inheritdoc />
    public async Task<Result> HandleAsync(ApproveStockCountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.StockApprove, cancellationToken);

        if (permitted.IsFailure)
        {
            return permitted;
        }

        IReadOnlyList<InventoryValidationIssue> reasonIssues = InventoryValidation.ValidateReason(command.Reason);

        if (reasonIssues.Count > 0)
        {
            return Result.Failure(reasonIssues[0].Code, reasonIssues[0].Message);
        }

        int? actorId = session.Current?.UserId;

        if (actorId is null)
        {
            return Result.Failure(InventoryErrorCodes.NoActor, "Sign in to continue.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        StockCount? count = await context.StockCounts
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockCountId, cancellationToken);

        if (count is null)
        {
            return Result.Failure(NotFoundCode, "This stock count no longer exists.");
        }

        if (count.Status != StockCountStatus.AwaitingApproval)
        {
            return Result.Failure(
                NotAwaitingApprovalCode,
                "This stock count is not waiting for approval.");
        }

        if (count.UserId is int counterId && counterId == actorId.Value && !IsAdministrator())
        {
            return Result.Failure(
                SelfApprovalCode,
                "A stock count has to be approved by somebody other than the person who counted it.");
        }

        count.Status = StockCountStatus.Approved;
        count.ApprovedByUserId = actorId;
        count.ApprovedUtc = clock.UtcNow;
        count.Reason = command.Reason.Trim();

        auditWriter.AddTo(
            context,
            new AuditEntry(
                InventoryAudit.CountApprove,
                nameof(StockCount),
                count.Id.ToString(),
                null,
                $"{count.Number} approved: {count.Reason}",
                actorId));

        await context.BeforeWriteAsync(WritePoint.ApproveStockCount, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// True when the signed-in user is an administrator. An administrator may approve their own
    /// count: a shop with a single account would otherwise never be able to close a count.
    /// </summary>
    /// <returns>True when the session belongs to an administrator.</returns>
    private bool IsAdministrator() => session.Current?.IsAdministrator == true;
}

/// <summary>
/// Posts an approved count, writing one correction movement per line that differs from the book
/// quantity.
/// </summary>
public sealed class PostStockCountCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session) : ICommandHandler<PostStockCountCommand, Result<int>>
{
    /// <summary>The error code when the session does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.NotFound;

    /// <summary>The error code when the session is not approved.</summary>
    public const string NotApprovedCode = InventoryErrorCodes.NotApproved;

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(PostStockCountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.StockAdjust, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        StockCount? count = await context.StockCounts
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockCountId, cancellationToken);

        if (count is null)
        {
            return Result<int>.Failure(NotFoundCode, "This stock count no longer exists.");
        }

        if (count.Status != StockCountStatus.Approved)
        {
            return Result<int>.Failure(
                NotApprovedCode,
                "Only an approved stock count can be posted.");
        }

        List<Product> products = await context.Products
            .Include(product => product.Unit)
            .Where(product => count.Lines.Select(line => line.ProductId).Contains(product.Id))
            .ToListAsync(cancellationToken);

        DateTimeOffset now = clock.UtcNow;
        int written = 0;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        foreach (StockCountLine line in count.Lines.OrderBy(candidate => candidate.Id))
        {
            Product? product = products.FirstOrDefault(candidate => candidate.Id == line.ProductId);

            if (product is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Result<int>.Failure(
                    InventoryErrorCodes.InvalidReference,
                    "One of the counted products no longer exists.");
            }

            int scale = product.Unit?.Precision ?? 0;

            // The balance is read again here, inside the posting transaction, because the count is a
            // statement about the shelf as it stands — not about the shelf as it stood when the
            // counter wrote the number down. Between counting and posting the goods usually move,
            // and replaying the difference captured at save time would leave the books claiming a
            // quantity the counter never saw.
            StockBalance? balance = await context.StockBalances
                .FirstOrDefaultAsync(candidate => candidate.ProductId == line.ProductId, cancellationToken);

            // Both numbers are brought into the product's own scale before they are compared, so the
            // correction is a real difference and not an artefact of two different units.
            long counted = InventoryLedger.Rescale(line.CountedQuantityScaled, line.QuantityScale, scale);
            long onHand = InventoryLedger.Rescale(balance?.QuantityOnHandScaled ?? 0, balance?.QuantityScale ?? scale, scale);

            long correction = counted - onHand;

            // The line now carries what happened at posting, in the product's own scale, so the
            // count document and the ledger tell the same story.
            line.QuantityScale = scale;
            line.ExpectedQuantityScaled = onHand;
            line.CountedQuantityScaled = counted;
            line.DifferenceScaled = correction;
            line.DifferenceValue = InventoryLedger.ValueOf(Math.Abs(correction), scale, line.UnitCost);

            if (correction == 0)
            {
                continue;
            }

            // A balance left at another scale means the product's unit was changed before the shop's
            // rule about that was put in place. The count is the occasion to bring it back into the
            // product's own scale, so that the correction lands on the number the counter wrote.
            if (balance is not null && balance.QuantityScale != scale)
            {
                balance.QuantityOnHandScaled = onHand;
                balance.DamagedQuantityScaled = InventoryLedger.Rescale(balance.DamagedQuantityScaled, balance.QuantityScale, scale);
                balance.QuantityScale = scale;
            }

            (StockMovement? movement, string? errorCode, string? errorMessage) = await InventoryLedger.ApplyAsync(
                context,
                product,
                scale,
                StockMovementType.CountCorrection,
                StockDisposition.Sellable,
                correction,
                line.UnitCost,
                count.BusinessDate,
                count.Reason,
                session.Current?.UserId,
                count.Id,
                line.Id,
                now);

            if (movement is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Result<int>.Failure(errorCode!, errorMessage!);
            }

            written += 1;
        }

        count.Status = StockCountStatus.Posted;

        auditWriter.AddTo(
            context,
            new AuditEntry(
                InventoryAudit.CountPost,
                nameof(StockCount),
                count.Id.ToString(),
                null,
                $"{count.Number} posted with {written} corrections",
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.PostStockCountMovements, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.PostStockCountCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result<int>.Success(written);
    }
}

/// <summary>Cancels a count that has not been posted yet.</summary>
public sealed class CancelStockCountCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IUserSession session) : ICommandHandler<CancelStockCountCommand, Result>
{
    /// <summary>The error code when the session does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.NotFound;

    /// <summary>The error code when the session has already been posted.</summary>
    public const string AlreadyPostedCode = InventoryErrorCodes.AlreadyPosted;

    /// <inheritdoc />
    public async Task<Result> HandleAsync(CancelStockCountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.StockAdjust, cancellationToken);

        if (permitted.IsFailure)
        {
            return permitted;
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        StockCount? count = await context.StockCounts
            .FirstOrDefaultAsync(candidate => candidate.Id == command.StockCountId, cancellationToken);

        if (count is null)
        {
            return Result.Failure(NotFoundCode, "This stock count no longer exists.");
        }

        if (count.Status == StockCountStatus.Posted)
        {
            return Result.Failure(AlreadyPostedCode, "A posted stock count cannot be cancelled.");
        }

        count.Status = StockCountStatus.Cancelled;

        auditWriter.AddTo(
            context,
            new AuditEntry(
                InventoryAudit.CountCancel,
                nameof(StockCount),
                count.Id.ToString(),
                null,
                $"{count.Number} cancelled",
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.CancelStockCount, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Builds the next sequential stock count number for a year.
/// </summary>
internal static class StockCountNumber
{
    /// <summary>
    /// Produces the next free number, for example <c>CNT-2026-0003</c>.
    /// </summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="businessDate">The count date, which decides the year segment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The count number.</returns>
    public static async Task<string> NextAsync(
        StoreFlowDbContext context,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        string prefix = $"{InventoryRules.CountNumberPrefix}-{businessDate.Year:D4}-";

        List<string> existing = await context.StockCounts
            .Where(count => count.Number.StartsWith(prefix))
            .Select(count => count.Number)
            .ToListAsync(cancellationToken);

        int highest = 0;

        foreach (string number in existing)
        {
            if (int.TryParse(number.AsSpan(prefix.Length), out int sequence) && sequence > highest)
            {
                highest = sequence;
            }
        }

        return InventoryRules.BuildNumber(InventoryRules.CountNumberPrefix, businessDate, highest + 1);
    }
}
