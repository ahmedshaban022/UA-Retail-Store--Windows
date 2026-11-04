using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Pos;

/// <summary>
/// Suspends a cart, takes a suspended cart back onto the till, and throws a suspended cart away.
/// </summary>
/// <remarks>
/// A suspended cart reserves nothing and deducts nothing. The goods stay on the shelf and stay
/// sellable to the next customer, because a cart that was put aside is a note, not a movement
/// (PRD POS-09). Nothing here writes to the stock ledger.
/// </remarks>
public sealed class HoldCartCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session)
    : ICommandHandler<HoldCartCommand, Result<HeldCartSummary>>
{
    /// <inheritdoc />
    public async Task<Result<HeldCartSummary>> HandleAsync(HoldCartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.SalesPost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<HeldCartSummary>.Failure(permitted.Error);
        }

        string label = command.Label?.Trim() ?? string.Empty;

        if (label.Length == 0)
        {
            return Result<HeldCartSummary>.Failure(
                PosErrorCodes.HeldCartLabelRequired,
                "Give the cart a name before you put it aside.");
        }

        if (label.Length > PosRules.MaximumHeldCartLabelLength)
        {
            label = label[..PosRules.MaximumHeldCartLabelLength];
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        string prefix = $"{PosRules.HeldCartNumberPrefix}-{DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).Year:D4}-";

        List<string> existing = await context.HeldCarts
            .AsNoTracking()
            .Where(cart => cart.Number.StartsWith(prefix))
            .Select(cart => cart.Number)
            .ToListAsync(cancellationToken);

        int highest = 0;

        foreach (string number in existing)
        {
            if (int.TryParse(number.AsSpan(prefix.Length), out int sequence) && sequence > highest)
            {
                highest = sequence;
            }
        }

        var cart = new HeldCart
        {
            Number = PosRules.BuildNumber(PosRules.HeldCartNumberPrefix, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), highest + 1),
            Label = label,
            ContentJson = HeldCartStorageCodec.Encode(command.Lines, command.InvoiceDiscount),
            UserId = session.Current?.UserId,
            HeldUtc = clock.UtcNow,
            UpdatedUtc = clock.UtcNow,
        };

        context.HeldCarts.Add(cart);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.BeforeWriteAsync(WritePoint.HoldCart, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        HeldCartSummary summary = Summarise(cart, command.Lines);

        // A new cart has no id until its row has been saved, so the audit entry joins the same
        // transaction and is made permanent together with the cart it describes.
        auditWriter.AddTo(
            context,
            new AuditEntry(
                PosAudit.CartHold,
                nameof(HeldCart),
                cart.Id.ToString(CultureInfo.InvariantCulture),
                null,
                $"{cart.Number}: {summary.LineCount} lines",
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result<HeldCartSummary>.Success(summary);
    }

    /// <summary>Works out what a suspended cart is worth, from the lines it was suspended with.</summary>
    internal static HeldCartSummary Summarise(HeldCart cart, IReadOnlyList<SaleLineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(lines);

        var total = Money.Zero;
        decimal quantity = 0m;

        foreach (SaleLineInput line in lines)
        {
            total += new Money(line.UnitPrice.Amount * line.Quantity);
            quantity += line.Quantity;
        }

        return new HeldCartSummary(cart.Id, cart.Label, lines.Count, quantity, total, cart.HeldUtc);
    }
}

/// <summary>Takes a suspended cart back onto the till.</summary>
public sealed class ResumeHeldCartCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IUserSession session)
    : ICommandHandler<ResumeHeldCartCommand, Result<HeldCartDetails>>
{
    /// <inheritdoc />
    public async Task<Result<HeldCartDetails>> HandleAsync(
        ResumeHeldCartCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.SalesPost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<HeldCartDetails>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        HeldCart? cart = await context.HeldCarts
            .FirstOrDefaultAsync(candidate => candidate.Id == command.HeldCartId, cancellationToken);

        if (cart is null)
        {
            return Result<HeldCartDetails>.Failure(
                PosErrorCodes.HeldCartNotFound,
                "That cart is not waiting any more.");
        }

        HeldCartStorage storage = HeldCartStorageCodec.Decode(cart.ContentJson);
        (IReadOnlyList<SaleLineInput> lines, DiscountSpec invoiceDiscount) =
            HeldCartStorageCodec.ToTill(storage);

        HeldCartSummary summary = HoldCartCommandHandler.Summarise(cart, lines);

        // The cart is on the till now, so it is no longer waiting.
        context.HeldCarts.Remove(cart);

        auditWriter.AddTo(
            context,
            new AuditEntry(
                PosAudit.CartResume,
                nameof(HeldCart),
                cart.Id.ToString(CultureInfo.InvariantCulture),
                null,
                cart.Number,
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.ResumeHeldCart, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Result<HeldCartDetails>.Success(new HeldCartDetails(summary, lines, invoiceDiscount));
    }
}

/// <summary>Throws a suspended cart away without selling it.</summary>
public sealed class ClearHeldCartCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IUserSession session)
    : ICommandHandler<ClearHeldCartCommand, Result>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(ClearHeldCartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.SalesPost, cancellationToken);

        if (permitted.IsFailure)
        {
            return permitted;
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        HeldCart? cart = await context.HeldCarts
            .FirstOrDefaultAsync(candidate => candidate.Id == command.HeldCartId, cancellationToken);

        if (cart is null)
        {
            return Result.Failure(
                PosErrorCodes.HeldCartNotFound,
                "That cart is not waiting any more.");
        }

        context.HeldCarts.Remove(cart);

        auditWriter.AddTo(
            context,
            new AuditEntry(
                PosAudit.CartClear,
                nameof(HeldCart),
                cart.Id.ToString(CultureInfo.InvariantCulture),
                null,
                cart.Number,
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.ClearHeldCart, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
