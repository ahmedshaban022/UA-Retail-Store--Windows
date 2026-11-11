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
/// Creates or updates a draft purchase document. Saving a draft changes nothing in stock; only
/// posting it does (PRD PUR-02).
/// </summary>
public sealed class SavePurchaseCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session) : ICommandHandler<SavePurchaseCommand, Result<int>>
{
    /// <summary>The error code when the document does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.NotFound;

    /// <summary>The error code when the document is no longer a draft.</summary>
    public const string NotDraftCode = InventoryErrorCodes.NotDraft;

    /// <summary>The error code when a referenced record does not exist.</summary>
    public const string InvalidReferenceCode = InventoryErrorCodes.InvalidReference;

    /// <summary>The error code when the discount is negative or larger than the document.</summary>
    public const string InvalidDiscountCode = InventoryErrorCodes.InvalidDiscount;

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(SavePurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.PurchasePost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        PurchaseDocument? document = null;

        if (command.PurchaseId is int purchaseId)
        {
            document = await context.PurchaseDocuments
                .Include(existing => existing.Lines)
                .FirstOrDefaultAsync(existing => existing.Id == purchaseId, cancellationToken);

            if (document is null)
            {
                return Result<int>.Failure(NotFoundCode, "This purchase document no longer exists.");
            }

            if (document.Status != DocumentStatus.Draft)
            {
                return Result<int>.Failure(
                    NotDraftCode,
                    "A posted purchase cannot be edited. Reverse it and post a new one instead.");
            }
        }

        if (command.SupplierId is int supplierId
            && !await context.Suppliers.AnyAsync(supplier => supplier.Id == supplierId, cancellationToken))
        {
            return Result<int>.Failure(InvalidReferenceCode, "The chosen supplier no longer exists.");
        }

        List<int> productIds = command.Lines.Select(line => line.ProductId).Distinct().ToList();

        List<Product> products = await context.Products
            .Include(product => product.Unit)
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        var issues = new List<InventoryValidationIssue>();

        issues.AddRange(InventoryValidation.ValidatePurchase(command.Lines));

        foreach (PurchaseLineInput line in command.Lines)
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

            issues.AddRange(InventoryValidation.ValidatePurchaseLine(
                line.Quantity,
                line.UnitCost,
                line.Tax,
                product.Unit?.Precision ?? 0));
        }

        if (command.Discount.IsNegative)
        {
            issues.Add(new InventoryValidationIssue(
                "discount",
                InvalidDiscountCode,
                "The discount cannot be negative."));
        }

        if (issues.Count > 0)
        {
            return Result<int>.Failure(issues[0].Code, issues[0].Message);
        }

        DateTimeOffset now = clock.UtcNow;
        bool isNew = document is null;

        document ??= new PurchaseDocument
        {
            Number = await PurchaseNumber.NextAsync(context, command.BusinessDate, cancellationToken),
            Status = DocumentStatus.Draft,
        };

        document.SupplierId = command.SupplierId;
        document.BusinessDate = command.BusinessDate;
        document.Discount = command.Discount;
        document.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        document.UserId = session.Current?.UserId;

        // The lines are replaced wholesale: the owner edited the document, so the document holds
        // exactly the lines they can see.
        context.PurchaseLines.RemoveRange(document.Lines);
        document.Lines.Clear();

        Money subtotal = Money.Zero;
        Money tax = Money.Zero;

        foreach (PurchaseLineInput input in command.Lines)
        {
            Product product = products.First(candidate => candidate.Id == input.ProductId);
            int scale = product.Unit?.Precision ?? 0;

            var line = new PurchaseLine
            {
                PurchaseDocument = document,
                ProductId = input.ProductId,
                QuantityScaled = new UnitQuantity(input.Quantity, scale).ToScaledLong(),
                QuantityScale = scale,
                UnitCost = input.UnitCost,
                Tax = input.Tax,
                Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            };

            line.Total = new Money((input.Quantity * input.UnitCost.Amount) + input.Tax.Amount);

            subtotal += new Money(input.Quantity * input.UnitCost.Amount);
            tax += input.Tax;

            document.Lines.Add(line);
        }

        if (command.Discount.Amount > subtotal.Amount)
        {
            return Result<int>.Failure(
                InvalidDiscountCode,
                "The discount cannot be larger than the value of the document.");
        }

        document.Subtotal = subtotal;
        document.Tax = tax;
        document.Total = subtotal - command.Discount + tax;

        if (isNew)
        {
            context.PurchaseDocuments.Add(document);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await context.BeforeWriteAsync(WritePoint.SavePurchase, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // A new document has no id until its row has been saved, so the audit entry joins the same
            // transaction and is made permanent together with the document it describes.
            auditWriter.AddTo(
                context,
                new AuditEntry(
                    InventoryAudit.PurchaseSave,
                    nameof(PurchaseDocument),
                    document.Id.ToString(),
                    null,
                    $"{document.Number} with {document.Lines.Count} lines, total {document.Total}",
                    session.Current?.UserId));

            await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (UniqueConstraintClash.IsClash(exception))
        {
            // The number is worked out before the transaction opens, so two clerks saving at the
            // same moment can work out the same number, and the index refuses the second. The
            // refusal is rolled back and the shop is asked to save again: the next number is then
            // free, and the document the clerk wrote is still on their screen.
            await transaction.RollbackAsync(cancellationToken);

            return Result<int>.Failure(
                InventoryErrorCodes.NumberClash,
                "Another purchase was saved at the same moment and took this number. Save the document again and it will be given the next number.");
        }

        return Result<int>.Success(document.Id);
    }
}

/// <summary>
/// Posts a draft purchase document: stock increases exactly once and every movement references the
/// document (PRD PUR-03, PUR-04).
/// </summary>
public sealed class PostPurchaseCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session) : ICommandHandler<PostPurchaseCommand, Result<int>>
{
    /// <summary>The error code when the document does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.NotFound;

    /// <summary>The error code when the document is not a draft.</summary>
    public const string NotDraftCode = InventoryErrorCodes.NotDraft;

    /// <summary>The error code when the document has no lines.</summary>
    public const string EmptyDocumentCode = InventoryErrorCodes.EmptyDocument;

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(PostPurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.PurchasePost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        PurchaseDocument? document = await context.PurchaseDocuments
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PurchaseId, cancellationToken);

        if (document is null)
        {
            return Result<int>.Failure(NotFoundCode, "This purchase document no longer exists.");
        }

        if (document.Status != DocumentStatus.Draft)
        {
            return Result<int>.Failure(
                NotDraftCode,
                "This purchase document has already been posted.");
        }

        if (document.Lines.Count == 0)
        {
            return Result<int>.Failure(
                EmptyDocumentCode,
                "Add at least one line before posting this purchase.");
        }

        List<Product> products = await context.Products
            .Include(product => product.Unit)
            .Where(product => document.Lines.Select(line => line.ProductId).Contains(product.Id))
            .ToListAsync(cancellationToken);

        DateTimeOffset now = clock.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        foreach (PurchaseLine line in document.Lines.OrderBy(candidate => candidate.Id))
        {
            Product? product = products.FirstOrDefault(candidate => candidate.Id == line.ProductId);

            if (product is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Result<int>.Failure(
                    InventoryErrorCodes.InvalidReference,
                    "One of the products on this purchase no longer exists.");
            }

            (StockMovement? movement, string? errorCode, string? errorMessage) = await InventoryLedger.ApplyAsync(
                context,
                product,
                line.QuantityScale,
                StockMovementType.PurchaseReceipt,
                StockDisposition.Sellable,
                line.QuantityScaled,
                line.UnitCost,
                document.BusinessDate,
                null,
                session.Current?.UserId,
                document.Id,
                line.Id,
                now);

            if (movement is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Result<int>.Failure(errorCode!, errorMessage!);
            }
        }

        document.Status = DocumentStatus.Posted;
        document.PostedUtc = now;
        document.UserId = session.Current?.UserId;

        auditWriter.AddTo(
            context,
            new AuditEntry(
                InventoryAudit.PurchasePost,
                nameof(PurchaseDocument),
                document.Id.ToString(),
                null,
                $"{document.Number} received, total {document.Total}",
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.PostPurchaseStock, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.PostPurchaseCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result<int>.Success(document.Id);
    }
}

/// <summary>
/// Reverses a posted purchase document. The correction is a linked reversing document, never a
/// deletion of history (PRD PUR-06).
/// </summary>
public sealed class ReversePurchaseCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session) : ICommandHandler<ReversePurchaseCommand, Result<int>>
{
    /// <summary>The error code when the document does not exist.</summary>
    public const string NotFoundCode = InventoryErrorCodes.NotFound;

    /// <summary>The error code when the document is not posted.</summary>
    public const string NotPostedCode = InventoryErrorCodes.NotPosted;

    /// <summary>The error code when the document has already been reversed.</summary>
    public const string AlreadyReversedCode = InventoryErrorCodes.AlreadyReversed;

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(ReversePurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.PurchasePost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        IReadOnlyList<InventoryValidationIssue> reasonIssues = InventoryValidation.ValidateReason(command.Reason);

        if (reasonIssues.Count > 0)
        {
            return Result<int>.Failure(reasonIssues[0].Code, reasonIssues[0].Message);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        PurchaseDocument? document = await context.PurchaseDocuments
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PurchaseId, cancellationToken);

        if (document is null)
        {
            return Result<int>.Failure(NotFoundCode, "This purchase document no longer exists.");
        }

        if (document.Status == DocumentStatus.Reversed)
        {
            return Result<int>.Failure(AlreadyReversedCode, "This purchase has already been reversed.");
        }

        if (document.Status != DocumentStatus.Posted)
        {
            return Result<int>.Failure(
                NotPostedCode,
                "Only a posted purchase can be reversed.");
        }

        List<Product> products = await context.Products
            .Include(product => product.Unit)
            .Where(product => document.Lines.Select(line => line.ProductId).Contains(product.Id))
            .ToListAsync(cancellationToken);

        DateTimeOffset now = clock.UtcNow;
        string reason = command.Reason.Trim();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var reversal = new PurchaseDocument
        {
            Number = await PurchaseNumber.NextAsync(context, document.BusinessDate, cancellationToken),
            SupplierId = document.SupplierId,
            Status = DocumentStatus.Posted,
            Subtotal = document.Subtotal,
            Discount = document.Discount,
            Tax = document.Tax,
            Total = document.Total,
            BusinessDate = document.BusinessDate,
            PostedUtc = now,
            UserId = session.Current?.UserId,
            ReversesDocumentId = document.Id,
            Notes = reason,
        };

        context.PurchaseDocuments.Add(reversal);

        var reversalLines = new List<PurchaseLine>();

        foreach (PurchaseLine line in document.Lines.OrderBy(candidate => candidate.Id))
        {
            var reversalLine = new PurchaseLine
            {
                PurchaseDocument = reversal,
                ProductId = line.ProductId,
                QuantityScaled = line.QuantityScaled,
                QuantityScale = line.QuantityScale,
                UnitCost = line.UnitCost,
                Tax = line.Tax,
                Total = line.Total,
                Notes = reason,
            };

            reversal.Lines.Add(reversalLine);
            reversalLines.Add(reversalLine);
        }

        // The reversing document and its lines are written first, so every movement can point at the
        // line that caused it.
        await context.BeforeWriteAsync(WritePoint.ReversePurchaseDocument, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        foreach (PurchaseLine line in document.Lines.OrderBy(candidate => candidate.Id))
        {
            PurchaseLine reversalLine = reversalLines.First(candidate => candidate.ProductId == line.ProductId);

            Product? product = products.FirstOrDefault(candidate => candidate.Id == line.ProductId);

            if (product is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Result<int>.Failure(
                    InventoryErrorCodes.InvalidReference,
                    "One of the products on this purchase no longer exists.");
            }

            (StockMovement? movement, string? errorCode, string? errorMessage) = await InventoryLedger.ApplyAsync(
                context,
                product,
                line.QuantityScale,
                StockMovementType.Reversal,
                StockDisposition.Sellable,
                -line.QuantityScaled,
                line.UnitCost,
                document.BusinessDate,
                reason,
                session.Current?.UserId,
                reversal.Id,
                reversalLine.Id,
                now);

            if (movement is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Result<int>.Failure(errorCode!, errorMessage!);
            }
        }

        document.Status = DocumentStatus.Reversed;

        auditWriter.AddTo(
            context,
            new AuditEntry(
                InventoryAudit.PurchaseReverse,
                nameof(PurchaseDocument),
                document.Id.ToString(),
                null,
                $"{document.Number} reversed by {reversal.Number}: {reason}",
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.ReversePurchaseStock, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.ReversePurchaseCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result<int>.Success(reversal.Id);
    }
}

/// <summary>
/// Builds the next sequential purchase document number for a year.
/// </summary>
internal static class PurchaseNumber
{
    /// <summary>
    /// Produces the next free number, for example <c>PUR-2026-0007</c>. The number is unique inside
    /// the database by a unique index, so a clash is refused rather than silently reused.
    /// </summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="businessDate">The document date, which decides the year segment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document number.</returns>
    public static async Task<string> NextAsync(
        StoreFlowDbContext context,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        string prefix = $"{InventoryRules.PurchaseNumberPrefix}-{businessDate.Year:D4}-";

        List<string> existing = await context.PurchaseDocuments
            .Where(document => document.Number.StartsWith(prefix))
            .Select(document => document.Number)
            .ToListAsync(cancellationToken);

        int highest = 0;

        foreach (string number in existing)
        {
            if (int.TryParse(number.AsSpan(prefix.Length), out int sequence) && sequence > highest)
            {
                highest = sequence;
            }
        }

        return InventoryRules.BuildNumber(InventoryRules.PurchaseNumberPrefix, businessDate, highest + 1);
    }
}
