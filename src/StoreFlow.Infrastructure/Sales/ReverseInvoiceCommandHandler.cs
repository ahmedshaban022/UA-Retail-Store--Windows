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
/// Reverses a posted invoice: a linked document that puts the money and the goods back and closes
/// the original off.
/// </summary>
/// <remarks>
/// <para>The original invoice is not edited and not deleted. It is marked as reversed, and a second
/// document carries the undo: the money that goes back, the stock that comes back at the cost it
/// left at, and the reason (PRD §5.7, SAL-06).</para>
/// <para>A posted invoice may be reversed only once. The reversal is a document of its own with its
/// own number, so the history of the till stays readable: a sale, and the document that undid
/// it.</para>
/// <para>When part of the invoice has already come back as a return, the reversal takes back only
/// what is left. The goods the return already put on the shelf are not put back a second time, and
/// the money the return already refunded is not refunded a second time, so the two corrections
/// together undo the sale exactly once.</para>
/// </remarks>
public sealed class ReverseInvoiceCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session)
    : ICommandHandler<ReverseInvoiceCommand, Result<RefundReceipt>>
{
    /// <inheritdoc />
    public async Task<Result<RefundReceipt>> HandleAsync(ReverseInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.SalesReturn, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<RefundReceipt>.Failure(permitted.Error);
        }

        string reason = (command.Reason ?? string.Empty).Trim();

        if (reason.Length == 0)
        {
            return Failure(SalesErrorCodes.ReasonRequired, "A reversal must say why the invoice is being reversed.");
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
            .FirstOrDefaultAsync(candidate => candidate.Id == command.InvoiceId, cancellationToken);

        if (invoice is null)
        {
            return Failure(SalesErrorCodes.InvoiceNotFound, "This invoice no longer exists.");
        }

        if (invoice.Status == DocumentStatus.Reversed)
        {
            return Failure(SalesErrorCodes.InvoiceAlreadyReversed, "This invoice has already been reversed.");
        }

        if (invoice.Status != DocumentStatus.Posted)
        {
            return Failure(SalesErrorCodes.InvoiceNotPosted, "Only a posted invoice can be reversed.");
        }

        if (invoice.ReversesDocumentId.HasValue)
        {
            return Failure(SalesErrorCodes.InvoiceIsReversal, "A reversal cannot be reversed again.");
        }

        if (invoice.Lines.Count == 0)
        {
            return Failure(SalesErrorCodes.NoLines, "This invoice has no lines, so there is nothing to reverse.");
        }

        Dictionary<int, long> alreadyReturned = await ReadReturnedQuantitiesAsync(context, invoice, cancellationToken);

        // Only what has not already come back is taken back. The lines that a return has already
        // settled are left out of the reversal altogether.
        List<PlannedReversalLine> planned = [];

        foreach (SalesInvoiceLine line in invoice.Lines)
        {
            long returned = alreadyReturned.GetValueOrDefault(line.Id);
            long remaining = SalesRules.RemainingQuantity(line.QuantityScaled, returned);

            if (remaining <= 0)
            {
                continue;
            }

            Money total = SalesRules.RefundForQuantity(line.Total, remaining, line.QuantityScaled);
            Money tax = SalesRules.RefundForQuantity(line.Tax, remaining, line.QuantityScaled);
            Money lineDiscount = SalesRules.RefundForQuantity(line.Discount, remaining, line.QuantityScaled);
            Money invoiceShare = SalesRules.RefundForQuantity(line.InvoiceDiscountShare, remaining, line.QuantityScaled);

            // The gross is taken from the other figures rather than pro-rated on its own, so the
            // document adds up exactly: the gross less the discount plus the tax is the total.
            Money subtotal = total + lineDiscount + invoiceShare - tax;

            planned.Add(new PlannedReversalLine(
                line,
                remaining,
                subtotal,
                lineDiscount,
                invoiceShare,
                tax,
                total));
        }

        if (planned.Count == 0)
        {
            return Failure(
                SalesErrorCodes.NothingToReverse,
                "Everything on this invoice has already been returned, so there is nothing left to reverse.");
        }

        Money reversalSubtotal = new(planned.Sum(plan => plan.Subtotal.Amount));
        Money reversalDiscount = new(planned.Sum(plan => plan.LineDiscount.Amount + plan.InvoiceShare.Amount));
        Money reversalTax = new(planned.Sum(plan => plan.Tax.Amount));
        Money reversalTotal = new(planned.Sum(plan => plan.Total.Amount));

        string number = await SalesNumbers.NextReversalNumberAsync(context, command.BusinessDate, cancellationToken);

        var reversal = new SalesInvoice
        {
            Number = number,
            Status = DocumentStatus.Posted,
            ReversesDocumentId = invoice.Id,
            Subtotal = reversalSubtotal,
            Discount = reversalDiscount,
            Tax = reversalTax,
            Total = reversalTotal,
            Paid = reversalTotal,
            Change = Money.Zero,
            PaymentMethod = command.RefundMethod,
            UserId = session.Current?.UserId ?? 0,
            BusinessDate = command.BusinessDate,
            PostedUtc = clock.UtcNow,
            ClientOperationId = Guid.NewGuid(),
            Notes = command.Notes,
        };

        context.SalesInvoices.Add(reversal);

        foreach (PlannedReversalLine plan in planned)
        {
            SalesInvoiceLine line = plan.Line;

            reversal.Lines.Add(new SalesInvoiceLine
            {
                SalesInvoice = reversal,
                ProductId = line.ProductId,
                NameSnapshot = line.NameSnapshot,
                SkuSnapshot = line.SkuSnapshot,
                BarcodeSnapshot = line.BarcodeSnapshot,
                UnitSnapshot = line.UnitSnapshot,
                QuantityScaled = plan.QuantityScaled,
                QuantityScale = line.QuantityScale,
                UnitPrice = line.UnitPrice,
                Discount = plan.LineDiscount,
                InvoiceDiscountShare = plan.InvoiceShare,
                DiscountPercentScaled = line.DiscountPercentScaled,
                Tax = plan.Tax,
                Total = plan.Total,
                UnitCostSnapshot = line.UnitCostSnapshot,
                TaxRateSnapshotBasisPoints = line.TaxRateSnapshotBasisPoints,
                TaxPriceIncludedSnapshot = line.TaxPriceIncludedSnapshot,
            });
        }

        // The refund is recorded as a payment row of its own, so every movement of money in the shop
        // is in one place and can be read back.
        reversal.Payments.Add(new Payment
        {
            SalesInvoice = reversal,
            Method = command.RefundMethod,
            Amount = reversalTotal,
            IsRefund = true,
        });

        await context.BeforeWriteAsync(WritePoint.ReverseInvoiceDocuments, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        foreach (PlannedReversalLine plan in planned)
        {
            Product product = await context.Products
                .Include(candidate => candidate.Unit)
                .FirstAsync(candidate => candidate.Id == plan.Line.ProductId, cancellationToken);

            (StockMovement? movement, string? errorCode, string? errorMessage) =
                await InventoryLedger.ApplyAsync(
                    context,
                    product,
                    plan.Line.QuantityScale,
                    StockMovementType.Reversal,
                    command.IsResellable ? StockDisposition.Sellable : StockDisposition.Damaged,
                    plan.QuantityScaled,
                    plan.Line.UnitCostSnapshot,
                    command.BusinessDate,
                    reason,
                    session.Current?.UserId,
                    reversal.Id,
                    plan.Line.Id,
                    clock.UtcNow);

            if (movement is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Failure(
                    SalesErrorCodes.StockNotRestored,
                    errorMessage ?? "The stock could not be put back on the shelf.");
            }
        }

        invoice.Status = DocumentStatus.Reversed;

        await context.BeforeWriteAsync(WritePoint.ReverseInvoiceStockMovement, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await context.BeforeCommitAsync(WritePoint.ReverseInvoiceCommit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var receipt = new RefundReceipt(
            SalesDocumentKind.Reversal,
            reversal.Id,
            reversal.Number,
            invoice.Id,
            invoice.Number,
            reversal.BusinessDate,
            reversal.PostedUtc,
            reversalTotal,
            command.RefundMethod,
            reason,
            planned
                .Select(plan => new ReturnLineSummary(
                    plan.Line.Id,
                    plan.Line.NameSnapshot,
                    SalesRules.Unscale(plan.QuantityScaled, plan.Line.QuantityScale),
                    command.IsResellable,
                    plan.Total))
                .ToList());

        await auditWriter.WriteAsync(
            new AuditEntry(
                SalesAudit.InvoiceReversal,
                nameof(SalesInvoice),
                reversal.Id.ToString(CultureInfo.InvariantCulture),
                null,
                $"{reversal.Number} reverses {invoice.Number} for {reversalTotal.Amount.ToString("0.00", CultureInfo.InvariantCulture)}",
                session.Current?.UserId),
            cancellationToken);

        return Result<RefundReceipt>.Success(receipt);
    }

    /// <summary>
    /// Reads how much of every line of the invoice has already come back.
    /// </summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="invoice">The invoice being reversed.</param>
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

    /// <summary>One line of the reversal, once it has been measured against what came back already.</summary>
    /// <param name="Line">The line of the original invoice.</param>
    /// <param name="QuantityScaled">The quantity still to take back.</param>
    /// <param name="Subtotal">The gross of what is taken back.</param>
    /// <param name="LineDiscount">The part of the discount that belonged to the line itself.</param>
    /// <param name="InvoiceShare">The part of the whole-sale discount the line carried.</param>
    /// <param name="Tax">The tax that comes back with it.</param>
    /// <param name="Total">The money that goes back.</param>
    private sealed record PlannedReversalLine(
        SalesInvoiceLine Line,
        long QuantityScaled,
        Money Subtotal,
        Money LineDiscount,
        Money InvoiceShare,
        Money Tax,
        Money Total);

    /// <summary>The projection the returned quantities are read with.</summary>
    private sealed class ReturnedQuantity
    {
        /// <summary>The line the quantity belongs to.</summary>
        public int LineId { get; set; }

        /// <summary>The quantity that has come back.</summary>
        public long Quantity { get; set; }
    }
}
