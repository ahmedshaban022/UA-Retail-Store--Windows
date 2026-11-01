using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Sales;

/// <summary>
/// Reverses a posted invoice in full: a linked document that puts the money and the goods back and
/// closes the original off.
/// </summary>
/// <remarks>
/// <para>The original invoice is not edited and not deleted. It is marked as reversed, and a second
/// document carries the undo: the money that goes back, the stock that comes back at the cost it
/// left at, and the reason (PRD §5.7, SAL-06).</para>
/// <para>A posted invoice may be reversed only once. The reversal is a document of its own with its
/// own number, so the history of the till stays readable: a sale, and the document that undid
/// it.</para>
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

        string number = await SalesNumbers.NextReversalNumberAsync(context, command.BusinessDate, cancellationToken);

        var reversal = new SalesInvoice
        {
            Number = number,
            Status = DocumentStatus.Posted,
            ReversesDocumentId = invoice.Id,
            Subtotal = invoice.Subtotal,
            Discount = invoice.Discount,
            Tax = invoice.Tax,
            Total = invoice.Total,
            Paid = invoice.Total,
            Change = Domain.Money.Money.Zero,
            PaymentMethod = command.RefundMethod,
            UserId = session.Current?.UserId ?? 0,
            BusinessDate = command.BusinessDate,
            PostedUtc = clock.UtcNow,
            ClientOperationId = Guid.NewGuid(),
            Notes = command.Notes,
        };

        context.SalesInvoices.Add(reversal);

        foreach (SalesInvoiceLine line in invoice.Lines)
        {
            reversal.Lines.Add(new SalesInvoiceLine
            {
                SalesInvoice = reversal,
                ProductId = line.ProductId,
                NameSnapshot = line.NameSnapshot,
                SkuSnapshot = line.SkuSnapshot,
                BarcodeSnapshot = line.BarcodeSnapshot,
                UnitSnapshot = line.UnitSnapshot,
                QuantityScaled = line.QuantityScaled,
                QuantityScale = line.QuantityScale,
                UnitPrice = line.UnitPrice,
                Discount = line.Discount,
                InvoiceDiscountShare = line.InvoiceDiscountShare,
                DiscountPercentScaled = line.DiscountPercentScaled,
                Tax = line.Tax,
                Total = line.Total,
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
            Amount = invoice.Total,
            IsRefund = true,
        });

        await context.SaveChangesAsync(cancellationToken);

        foreach (SalesInvoiceLine line in reversal.Lines)
        {
            Product product = await context.Products
                .Include(candidate => candidate.Unit)
                .FirstAsync(candidate => candidate.Id == line.ProductId, cancellationToken);

            (StockMovement? movement, string? errorCode, string? errorMessage) =
                await InventoryLedger.ApplyAsync(
                    context,
                    product,
                    line.QuantityScale,
                    StockMovementType.Reversal,
                    command.IsResellable ? StockDisposition.Sellable : StockDisposition.Damaged,
                    line.QuantityScaled,
                    line.UnitCostSnapshot,
                    command.BusinessDate,
                    reason,
                    session.Current?.UserId,
                    reversal.Id,
                    line.Id,
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

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var receipt = new RefundReceipt(
            SalesDocumentKind.Reversal,
            reversal.Id,
            reversal.Number,
            invoice.Id,
            invoice.Number,
            reversal.BusinessDate,
            reversal.PostedUtc,
            invoice.Total,
            command.RefundMethod,
            reason,
            reversal.Lines
                .Select(line => new ReturnLineSummary(
                    0,
                    line.NameSnapshot,
                    Application.Sales.SalesRules.Unscale(line.QuantityScaled, line.QuantityScale),
                    command.IsResellable,
                    line.Total))
                .ToList());

        await auditWriter.WriteAsync(
            new AuditEntry(
                SalesAudit.InvoiceReversal,
                nameof(SalesInvoice),
                reversal.Id.ToString(CultureInfo.InvariantCulture),
                null,
                $"{reversal.Number} reverses {invoice.Number} for {invoice.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture)}",
                session.Current?.UserId),
            cancellationToken);

        return Result<RefundReceipt>.Success(receipt);
    }

    private static Result<RefundReceipt> Failure(string code, string message) =>
        Result<RefundReceipt>.Failure(code, message);
}
