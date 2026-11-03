using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Approvals;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Pos;
using StoreFlow.Application.Settings;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Pos;

/// <summary>
/// Posts a sale. One invoice, its lines, its payments and the stock movements that go with it are
/// committed together, or none of them are.
/// </summary>
/// <remarks>
/// <para>The idempotency key is what makes a double click harmless. The key is claimed in the same
/// transaction as the sale, and the invoice carries a unique index on it as well, so the second
/// click of a double click finds the first sale and repeats its answer rather than making a second
/// one (PRD POS-07).</para>
/// <para>The stock is taken off the shelf by the same ledger that put it there, so the balance and
/// the ledger cannot drift apart, and the cost that comes off is the moving average at that
/// moment, which is what the valuation is measured against (PRD §5.4, ADR-0003).</para>
/// </remarks>
public sealed class PostSaleCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IUserSession session,
    IStoreSettingsReader settingsReader,
    ISaleActivityMonitor saleMonitor)
    : ICommandHandler<PostSaleCommand, Result<SaleReceipt>>
{
    private static readonly JsonSerializerOptions ReceiptJson = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<Result<SaleReceipt>> HandleAsync(PostSaleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.SalesPost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<SaleReceipt>.Failure(permitted.Error);
        }

        if (command.ClientOperationId == Guid.Empty)
        {
            return Result<SaleReceipt>.Failure(
                PosErrorCodes.IdempotencyConflict,
                "The checkout has no operation id, so it cannot be posted safely.");
        }

        string key = command.ClientOperationId.ToString("D");
        string fingerprint = Fingerprint(command);

        // While a sale is being posted, the shop's data must not be replaced underneath it. A
        // restore that landed in the middle of a sale would lose money (PRD §8.5).
        using IDisposable saleScope = saleMonitor.Begin();

        // The key is looked at before anything is priced, so a repeated checkout is answered from
        // the ledger rather than from a second reading of the shelf.
        Result<SaleReceipt>? replay = await TryReplayAsync(key, fingerprint, command, cancellationToken);

        if (replay is not null)
        {
            return replay;
        }

        StoreSettings settings = await settingsReader.GetAsync(cancellationToken);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        int[] productIds = command.Lines.Select(line => line.ProductId).Distinct().ToArray();

        Dictionary<int, PosProductFacts> products = await PosProductReader.LoadAsync(
            context, productIds, cancellationToken);

        PosCart cart = PosCartBuilder.Build(command.Lines, products, command.InvoiceDiscount);

        if (!cart.IsValid)
        {
            PosIssue issue = cart.Issues[0];

            return Result<SaleReceipt>.Failure(issue.Code, issue.Message);
        }

        Result gate = await AuthorizeCartAsync(cart, products, settings, context, command, cancellationToken);

        if (gate.IsFailure)
        {
            return Result<SaleReceipt>.Failure(gate.Error);
        }

        Result payment = CheckPayments(cart, command.Payments);

        if (payment.IsFailure)
        {
            return Result<SaleReceipt>.Failure(payment.Error);
        }

        PosTotals totals = cart.Totals;
        decimal paidAmount = command.Payments.Sum(paymentInput => paymentInput.Amount.Amount);
        var paid = new Money(paidAmount);
        var change = new Money(ChangeOwed(command.Payments));

        var receipt = new SaleReceipt(
            0,
            string.Empty,
            command.BusinessDate,
            clock.UtcNow,
            totals with { Paid = paid, Change = change },
            cart.Lines,
            command.Payments
                .Select(paymentInput => new PaymentSummary(
                    paymentInput.Method,
                    paymentInput.Amount,
                    paymentInput.CashReceived,
                    paymentInput.Method == PaymentMethod.Cash
                        ? paymentInput.CashReceived - paymentInput.Amount
                        : Money.Zero,
                    paymentInput.Reference))
                .ToList(),
            false);

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            string number = await InvoiceNumber.NextAsync(context, command.BusinessDate, cancellationToken);

            var invoice = new SalesInvoice
            {
                Number = number,
                Status = DocumentStatus.Posted,
                Subtotal = totals.Subtotal,
                Discount = totals.Discount,
                Tax = totals.Tax,
                Total = totals.Total,
                Paid = paid,
                Change = change,
                PaymentMethod = command.Payments.Count > 0 ? command.Payments[0].Method : PaymentMethod.Cash,
                UserId = session.Current?.UserId ?? 0,
                BusinessDate = command.BusinessDate,
                PostedUtc = clock.UtcNow,
                ClientOperationId = command.ClientOperationId,
                Notes = command.Notes,
            };

            context.SalesInvoices.Add(invoice);

            List<Product> trackedProducts = await context.Products
                .Include(product => product.Unit)
                .Where(product => productIds.Contains(product.Id))
                .ToListAsync(cancellationToken);

            foreach (CartLine line in cart.Lines)
            {
                Product product = trackedProducts.First(candidate => candidate.Id == line.ProductId);

                invoice.Lines.Add(new SalesInvoiceLine
                {
                    SalesInvoice = invoice,
                    ProductId = line.ProductId,
                    NameSnapshot = line.Name,
                    SkuSnapshot = line.Sku,
                    BarcodeSnapshot = line.Barcode,
                    UnitSnapshot = line.UnitName,
                    QuantityScaled = line.QuantityScaled,
                    QuantityScale = line.UnitScale,
                    UnitPrice = line.UnitPrice,
                    Discount = line.DiscountAmount,
                    InvoiceDiscountShare = line.InvoiceDiscountShare,
                    DiscountPercentScaled = line.Discount.Kind == DiscountKind.Percent
                        ? (long)Math.Round(line.Discount.Value * 10_000m, 0, MidpointRounding.AwayFromZero)
                        : 0L,
                    Tax = line.Tax,
                    Total = line.Total,
                    UnitCostSnapshot = await CurrentAverageCostAsync(context, line.ProductId, cancellationToken),
                    TaxRateSnapshotBasisPoints = products[line.ProductId].TaxRateBasisPoints,
                    TaxPriceIncludedSnapshot = products[line.ProductId].PriceIncludesTax,
                });
            }

            foreach (PaymentInput paymentInput in command.Payments)
            {
                invoice.Payments.Add(new Payment
                {
                    SalesInvoice = invoice,
                    Method = paymentInput.Method,
                    Amount = paymentInput.Amount,
                    CashReceived = paymentInput.CashReceived,
                    Change = paymentInput.Method == PaymentMethod.Cash
                        ? paymentInput.CashReceived - paymentInput.Amount
                        : Money.Zero,
                    Reference = paymentInput.Reference,
                });
            }

            // The outcome is claimed first, so that the key is taken inside the same transaction as
            // the sale. A row left pending is the sign of a commit that was never certain.
            var outcome = new RequestOutcome
            {
                ClientOperationId = key,
                Kind = "PostSale",
                PayloadHash = fingerprint,
                Status = RequestOutcomeStatus.Pending,
                CreatedUtc = clock.UtcNow,
            };

            context.RequestOutcomes.Add(outcome);

            await context.SaveChangesAsync(cancellationToken);

            foreach (CartLine line in cart.Lines)
            {
                Product product = trackedProducts.First(candidate => candidate.Id == line.ProductId);

                (StockMovement? movement, string? errorCode, string? errorMessage) =
                    await Inventory.InventoryLedger.ApplyAsync(
                        context,
                        product,
                        line.UnitScale,
                        StockMovementType.Sale,
                        StockDisposition.Sellable,
                        -line.QuantityScaled,
                        await CurrentAverageCostAsync(context, line.ProductId, cancellationToken),
                        command.BusinessDate,
                        null,
                        session.Current?.UserId,
                        invoice.Id,
                        null,
                        clock.UtcNow);

                if (movement is null)
                {
                    await transaction.RollbackAsync(cancellationToken);

                    // The ledger speaks in inventory codes; the till only knows the point-of-sale
                    // ones, so the refusal is translated before it reaches the screen.
                    return Result<SaleReceipt>.Failure(
                        TranslateLedgerError(errorCode),
                        errorMessage ?? "There is not enough stock for this sale.");
                }
            }

            receipt = receipt with { InvoiceId = invoice.Id, Number = invoice.Number };

            outcome.Status = RequestOutcomeStatus.Committed;
            outcome.ResultJson = JsonSerializer.Serialize(receipt, ReceiptJson);
            outcome.CompletedUtc = clock.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            await auditWriter.WriteAsync(
                new AuditEntry(
                    PosAudit.SalePost,
                    nameof(SalesInvoice),
                    invoice.Id.ToString(CultureInfo.InvariantCulture),
                    null,
                    $"{invoice.Number} for {invoice.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture)}",
                    session.Current?.UserId),
                cancellationToken);

            if (command.ApprovalRequestId is int approvalRequestId)
            {
                // The approval that let the discount through is written down, so that the trail
                // shows why the sale was allowed.
                await auditWriter.WriteAsync(
                    new AuditEntry(
                        Approvals.ApprovalAudit.Applied,
                        nameof(SalesInvoice),
                        invoice.Id.ToString(CultureInfo.InvariantCulture),
                        null,
                        $"approval {approvalRequestId.ToString(CultureInfo.InvariantCulture)}",
                        session.Current?.UserId),
                    cancellationToken);
            }

            return Result<SaleReceipt>.Success(receipt);
        }
        catch (DbUpdateException)
        {
            // Another checkout with the same key won the race. Its sale is the sale.
            Result<SaleReceipt>? replayed = await TryReplayAsync(key, fingerprint, command, cancellationToken);

            if (replayed is not null)
            {
                return replayed;
            }

            throw;
        }
    }

    /// <summary>
    /// Answers a repeated checkout from the ledger, or returns null when this key has not been used
    /// yet and the sale may go ahead.
    /// </summary>
    private async Task<Result<SaleReceipt>?> TryReplayAsync(
        string key,
        string fingerprint,
        PostSaleCommand command,
        CancellationToken cancellationToken)
    {
        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        RequestOutcome? outcome = await context.RequestOutcomes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ClientOperationId == key, cancellationToken);

        if (outcome is null)
        {
            return null;
        }

        if (!string.Equals(outcome.PayloadHash, fingerprint, StringComparison.Ordinal))
        {
            return Result<SaleReceipt>.Failure(
                PosErrorCodes.IdempotencyConflict,
                "This checkout was already used with a different cart.");
        }

        if (outcome.Status == RequestOutcomeStatus.Committed && !string.IsNullOrWhiteSpace(outcome.ResultJson))
        {
            SaleReceipt? stored = JsonSerializer.Deserialize<SaleReceipt>(outcome.ResultJson, ReceiptJson);

            if (stored is not null)
            {
                return Result<SaleReceipt>.Success(stored with { WasReplay = true });
            }
        }

        // The row is still pending, which means a previous attempt did not finish. The invoice is
        // the truth: if it is there, the sale was committed and the answer is repeated; if it is
        // not, the attempt rolled back and the checkout may be tried again.
        SalesInvoice? invoice = await context.SalesInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ClientOperationId == command.ClientOperationId, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        SaleReceipt rebuilt = await RebuildReceiptAsync(context, invoice, cancellationToken);

        return Result<SaleReceipt>.Success(rebuilt with { WasReplay = true });
    }

    /// <summary>Reads a posted sale back out of the database as a receipt.</summary>
    private static async Task<SaleReceipt> RebuildReceiptAsync(
        StoreFlowDbContext context,
        SalesInvoice invoice,
        CancellationToken cancellationToken)
    {
        List<SalesInvoiceLine> lines = await context.SalesInvoiceLines
            .AsNoTracking()
            .Where(line => line.SalesInvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);

        List<Payment> payments = await context.Payments
            .AsNoTracking()
            .Where(payment => payment.SalesInvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);

        var cartLines = lines.Select(line =>
        {
            decimal quantity = line.QuantityScaled / (decimal)Math.Pow(10, line.QuantityScale);
            var gross = new Money(line.UnitPrice.Amount * quantity);
            var net = gross - line.Discount - line.InvoiceDiscountShare;

            var taxableBase = PosRules.TaxableBaseOn(
                net,
                line.TaxRateSnapshotBasisPoints,
                line.TaxPriceIncludedSnapshot);

            return new CartLine(
                line.ProductId,
                line.SkuSnapshot,
                line.NameSnapshot,
                line.NameSnapshot,
                line.BarcodeSnapshot,
                line.UnitSnapshot,
                line.QuantityScale,
                quantity,
                line.QuantityScaled,
                line.UnitPrice,
                DiscountSpec.None,
                line.Discount,
                gross,
                net,
                line.InvoiceDiscountShare,
                taxableBase,
                line.Tax,
                line.Total);
        }).ToList();

        var lineDiscounts = new Money(lines.Sum(line => line.Discount.Amount));
        var invoiceShares = new Money(lines.Sum(line => line.InvoiceDiscountShare.Amount));

        var totals = new PosTotals(
            invoice.Subtotal,
            lineDiscounts,
            invoiceShares,
            invoice.Discount,
            invoice.Tax,
            invoice.Total,
            invoice.Paid,
            invoice.Change);

        var paymentSummaries = payments.Select(payment => new PaymentSummary(
            payment.Method,
            payment.Amount,
            payment.CashReceived,
            payment.Change,
            payment.Reference)).ToList();

        return new SaleReceipt(
            invoice.Id,
            invoice.Number,
            invoice.BusinessDate,
            invoice.PostedUtc,
            totals,
            cartLines,
            paymentSummaries,
            false);
    }

    /// <summary>
    /// The gates a sale has to pass: the price may only be changed by somebody allowed to change
    /// it, and a discount above the shop's limit has to be approved by somebody allowed to approve
    /// it (PRD POS-03, §3).
    /// </summary>
    private async Task<Result> AuthorizeCartAsync(
        PosCart cart,
        IReadOnlyDictionary<int, PosProductFacts> products,
        StoreSettings settings,
        StoreFlowDbContext context,
        PostSaleCommand command,
        CancellationToken cancellationToken)
    {
        bool priceChanged = cart.Lines.Any(line =>
            PosCartBuilder.IsPriceOverride(products[line.ProductId], line.UnitPrice));

        if (priceChanged)
        {
            Result pricePermitted = await authorization.RequireAsync(Permission.SalesPriceOverride, cancellationToken);

            if (pricePermitted.IsFailure)
            {
                return Result.Failure(
                    PosErrorCodes.PriceOverrideNotAllowed,
                    "The price of a line is not the catalogue price, and you may not change it.");
            }
        }

        decimal discountPercent = PosCartBuilder.EffectiveDiscountPercent(
            cart.Totals.Subtotal, cart.Totals.Discount);

        if (discountPercent > settings.MaximumDiscountPercent)
        {
            Result discountPermitted = await authorization.RequireAsync(
                Permission.SalesDiscountOverLimit, cancellationToken);

            if (discountPermitted.IsFailure)
            {
                // The sale is held for a manager. Nothing is written to the ledger; the till asks
                // for the approval and the cashier confirms the sale again once it has been given.
                if (command.ApprovalRequestId is int approvalRequestId)
                {
                    return await CheckApprovalAsync(
                        context,
                        approvalRequestId,
                        DiscountApproval.CartFingerprint(command.Lines, command.InvoiceDiscount),
                        cancellationToken);
                }

                return Result.Failure(
                    PosErrorCodes.DiscountApprovalRequired,
                    "The discount is over the limit, so it is waiting for a manager to approve it.");
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Checks that the approval really is the approval of this cart by this cashier. An approval
    /// that has not been given, that belongs to another cart or to another cashier is no approval
    /// at all.
    /// </summary>
    private async Task<Result> CheckApprovalAsync(
        StoreFlowDbContext context,
        int approvalRequestId,
        string cartFingerprint,
        CancellationToken cancellationToken)
    {
        ApprovalRequest? request = await context.ApprovalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == approvalRequestId, cancellationToken);

        if (request is null)
        {
            return Result.Failure(
                ApprovalErrorCodes.NotFound,
                "That approval is not waiting for anybody.");
        }

        if (request.Kind != ApprovalKind.DiscountOverLimit)
        {
            return Result.Failure(
                ApprovalErrorCodes.Mismatch,
                "That approval is about something else.");
        }

        if (request.Status != ApprovalStatus.Approved)
        {
            return Result.Failure(
                ApprovalErrorCodes.NotApproved,
                "That approval has not been given yet.");
        }

        if (request.RequestedByUserId != (session.Current?.UserId ?? 0))
        {
            return Result.Failure(
                ApprovalErrorCodes.Mismatch,
                "That approval was asked for by somebody else.");
        }

        DiscountApprovalPayload? payload = DiscountApproval.ReadPayload(request.PayloadJson);

        if (payload is null ||
            !string.Equals(payload.Fingerprint, cartFingerprint, StringComparison.Ordinal))
        {
            return Result.Failure(
                ApprovalErrorCodes.Mismatch,
                "The cart has changed since the approval was asked for, so it has to be asked for again.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Checks that the customer has paid enough. Cash that is short is refused; the change is what
    /// is handed back.
    /// </summary>
    private static Result CheckPayments(PosCart cart, IReadOnlyList<PaymentInput> payments)
    {
        if (payments.Count == 0)
        {
            return Result.Failure(
                PosErrorCodes.InsufficientPayment,
                "The customer has not paid yet.");
        }

        decimal paid = payments.Sum(payment => payment.Amount.Amount);

        if (paid < cart.Totals.Total.Amount)
        {
            return Result.Failure(
                PosErrorCodes.InsufficientPayment,
                "The customer has not paid enough.");
        }

        foreach (PaymentInput payment in payments)
        {
            if (payment.Amount.IsNegative)
            {
                return Result.Failure(
                    PosErrorCodes.InsufficientPayment,
                    "A payment cannot be negative.");
            }

            if (payment.Method == PaymentMethod.Cash && payment.CashReceived < payment.Amount)
            {
                return Result.Failure(
                    PosErrorCodes.InsufficientCash,
                    "The cash handed over is less than the amount it is paying.");
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// The change owed to the customer: the cash that was handed over, less the cash that was
    /// applied to the invoice. It is the same figure the payment rows carry, so the invoice, the
    /// receipt and the till cannot tell the cashier two different stories.
    /// </summary>
    /// <param name="payments">The payments the customer made.</param>
    /// <returns>The change, in pounds.</returns>
    private static decimal ChangeOwed(IReadOnlyList<PaymentInput> payments) =>
        payments
            .Where(payment => payment.Method == PaymentMethod.Cash)
            .Sum(payment => (payment.CashReceived - payment.Amount).Amount);

    /// <summary>
    /// Translates a refusal from the stock ledger into the point-of-sale code the till knows how
    /// to show. A sale that finds no stock is an out-of-stock sale, whatever the ledger calls it.
    /// </summary>
    /// <param name="errorCode">The code the ledger answered with.</param>
    /// <returns>The point-of-sale code to answer with.</returns>
    private static string TranslateLedgerError(string? errorCode) =>
        errorCode is null || errorCode == Inventory.InventoryLedger.NegativeStockCode
            ? PosErrorCodes.InsufficientStock
            : errorCode;

    /// <summary>The moving average cost of a product at this moment, which is the cost that comes off.</summary>
    private static async Task<Money> CurrentAverageCostAsync(
        StoreFlowDbContext context,
        int productId,
        CancellationToken cancellationToken)
    {
        StockBalance? balance = await context.StockBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ProductId == productId, cancellationToken);

        return balance?.AverageUnitCost ?? Money.Zero;
    }

    /// <summary>
    /// A fingerprint of what was asked for. The same key with a different cart is a mistake worth
    /// refusing, not a sale to make twice.
    /// </summary>
    private static string Fingerprint(PostSaleCommand command)
    {
        var builder = new StringBuilder();

        builder.Append(command.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        builder.Append('|').Append(command.InvoiceDiscount.Kind).Append(':')
            .Append(command.InvoiceDiscount.Value.ToString("0.####", CultureInfo.InvariantCulture));

        foreach (SaleLineInput line in command.Lines)
        {
            builder.Append('|').Append(line.ProductId).Append(':')
                .Append(line.Quantity.ToString("0.####", CultureInfo.InvariantCulture)).Append(':')
                .Append(line.UnitPrice.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(':')
                .Append(line.LineDiscount.Kind).Append(':')
                .Append(line.LineDiscount.Value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        foreach (PaymentInput payment in command.Payments)
        {
            builder.Append('|').Append(payment.Method).Append(':')
                .Append(payment.Amount.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(':')
                .Append(payment.CashReceived.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(':')
                .Append(payment.Reference);
        }

        builder.Append('|').Append(command.Notes ?? string.Empty);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexString(hash);
    }
}
