using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Sales;

/// <summary>
/// Posts a return: the document, the stock that comes back, the money that goes back, and the
/// counter on the invoice line that keeps the two in step. All of it commits together, or none of
/// it does.
/// </summary>
/// <remarks>
/// <para>A return never edits the sale it undoes. It is a linked document, and the sale keeps every
/// figure it was written down with. The only thing that moves on the original invoice line is the
/// counter of what has come back, and the database refuses every other change to a posted line
/// (PRD §5.7, SAL-06).</para>
/// <para>The stock comes back through the same ledger that took it off the shelf, at the cost that
/// was snapshotted on the invoice line, so a resellable return restores the moving average exactly
/// the way a receipt does, and a damaged one lands in the damaged bucket and never in the quantity
/// available to sell (ADR-0003).</para>
/// </remarks>
public sealed class PostReturnCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session)
    : ICommandHandler<PostReturnCommand, Result<RefundReceipt>>
{
    /// <inheritdoc />
    public async Task<Result<RefundReceipt>> HandleAsync(PostReturnCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.SalesReturn, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<RefundReceipt>.Failure(permitted.Error);
        }

        if (command.Lines.Count == 0)
        {
            return Failure(SalesErrorCodes.NoLines, "A return must have at least one line.");
        }

        string reason = (command.Reason ?? string.Empty).Trim();

        if (reason.Length == 0)
        {
            return Failure(SalesErrorCodes.ReasonRequired, "A return must say why the goods came back.");
        }

        if (reason.Length > SalesRules.MaximumReasonLength)
        {
            return Failure(
                SalesErrorCodes.ReasonRequired,
                $"The reason is longer than {SalesRules.MaximumReasonLength} characters.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        SalesInvoice? invoice = await context.SalesInvoices
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.OriginalInvoiceId, cancellationToken);

        if (invoice is null)
        {
            return Failure(SalesErrorCodes.InvoiceNotFound, "This invoice no longer exists.");
        }

        if (invoice.Status == DocumentStatus.Reversed)
        {
            return Failure(
                SalesErrorCodes.InvoiceAlreadyReversed,
                "This invoice has been reversed, so there is nothing left to return.");
        }

        if (invoice.Status != DocumentStatus.Posted)
        {
            return Failure(SalesErrorCodes.InvoiceNotPosted, "Only a posted invoice can be returned against.");
        }

        if (invoice.ReversesDocumentId.HasValue)
        {
            return Failure(
                SalesErrorCodes.InvoiceIsReversal,
                "A reversal is the end of the line for the invoice it undoes.");
        }

        Dictionary<int, long> alreadyReturned = await ReadReturnedQuantitiesAsync(context, invoice, cancellationToken);

        List<PlannedReturnLine> planned = [];

        foreach (ReturnLineInput input in command.Lines)
        {
            SalesInvoiceLine? line = invoice.Lines
                .FirstOrDefault(candidate => candidate.Id == input.SalesInvoiceLineId);

            if (line is null)
            {
                return Failure(
                    SalesErrorCodes.LineNotOnInvoice,
                    "One of the lines being returned is not on this invoice.");
            }

            if (!SalesRules.FitsScale(input.Quantity, line.QuantityScale))
            {
                return Failure(
                    SalesErrorCodes.InvalidQuantity,
                    "A quantity that the selling unit cannot express cannot be returned.");
            }

            long quantityScaled = SalesRules.Scale(input.Quantity, line.QuantityScale);

            if (quantityScaled <= 0)
            {
                return Failure(SalesErrorCodes.InvalidQuantity, "A return line must carry a quantity.");
            }

            long returned = alreadyReturned.GetValueOrDefault(line.Id);

            if (returned + quantityScaled > line.QuantityScaled)
            {
                return Failure(
                    returned == 0 ? SalesErrorCodes.QuantityExceedsSold : SalesErrorCodes.QuantityExceedsRemaining,
                    returned == 0
                        ? "More is being returned than was sold on that line."
                        : "More is being returned than is left to return on that line.");
            }

            // Two lines of the same command may name the same line of the invoice. What the first
            // one takes back counts towards the second one, or the shop could take back more than
            // it sold.
            alreadyReturned[line.Id] = returned + quantityScaled;

            planned.Add(new PlannedReturnLine(
                line,
                quantityScaled,
                input.IsResellable,
                input.Reason,
                SalesRules.RefundForQuantity(line.Total, quantityScaled, line.QuantityScaled),
                SalesRules.RefundForQuantity(line.Tax, quantityScaled, line.QuantityScaled)));
        }

        Money refundTotal = new(planned.Sum(plan => plan.Refund.Amount));
        Money refundTax = new(planned.Sum(plan => plan.Tax.Amount));

        string number = await SalesNumbers.NextReturnNumberAsync(context, command.BusinessDate, cancellationToken);

        var salesReturn = new SalesReturn
        {
            Number = number,
            OriginalInvoiceId = invoice.Id,
            Reason = reason,
            RefundTotal = refundTotal,
            RefundTax = refundTax,
            RefundMethod = command.RefundMethod,
            UserId = session.Current?.UserId ?? 0,
            BusinessDate = command.BusinessDate,
            PostedUtc = clock.UtcNow,
            Notes = command.Notes,
        };

        context.SalesReturns.Add(salesReturn);

        foreach (PlannedReturnLine plan in planned)
        {
            var returnLine = new SalesReturnLine
            {
                SalesReturn = salesReturn,
                SalesInvoiceLine = plan.Line,
                ProductId = plan.Line.ProductId,
                QuantityScaled = plan.QuantityScaled,
                QuantityScale = plan.Line.QuantityScale,
                IsResellable = plan.IsResellable,
                RefundAmount = plan.Refund,
                OriginalUnitCost = plan.Line.UnitCostSnapshot,
                Reason = plan.Reason,
            };

            salesReturn.Lines.Add(returnLine);

            plan.ReturnLine = returnLine;

            // The counter is the one thing a posted invoice line is allowed to change, and the
            // database refuses every other change to it.
            plan.Line.ReturnedQuantityScaled += plan.QuantityScaled;
        }

        // The refund is recorded as a payment row of its own, so every movement of money in the shop
        // is in one place and can be read back.
        salesReturn.Payments.Add(new Payment
        {
            SalesReturn = salesReturn,
            Method = command.RefundMethod,
            Amount = refundTotal,
            IsRefund = true,
        });

        await context.BeforeWriteAsync(WritePoint.PostReturnDocuments, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        foreach (PlannedReturnLine plan in planned)
        {
            Product product = await context.Products
                .Include(candidate => candidate.Unit)
                .FirstAsync(candidate => candidate.Id == plan.Line.ProductId, cancellationToken);

            (StockMovement? movement, string? errorCode, string? errorMessage) =
                await InventoryLedger.ApplyAsync(
                    context,
                    product,
                    plan.Line.QuantityScale,
                    plan.IsResellable ? StockMovementType.ReturnRestock : StockMovementType.ReturnDamaged,
                    plan.IsResellable ? StockDisposition.Sellable : StockDisposition.Damaged,
                    plan.QuantityScaled,
                    plan.Line.UnitCostSnapshot,
                    command.BusinessDate,
                    reason,
                    session.Current?.UserId,
                    salesReturn.Id,
                    plan.ReturnLine.Id,
                    clock.UtcNow);

            if (movement is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Failure(
                    SalesErrorCodes.StockNotRestored,
                    errorMessage ?? "The stock could not be put back on the shelf.");
            }
        }

        await context.BeforeWriteAsync(WritePoint.PostReturnRestock, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // The return only has an id once it has been saved, so the audit entry joins the same
        // transaction and is made permanent together with the return it describes.
        auditWriter.AddTo(
            context,
            new AuditEntry(
                SalesAudit.ReturnPost,
                nameof(SalesReturn),
                salesReturn.Id.ToString(CultureInfo.InvariantCulture),
                null,
                $"{salesReturn.Number} against {invoice.Number} for {refundTotal.Amount.ToString("0.00", CultureInfo.InvariantCulture)}",
                session.Current?.UserId));

        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.PostReturnCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var receipt = new RefundReceipt(
            SalesDocumentKind.Return,
            salesReturn.Id,
            salesReturn.Number,
            invoice.Id,
            invoice.Number,
            salesReturn.BusinessDate,
            salesReturn.PostedUtc,
            refundTotal,
            command.RefundMethod,
            reason,
            planned
                .Select(plan => new ReturnLineSummary(
                    plan.Line.Id,
                    plan.Line.NameSnapshot,
                    SalesRules.Unscale(plan.QuantityScaled, plan.Line.QuantityScale),
                    plan.IsResellable,
                    plan.Refund))
                .ToList());

        return Result<RefundReceipt>.Success(receipt);
    }

    /// <summary>
    /// Reads how much of every line of the invoice has already come back.
    /// </summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="invoice">The invoice being returned against.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The returned quantity of each line, keyed by line id.</returns>
    private static async Task<Dictionary<int, long>> ReadReturnedQuantitiesAsync(
        StoreFlowDbContext context,
        SalesInvoice invoice,
        CancellationToken cancellationToken)
    {
        int[] lineIds = invoice.Lines.Select(line => line.Id).ToArray();

        if (lineIds.Length == 0)
        {
            return [];
        }

        List<ReturnedQuantity> returned = await context.SalesReturnLines
            .AsNoTracking()
            .Where(line => lineIds.Contains(line.SalesInvoiceLineId))
            .GroupBy(line => line.SalesInvoiceLineId)
            .Select(group => new ReturnedQuantity
            {
                LineId = group.Key,
                Quantity = group.Sum(line => line.QuantityScaled),
            })
            .ToListAsync(cancellationToken);

        return returned.ToDictionary(item => item.LineId, item => item.Quantity);
    }

    private static Result<RefundReceipt> Failure(string code, string message) =>
        Result<RefundReceipt>.Failure(code, message);

    /// <summary>One line of the return, once it has been checked against the invoice.</summary>
    /// <param name="Line">The line of the original invoice.</param>
    /// <param name="QuantityScaled">The quantity coming back.</param>
    /// <param name="IsResellable">True when the goods go back on the shelf.</param>
    /// <param name="Reason">The optional line-level reason.</param>
    /// <param name="Refund">The money that goes back for this line.</param>
    /// <param name="Tax">The tax that comes back with it.</param>
    private sealed record PlannedReturnLine(
        SalesInvoiceLine Line,
        long QuantityScaled,
        bool IsResellable,
        string? Reason,
        Money Refund,
        Money Tax)
    {
        /// <summary>The return line that was written for this plan.</summary>
        public SalesReturnLine ReturnLine { get; set; } = null!;
    }

    /// <summary>The projection the returned quantities are read with.</summary>
    private sealed class ReturnedQuantity
    {
        public int LineId { get; set; }

        public long Quantity { get; set; }
    }
}
