using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Reporting;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Reporting;

/// <summary>
/// Reads the reports straight from the posted documents and the stock ledger.
/// </summary>
/// <remarks>
/// <para>Every figure is worked out by <see cref="ReportRules"/>, so the money can be checked by
/// hand against the documents it came from.</para>
/// <para>The stock is always replayed from the ledger and never read from the cached balance: a
/// report dated before a receipt has to leave that receipt out, and the cache cannot answer that.</para>
/// </remarks>
public sealed class EfReportReader(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IClock clock) : IReportReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DailySalesRow>> ReadDailySalesAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<SaleAmountRow> invoices = await ReadSaleAmountsAsync(context, filter, cancellationToken);
        List<ReturnAmountRow> returns = await ReadReturnAmountsAsync(context, filter, cancellationToken);

        var days = new SortedDictionary<DateOnly, DayAccumulator>();

        foreach (SaleAmountRow invoice in invoices)
        {
            if (!days.TryGetValue(invoice.Date, out DayAccumulator? day))
            {
                day = new DayAccumulator();
                days[invoice.Date] = day;
            }

            SaleAmounts amounts = new(invoice.Total, invoice.Discount, invoice.Tax);

            if (invoice.IsReversal)
            {
                day.ReversedGross += ReportRules.GrossSales(amounts);
                day.ReversedDiscounts += amounts.Discount;
                day.ReversedTax += amounts.Tax;
                day.ReversedNet += ReportRules.NetSales(amounts);
            }
            else
            {
                day.InvoiceCount += 1;
                day.GrossSales += ReportRules.GrossSales(amounts);
                day.Discounts += amounts.Discount;
                day.Tax += amounts.Tax;
                day.NetSales += ReportRules.NetSales(amounts);
            }
        }

        foreach (ReturnAmountRow salesReturn in returns)
        {
            if (!days.TryGetValue(salesReturn.Date, out DayAccumulator? day))
            {
                day = new DayAccumulator();
                days[salesReturn.Date] = day;
            }

            day.Returns += ReportRules.NetRefund(new ReturnAmounts(salesReturn.RefundTotal, salesReturn.RefundTax));
        }

        var rows = new List<DailySalesRow>(days.Count);

        foreach (KeyValuePair<DateOnly, DayAccumulator> entry in days)
        {
            DayAccumulator day = entry.Value;

            Money grossSales = day.GrossSales - day.ReversedGross;
            Money discounts = day.Discounts - day.ReversedDiscounts;
            Money tax = day.Tax - day.ReversedTax;
            Money netSales = day.NetSales - day.ReversedNet - day.Returns;

            rows.Add(new DailySalesRow(entry.Key, day.InvoiceCount, grossSales, discounts, tax, day.Returns, netSales));
        }

        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesByProductRow>> ReadSalesByProductAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<LineAmountRow> lines = await ReadSaleLinesAsync(context, filter, cancellationToken);
        List<ReturnLineAmountRow> returnLines = await ReadReturnLinesAsync(context, filter, cancellationToken);

        var products = new Dictionary<int, ProductAccumulator>();

        foreach (LineAmountRow line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out ProductAccumulator? product))
            {
                product = new ProductAccumulator(line.ProductId, line.Sku, line.Name, null);
                products[line.ProductId] = product;
            }

            LineAmounts amounts = new(
                line.Total,
                line.Discount,
                line.InvoiceDiscountShare,
                line.Tax,
                line.UnitCost,
                line.QuantityScaled,
                line.QuantityScale);

            if (line.IsReversal)
            {
                product.Quantity -= ReportRules.Quantity(line.QuantityScaled, line.QuantityScale);
                product.NetRevenue -= ReportRules.LineNetRevenue(amounts);
                product.CostOfGoodsSold -= ReportRules.LineCostOfGoodsSold(amounts);
            }
            else
            {
                product.Quantity += ReportRules.Quantity(line.QuantityScaled, line.QuantityScale);
                product.NetRevenue += ReportRules.LineNetRevenue(amounts);
                product.CostOfGoodsSold += ReportRules.LineCostOfGoodsSold(amounts);
            }
        }

        // A return takes the revenue and the cost of the goods back off the product it came from.
        foreach (ReturnLineAmountRow line in returnLines)
        {
            if (!products.TryGetValue(line.ProductId, out ProductAccumulator? product))
            {
                product = new ProductAccumulator(line.ProductId, line.Sku, line.Name, null);
                products[line.ProductId] = product;
            }

            product.Quantity -= ReportRules.Quantity(line.QuantityScaled, line.QuantityScale);
            product.NetRevenue -= line.NetRefund;
            product.CostOfGoodsSold -= ReportRules.ReturnCostOfGoodsSold(
                new ReturnLineAmounts(line.RefundAmount, line.OriginalUnitCost, line.QuantityScaled, line.QuantityScale));
        }

        var rows = new List<SalesByProductRow>(products.Count);

        foreach (ProductAccumulator product in products.Values)
        {
            rows.Add(new SalesByProductRow(
                product.ProductId,
                product.Sku,
                product.Name,
                product.NameAr,
                product.Quantity,
                product.NetRevenue,
                product.CostOfGoodsSold,
                product.NetRevenue - product.CostOfGoodsSold));
        }

        return rows
            .OrderByDescending(row => row.NetRevenue)
            .ThenBy(row => row.Sku, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BestSellerRow>> ReadBestSellersAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IReadOnlyList<SalesByProductRow> rows = await ReadSalesByProductAsync(filter, cancellationToken);

        IEnumerable<SalesByProductRow> ranked = filter.RankByRevenue
            ? rows.OrderByDescending(row => row.NetRevenue).ThenBy(row => row.Sku, StringComparer.Ordinal)
            : rows.OrderByDescending(row => row.Quantity).ThenBy(row => row.Sku, StringComparer.Ordinal);

        int limit = Math.Clamp(filter.TopCount, 1, 500);

        var result = new List<BestSellerRow>();
        int rank = 1;

        foreach (SalesByProductRow row in ranked.Take(limit))
        {
            result.Add(new BestSellerRow(rank, row.ProductId, row.Sku, row.Name, row.NameAr, row.Quantity, row.NetRevenue));
            rank++;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockOnHandRow>> ReadStockOnHandAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        DateOnly asOf = filter.AsOfDate ?? clock.LocalToday;

        IReadOnlyDictionary<int, Position> positions = await ReadPositionsAsync(context, asOf, cancellationToken);

        List<ProductFacts> products = await ReadProductFactsAsync(context, filter, cancellationToken);

        var rows = new List<StockOnHandRow>();

        foreach (ProductFacts product in products)
        {
            positions.TryGetValue(product.ProductId, out Position position);

            // The position is read in the scale the movements were written in, which is the scale the
            // balance carries and not necessarily the product's current unit.
            decimal quantity = ReportRules.Quantity(position.Quantity, position.Scale);
            decimal reorderLevel = ReportRules.Quantity(product.ReorderLevelScaled, product.Precision);

            bool belowReorder = product.ReorderLevelScaled > 0 &&
                                position.Quantity <= InventoryLedger.Rescale(product.ReorderLevelScaled, product.Precision, position.Scale);

            bool keep = filter.StockStatus switch
            {
                StockStatusFilter.BelowReorder => belowReorder,
                StockStatusFilter.OutOfStock => position.Quantity == 0,
                _ => position.Quantity != 0,
            };

            if (!keep)
            {
                continue;
            }

            Money average = position.Quantity != 0
                ? Money.FromMinorUnits(position.AverageMinor)
                : Money.Zero;

            Money value = InventoryLedger.ValueOf(position.Quantity, product.Precision, average);

            rows.Add(new StockOnHandRow(
                product.ProductId,
                product.Sku,
                product.Name,
                product.NameAr,
                product.Category,
                quantity,
                reorderLevel,
                average,
                value,
                belowReorder));
        }

        return rows
            .OrderBy(row => row.Category, StringComparer.Ordinal)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockValuationRow>> ReadStockValuationAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        DateOnly asOf = filter.AsOfDate ?? clock.LocalToday;

        IReadOnlyDictionary<int, Position> positions = await ReadPositionsAsync(context, asOf, cancellationToken);

        List<ProductFacts> products = await ReadProductFactsAsync(context, filter, cancellationToken);

        var rows = new List<StockValuationRow>();

        foreach (ProductFacts product in products)
        {
            if (!positions.TryGetValue(product.ProductId, out Position position) || position.Quantity == 0)
            {
                continue;
            }

            Money average = Money.FromMinorUnits(position.AverageMinor);
            Money value = InventoryLedger.ValueOf(position.Quantity, position.Scale, average);

            rows.Add(new StockValuationRow(
                product.ProductId,
                product.Sku,
                product.Name,
                ReportRules.Quantity(position.Quantity, position.Scale),
                average,
                value));
        }

        return rows
            .OrderByDescending(row => row.Value.Amount)
            .ThenBy(row => row.Sku, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<StockMovementsReport> ReadStockMovementsAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<StockMovement> movements = context.StockMovements
            .AsNoTracking()
            .Include(movement => movement.Product);

        if (filter.ProductId is int productId)
        {
            movements = movements.Where(movement => movement.ProductId == productId);
        }

        if (filter.From != DateOnly.MinValue)
        {
            movements = movements.Where(movement => movement.BusinessDate >= filter.From);
        }

        if (filter.To != DateOnly.MaxValue)
        {
            movements = movements.Where(movement => movement.BusinessDate <= filter.To);
        }

        // How many movements there are in all. The ledger is the one report that can hold more
        // rows than a screen can show, and a shop that is shown a part of it has to be told how
        // big the whole of it is.
        int total = await movements.CountAsync(cancellationToken);

        IQueryable<StockMovement> newest = movements
            .OrderByDescending(movement => movement.BusinessDate)
            .ThenByDescending(movement => movement.Id);

        if (filter.RowLimit is int limit)
        {
            newest = newest.Take(limit);
        }

        List<StockMovement> rows = await newest.ToListAsync(cancellationToken);

        var result = new List<StockMovementRow>(rows.Count);

        foreach (StockMovement movement in rows)
        {
            decimal scale = (decimal)Math.Pow(10, movement.QuantityScale);

            result.Add(new StockMovementRow(
                movement.BusinessDate,
                movement.PostedUtc,
                movement.Product?.Sku ?? string.Empty,
                movement.Product?.Name ?? string.Empty,
                movement.MovementType.ToString(),
                movement.Disposition.ToString(),
                movement.QuantityDeltaScaled / scale,
                movement.UnitCost,
                movement.RunningBalanceScaled / scale,
                movement.Reason));
        }

        return new StockMovementsReport(result, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseBySupplierRow>> ReadPurchasesBySupplierAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<PurchaseDocument> documents = context.PurchaseDocuments
            .AsNoTracking()
            .Where(document => document.Status != DocumentStatus.Draft &&
                               document.Status != DocumentStatus.Cancelled);

        if (filter.From != DateOnly.MinValue)
        {
            documents = documents.Where(document => document.BusinessDate >= filter.From);
        }

        if (filter.To != DateOnly.MaxValue)
        {
            documents = documents.Where(document => document.BusinessDate <= filter.To);
        }

        if (filter.SupplierId is int supplierId)
        {
            documents = documents.Where(document => document.SupplierId == supplierId);
        }

        List<PurchaseAmountRow> rows = await documents
            .Select(document => new PurchaseAmountRow
            {
                SupplierId = document.SupplierId ?? 0,
                Supplier = document.Supplier == null ? string.Empty : document.Supplier.Name,
                Subtotal = document.Subtotal,
                Discount = document.Discount,
                Tax = document.Tax,
                Total = document.Total,
                IsReversal = document.ReversesDocumentId != null,
            })
            .ToListAsync(cancellationToken);

        var suppliers = new Dictionary<int, SupplierAccumulator>();

        foreach (PurchaseAmountRow row in rows)
        {
            if (!suppliers.TryGetValue(row.SupplierId, out SupplierAccumulator? supplier))
            {
                supplier = new SupplierAccumulator(row.SupplierId, row.Supplier);
                suppliers[row.SupplierId] = supplier;
            }

            if (row.IsReversal)
            {
                supplier.Subtotal -= row.Subtotal;
                supplier.Discount -= row.Discount;
                supplier.Tax -= row.Tax;
                supplier.Total -= row.Total;
            }
            else
            {
                supplier.DocumentCount += 1;
                supplier.Subtotal += row.Subtotal;
                supplier.Discount += row.Discount;
                supplier.Tax += row.Tax;
                supplier.Total += row.Total;
            }
        }

        return suppliers.Values
            .Select(supplier => new PurchaseBySupplierRow(
                supplier.SupplierId,
                supplier.Supplier,
                supplier.DocumentCount,
                supplier.Subtotal,
                supplier.Discount,
                supplier.Tax,
                supplier.Total))
            .OrderByDescending(row => row.Total.Amount)
            .ThenBy(row => row.Supplier, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ProfitAndLossReport> ReadProfitAndLossAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<SaleAmountRow> invoices = await ReadSaleAmountsAsync(context, filter, cancellationToken);
        List<ReturnAmountRow> returns = await ReadReturnAmountsAsync(context, filter, cancellationToken);

        // The profit and loss is a statement about the whole shop. An invoice's total cannot be
        // split by category, so the report is not narrowed by one: a report that took its net
        // sales from every invoice and its cost of goods from one category would not add up.
        ReportFilter wholeShop = filter with { CategoryId = null };

        List<LineAmountRow> lines = await ReadSaleLinesAsync(context, wholeShop, cancellationToken);
        List<ReturnLineAmountRow> returnLines = await ReadReturnLinesAsync(context, wholeShop, cancellationToken);

        Money netSales = Money.Zero;
        Money discounts = Money.Zero;
        Money tax = Money.Zero;

        foreach (SaleAmountRow invoice in invoices)
        {
            SaleAmounts amounts = new(invoice.Total, invoice.Discount, invoice.Tax);
            int sign = invoice.IsReversal ? -1 : 1;

            netSales += ReportRules.NetSales(amounts) * sign;
            discounts += amounts.Discount * sign;
            tax += amounts.Tax * sign;
        }

        foreach (ReturnAmountRow salesReturn in returns)
        {
            netSales -= ReportRules.NetRefund(new ReturnAmounts(salesReturn.RefundTotal, salesReturn.RefundTax));
        }

        Money costOfGoodsSold = Money.Zero;

        foreach (LineAmountRow line in lines)
        {
            LineAmounts amounts = new(
                line.Total,
                line.Discount,
                line.InvoiceDiscountShare,
                line.Tax,
                line.UnitCost,
                line.QuantityScaled,
                line.QuantityScale);

            int sign = line.IsReversal ? -1 : 1;

            costOfGoodsSold += ReportRules.LineCostOfGoodsSold(amounts) * sign;
        }

        foreach (ReturnLineAmountRow line in returnLines)
        {
            costOfGoodsSold -= ReportRules.ReturnCostOfGoodsSold(
                new ReturnLineAmounts(line.RefundAmount, line.OriginalUnitCost, line.QuantityScaled, line.QuantityScale));
        }

        IReadOnlyList<PurchaseBySupplierRow> purchases = await ReadPurchasesBySupplierAsync(filter, cancellationToken);

        Money purchaseSpend = ReportRules.Sum(purchases.Select(purchase => purchase.Total));

        var lines2 = new List<ProfitAndLossLine>
        {
            new("net_sales", netSales),
            new("discounts", discounts),
            new("tax", tax),
            new("cost_of_goods_sold", costOfGoodsSold),
            new("gross_profit", netSales - costOfGoodsSold, true),
            new("purchases", purchaseSpend),
        };

        return new ProfitAndLossReport(filter.From, filter.To, lines2);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CashierPerformanceRow>> ReadCashierPerformanceAsync(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<SaleAmountRow> invoices = await ReadSaleAmountsAsync(context, filter, cancellationToken);

        var cashiers = new Dictionary<int, CashierAccumulator>();

        foreach (SaleAmountRow invoice in invoices)
        {
            if (!cashiers.TryGetValue(invoice.UserId, out CashierAccumulator? cashier))
            {
                cashier = new CashierAccumulator(invoice.UserId, invoice.CashierName);
                cashiers[invoice.UserId] = cashier;
            }

            SaleAmounts amounts = new(invoice.Total, invoice.Discount, invoice.Tax);
            int sign = invoice.IsReversal ? -1 : 1;

            if (!invoice.IsReversal)
            {
                cashier.InvoiceCount += 1;
            }

            cashier.NetSales += ReportRules.NetSales(amounts) * sign;
        }

        var rows = new List<CashierPerformanceRow>(cashiers.Count);

        foreach (CashierAccumulator cashier in cashiers.Values)
        {
            rows.Add(new CashierPerformanceRow(
                cashier.UserId,
                cashier.DisplayName,
                cashier.InvoiceCount,
                cashier.NetSales,
                cashier.InvoiceCount == 0 ? Money.Zero : ReportRules.Divide(cashier.NetSales, cashier.InvoiceCount)));
        }

        return rows
            .OrderByDescending(row => row.NetSales.Amount)
            .ThenBy(row => row.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReturnsRow>> ReadReturnsAsync(
        ReportFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<ReturnLineAmountRow> lines = await ReadReturnLinesAsync(context, filter, cancellationToken);

        var products = new Dictionary<int, ReturnsAccumulator>();

        foreach (ReturnLineAmountRow line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out ReturnsAccumulator? product))
            {
                product = new ReturnsAccumulator(line.ProductId, line.Sku, line.Name);
                products[line.ProductId] = product;
            }

            product.Count += 1;
            product.Quantity += ReportRules.Quantity(line.QuantityScaled, line.QuantityScale);
            product.Amount += line.NetRefund;

            if (!string.IsNullOrWhiteSpace(line.Reason) &&
                !product.Reasons.Contains(line.Reason, StringComparison.Ordinal))
            {
                product.Reasons = product.Reasons.Length == 0
                    ? line.Reason
                    : $"{product.Reasons}, {line.Reason}";
            }
        }

        return products.Values
            .Select(product => new ReturnsRow(
                product.ProductId,
                product.Sku,
                product.Name,
                product.Count,
                product.Quantity,
                product.Amount,
                product.Reasons))
            .OrderByDescending(row => row.Amount.Amount)
            .ThenBy(row => row.Sku, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CashierShiftRow>> ReadCashierShiftsAsync(
        ReportFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        DateOnly from = filter.From;
        DateOnly to = filter.To;

        List<CashMovementRow> taken = await ReadCashRowsAsync(context, from, to, isRefund: false, cancellationToken);
        List<CashMovementRow> refunded = await ReadCashRowsAsync(context, from, to, isRefund: true, cancellationToken);

        var shifts = new Dictionary<(DateOnly Date, int UserId), ShiftAccumulator>();

        foreach (CashMovementRow row in taken.Concat(refunded))
        {
            if (filter.CashierUserId is int cashierUserId && row.UserId != cashierUserId)
            {
                continue;
            }

            (DateOnly Date, int UserId) key = (row.Date, row.UserId);

            if (!shifts.TryGetValue(key, out ShiftAccumulator? shift))
            {
                shift = new ShiftAccumulator(row.Date, row.UserId, row.CashierName);
                shifts[key] = shift;
            }

            if (row.IsRefund)
            {
                shift.CashRefunded += row.Amount;
            }
            else
            {
                shift.CashTaken += row.Amount;
            }
        }

        var rows = new List<CashierShiftRow>(shifts.Count);

        foreach (ShiftAccumulator shift in shifts.Values)
        {
            DrawerCashSummary drawer = await DrawerCashStore.ReadAsync(
                context, shift.Date, shift.UserId, cancellationToken);

            Money closing = drawer.Opening + shift.CashTaken - shift.CashRefunded;

            bool counted = drawer.Counted != Money.Zero;

            rows.Add(new CashierShiftRow(
                shift.Date,
                shift.UserId,
                shift.CashierName,
                drawer.Opening,
                shift.CashTaken,
                shift.CashRefunded,
                closing,
                counted ? drawer.Counted : null,
                counted ? drawer.Counted - closing : null));
        }

        return rows
            .OrderByDescending(row => row.Date)
            .ThenBy(row => row.Cashier, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads the cash that went into or came out of the drawer on a day.</summary>
    private static async Task<List<CashMovementRow>> ReadCashRowsAsync(
        StoreFlowDbContext context,
        DateOnly from,
        DateOnly to,
        bool isRefund,
        CancellationToken cancellationToken)
    {
        IQueryable<Payment> payments = context.Payments
            .AsNoTracking()
            .Where(payment => payment.Method == PaymentMethod.Cash && payment.IsRefund == isRefund);

        if (isRefund)
        {
            payments = payments.Where(payment => payment.SalesReturn != null);

            if (from != DateOnly.MinValue)
            {
                payments = payments.Where(payment => payment.SalesReturn!.BusinessDate >= from);
            }

            if (to != DateOnly.MaxValue)
            {
                payments = payments.Where(payment => payment.SalesReturn!.BusinessDate <= to);
            }

            return await payments
                .Select(payment => new CashMovementRow
                {
                    Date = payment.SalesReturn!.BusinessDate,
                    UserId = payment.SalesReturn.UserId,
                    CashierName = payment.SalesReturn.User == null ? string.Empty : payment.SalesReturn.User.DisplayName,
                    Amount = payment.Amount,
                    IsRefund = true,
                })
                .ToListAsync(cancellationToken);
        }

        payments = payments.Where(payment => payment.SalesInvoice != null);

        if (from != DateOnly.MinValue)
        {
            payments = payments.Where(payment => payment.SalesInvoice!.BusinessDate >= from);
        }

        if (to != DateOnly.MaxValue)
        {
            payments = payments.Where(payment => payment.SalesInvoice!.BusinessDate <= to);
        }

        return await payments
            .Select(payment => new CashMovementRow
            {
                Date = payment.SalesInvoice!.BusinessDate,
                UserId = payment.SalesInvoice.UserId,
                CashierName = payment.SalesInvoice.User == null ? string.Empty : payment.SalesInvoice.User.DisplayName,
                Amount = payment.Amount,
                IsRefund = false,
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads the money of every sale in the period.</summary>
    private static async Task<List<SaleAmountRow>> ReadSaleAmountsAsync(
        StoreFlowDbContext context,
        ReportFilter filter,
        CancellationToken cancellationToken)
    {
        IQueryable<SalesInvoice> invoices = context.SalesInvoices
            .AsNoTracking()
            .Where(invoice => invoice.Status == DocumentStatus.Posted ||
                              invoice.Status == DocumentStatus.Reversed);

        if (filter.From != DateOnly.MinValue)
        {
            invoices = invoices.Where(invoice => invoice.BusinessDate >= filter.From);
        }

        if (filter.To != DateOnly.MaxValue)
        {
            invoices = invoices.Where(invoice => invoice.BusinessDate <= filter.To);
        }

        if (filter.CashierUserId is int cashierUserId)
        {
            invoices = invoices.Where(invoice => invoice.UserId == cashierUserId);
        }

        return await invoices
            .OrderBy(invoice => invoice.Id)
            .Select(invoice => new SaleAmountRow
            {
                Date = invoice.BusinessDate,
                UserId = invoice.UserId,
                CashierName = invoice.User == null ? string.Empty : invoice.User.DisplayName,
                Total = invoice.Total,
                Discount = invoice.Discount,
                Tax = invoice.Tax,
                IsReversal = invoice.ReversesDocumentId != null,
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads the money of every return in the period.</summary>
    private static async Task<List<ReturnAmountRow>> ReadReturnAmountsAsync(
        StoreFlowDbContext context,
        ReportFilter filter,
        CancellationToken cancellationToken)
    {
        IQueryable<SalesReturn> returns = context.SalesReturns.AsNoTracking();

        if (filter.From != DateOnly.MinValue)
        {
            returns = returns.Where(salesReturn => salesReturn.BusinessDate >= filter.From);
        }

        if (filter.To != DateOnly.MaxValue)
        {
            returns = returns.Where(salesReturn => salesReturn.BusinessDate <= filter.To);
        }

        if (filter.CashierUserId is int cashierUserId)
        {
            returns = returns.Where(salesReturn => salesReturn.UserId == cashierUserId);
        }

        return await returns
            .OrderBy(salesReturn => salesReturn.Id)
            .Select(salesReturn => new ReturnAmountRow
            {
                Date = salesReturn.BusinessDate,
                RefundTotal = salesReturn.RefundTotal,
                RefundTax = salesReturn.RefundTax,
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads every line of every sale in the period.</summary>
    private static async Task<List<LineAmountRow>> ReadSaleLinesAsync(
        StoreFlowDbContext context,
        ReportFilter filter,
        CancellationToken cancellationToken)
    {
        IQueryable<SalesInvoice> invoices = context.SalesInvoices
            .AsNoTracking()
            .Where(invoice => invoice.Status == DocumentStatus.Posted ||
                              invoice.Status == DocumentStatus.Reversed);

        if (filter.From != DateOnly.MinValue)
        {
            invoices = invoices.Where(invoice => invoice.BusinessDate >= filter.From);
        }

        if (filter.To != DateOnly.MaxValue)
        {
            invoices = invoices.Where(invoice => invoice.BusinessDate <= filter.To);
        }

        if (filter.CashierUserId is int cashierUserId)
        {
            invoices = invoices.Where(invoice => invoice.UserId == cashierUserId);
        }

        // The lines are read on their own, so that the query stays a plain EXISTS: SQLite cannot
        // translate a correlated projection.
        IQueryable<SalesInvoiceLine> saleLines = context.SalesInvoiceLines
            .AsNoTracking()
            .Where(line => invoices.Any(invoice => invoice.Id == line.SalesInvoiceId));

        if (filter.CategoryId is int categoryId)
        {
            // The category narrows the lines, not the invoices. An invoice can hold lines from
            // more than one category, and a report that kept every line of a matching invoice
            // would carry another category's money into a report about this one. The returns are
            // narrowed the same way, so the two halves of a report agree.
            saleLines = saleLines.Where(line => line.Product != null && line.Product.CategoryId == categoryId);
        }

        List<LineAmountRow> rows = await saleLines
            .OrderBy(line => line.Id)
            .Select(line => new LineAmountRow
            {
                ProductId = line.ProductId,
                Sku = line.SkuSnapshot,
                Name = line.NameSnapshot,
                Total = line.Total,
                Discount = line.Discount,
                InvoiceDiscountShare = line.InvoiceDiscountShare,
                Tax = line.Tax,
                UnitCost = line.UnitCostSnapshot,
                QuantityScaled = line.QuantityScaled,
                QuantityScale = line.QuantityScale,
                IsReversal = line.SalesInvoice!.ReversesDocumentId != null,
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <summary>
    /// The reason a line came back for.
    /// </summary>
    /// <remarks>
    /// A line may carry its own reason; when it does not, the reason of the return it belongs to
    /// applies to it. The report would otherwise say nothing about why goods came back, when the
    /// cashier told the customer why.
    /// </remarks>
    private static string? ReasonOf(SalesReturnLine line, SalesReturn salesReturn) =>
        string.IsNullOrWhiteSpace(line.Reason) ? salesReturn.Reason : line.Reason;

    /// <summary>Reads every line of every return in the period.</summary>
    private static async Task<List<ReturnLineAmountRow>> ReadReturnLinesAsync(
        StoreFlowDbContext context,
        ReportFilter filter,
        CancellationToken cancellationToken)
    {
        IQueryable<SalesReturn> returns = context.SalesReturns
            .AsNoTracking()
            .Include(salesReturn => salesReturn.Lines);

        if (filter.From != DateOnly.MinValue)
        {
            returns = returns.Where(salesReturn => salesReturn.BusinessDate >= filter.From);
        }

        if (filter.To != DateOnly.MaxValue)
        {
            returns = returns.Where(salesReturn => salesReturn.BusinessDate <= filter.To);
        }

        if (filter.CashierUserId is int cashierUserId)
        {
            returns = returns.Where(salesReturn => salesReturn.UserId == cashierUserId);
        }

        List<ReturnLineAmountRow> rows = new List<ReturnLineAmountRow>();

        List<SalesReturn> loaded = await returns.OrderBy(salesReturn => salesReturn.Id).ToListAsync(cancellationToken);

        foreach (SalesReturn salesReturn in loaded)
        {
            // The tax of a return is shared across its lines in proportion to what each line gave
            // back, so the lines add up to the refund and to the tax exactly.
            List<Money> refunds = salesReturn.Lines
                .Select(line => line.RefundAmount)
                .ToList();

            IReadOnlyList<Money> taxes = ReportRules.Split(salesReturn.RefundTax, refunds);

            int index = 0;

            foreach (SalesReturnLine line in salesReturn.Lines)
            {
                Money netRefund = refunds[index] - taxes[index];

                rows.Add(new ReturnLineAmountRow
                {
                    ProductId = line.ProductId,
                    Sku = line.SalesInvoiceLine?.SkuSnapshot ?? string.Empty,
                    Name = line.SalesInvoiceLine?.NameSnapshot ?? string.Empty,
                    NameAr = line.Product?.NameAr,
                    RefundAmount = refunds[index],
                    NetRefund = netRefund,
                    OriginalUnitCost = line.OriginalUnitCost,
                    QuantityScaled = line.QuantityScaled,
                    QuantityScale = line.QuantityScale,
                    Reason = ReasonOf(line, salesReturn),
                });

                index++;
            }
        }

        if (filter.CategoryId is int categoryId)
        {
            List<int> productIds = await context.Products
                .AsNoTracking()
                .Where(product => product.CategoryId == categoryId)
                .Select(product => product.Id)
                .ToListAsync(cancellationToken);

            rows = rows.Where(row => productIds.Contains(row.ProductId)).ToList();
        }

        return rows;
    }

    /// <summary>Replays the stock ledger to a day and reads the positions it leaves behind.</summary>
    private static async Task<IReadOnlyDictionary<int, Position>> ReadPositionsAsync(
        StoreFlowDbContext context,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        List<StockMovement> movements = await context.StockMovements
            .AsNoTracking()
            .Where(movement => movement.BusinessDate <= asOf)
            .OrderBy(movement => movement.Id)
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<int, ReplayedPosition> replayed =
            InventoryLedgerReplay.Replay(movements);

        var positions = new Dictionary<int, Position>();

        foreach (KeyValuePair<int, ReplayedPosition> entry in replayed)
        {
            positions[entry.Key] = new Position(entry.Value.Quantity, entry.Value.AverageMinor, entry.Value.Scale);
        }

        return positions;
    }

    /// <summary>Reads the products a stock report may show.</summary>
    private static async Task<List<ProductFacts>> ReadProductFactsAsync(
        StoreFlowDbContext context,
        ReportFilter filter,
        CancellationToken cancellationToken)
    {
        IQueryable<Product> products = context.Products.AsNoTracking();

        string? search = filter.Search?.Trim();

        if (string.IsNullOrEmpty(search))
        {
            products = products.Where(product => product.IsActive);
        }
        else
        {
            string pattern = $"%{search}%";

            products = products.Where(product =>
                EF.Functions.Like(product.Name, pattern) ||
                (product.NameAr != null && EF.Functions.Like(product.NameAr, pattern)) ||
                EF.Functions.Like(product.Sku, pattern));
        }

        if (filter.CategoryId is int categoryId)
        {
            products = products.Where(product => product.CategoryId == categoryId);
        }

        if (filter.ProductId is int productId)
        {
            products = products.Where(product => product.Id == productId);
        }

        return await products
            .OrderBy(product => product.Name)
            .Select(product => new ProductFacts
            {
                ProductId = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                NameAr = product.NameAr,
                Category = product.Category == null ? string.Empty : product.Category.Name,
                Precision = product.Unit == null ? 0 : product.Unit.Precision,
                ReorderLevelScaled = product.ReorderLevelScaled,
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>The money of one sale, as the reports read it.</summary>
    private sealed class SaleAmountRow
    {
        public DateOnly Date { get; set; }

        public int UserId { get; set; }

        public string CashierName { get; set; } = string.Empty;

        public Money Total { get; set; }

        public Money Discount { get; set; }

        public Money Tax { get; set; }

        public bool IsReversal { get; set; }
    }

    /// <summary>The money of one return, as the reports read it.</summary>
    private sealed class ReturnAmountRow
    {
        public DateOnly Date { get; set; }

        public Money RefundTotal { get; set; }

        public Money RefundTax { get; set; }
    }

    /// <summary>One line of a sale, as the reports read it.</summary>
    private sealed class LineAmountRow
    {
        public int ProductId { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public Money Total { get; set; }

        public Money Discount { get; set; }

        public Money InvoiceDiscountShare { get; set; }

        public Money Tax { get; set; }

        public Money UnitCost { get; set; }

        public long QuantityScaled { get; set; }

        public int QuantityScale { get; set; }

        public bool IsReversal { get; set; }
    }

    /// <summary>One line of a return, as the reports read it.</summary>
    private sealed class ReturnLineAmountRow
    {
        public int ProductId { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? NameAr { get; set; }

        public Money RefundAmount { get; set; }

        public Money NetRefund { get; set; }

        public Money OriginalUnitCost { get; set; }

        public long QuantityScaled { get; set; }

        public int QuantityScale { get; set; }

        public string? Reason { get; set; }
    }

    /// <summary>One purchase document, as the reports read it.</summary>
    private sealed class PurchaseAmountRow
    {
        public int SupplierId { get; set; }

        public string Supplier { get; set; } = string.Empty;

        public Money Subtotal { get; set; }

        public Money Discount { get; set; }

        public Money Tax { get; set; }

        public Money Total { get; set; }

        public bool IsReversal { get; set; }
    }

    /// <summary>What the reports need to know about a product.</summary>
    private sealed class ProductFacts
    {
        public int ProductId { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? NameAr { get; set; }

        public string Category { get; set; } = string.Empty;

        public int Precision { get; set; }

        public long ReorderLevelScaled { get; set; }
    }

    /// <summary>A product's position on the shelf.</summary>
    private readonly record struct Position(long Quantity, long AverageMinor, int Scale);

    /// <summary>What one day added up to.</summary>
    private sealed class DayAccumulator
    {
        public int InvoiceCount { get; set; }

        public Money GrossSales { get; set; }

        public Money Discounts { get; set; }

        public Money Tax { get; set; }

        public Money NetSales { get; set; }

        public Money Returns { get; set; }

        public Money ReversedGross { get; set; }

        public Money ReversedDiscounts { get; set; }

        public Money ReversedTax { get; set; }

        public Money ReversedNet { get; set; }
    }

    /// <summary>What one product added up to.</summary>
    private sealed class ProductAccumulator
    {
        public ProductAccumulator(int productId, string sku, string name, string? nameAr)
        {
            ProductId = productId;
            Sku = sku;
            Name = name;
            NameAr = nameAr;
        }

        public int ProductId { get; }

        public string Sku { get; }

        public string Name { get; }

        public string? NameAr { get; }

        public decimal Quantity { get; set; }

        public Money NetRevenue { get; set; }

        public Money CostOfGoodsSold { get; set; }
    }

    /// <summary>What one supplier added up to.</summary>
    private sealed class SupplierAccumulator
    {
        public SupplierAccumulator(int supplierId, string supplier)
        {
            SupplierId = supplierId;
            Supplier = supplier;
        }

        public int SupplierId { get; }

        public string Supplier { get; }

        public int DocumentCount { get; set; }

        public Money Subtotal { get; set; }

        public Money Discount { get; set; }

        public Money Tax { get; set; }

        public Money Total { get; set; }
    }

    /// <summary>What came back, product by product.</summary>
    private sealed class ReturnsAccumulator
    {
        public ReturnsAccumulator(int productId, string sku, string name)
        {
            ProductId = productId;
            Sku = sku;
            Name = name;
        }

        public int ProductId { get; }

        public string Sku { get; }

        public string Name { get; }

        public int Count { get; set; }

        public decimal Quantity { get; set; }

        public Money Amount { get; set; }

        public string Reasons { get; set; } = string.Empty;
    }

    /// <summary>One movement of cash in or out of the drawer.</summary>
    private sealed class CashMovementRow
    {
        public DateOnly Date { get; set; }

        public int UserId { get; set; }

        public string CashierName { get; set; } = string.Empty;

        public Money Amount { get; set; }

        public bool IsRefund { get; set; }
    }

    /// <summary>What one cashier's day of cash added up to.</summary>
    private sealed class ShiftAccumulator
    {
        public ShiftAccumulator(DateOnly date, int userId, string cashierName)
        {
            Date = date;
            UserId = userId;
            CashierName = cashierName;
        }

        public DateOnly Date { get; }

        public int UserId { get; }

        public string CashierName { get; }

        public Money CashTaken { get; set; }

        public Money CashRefunded { get; set; }
    }

    /// <summary>What one cashier took.</summary>
    private sealed class CashierAccumulator    {
        public CashierAccumulator(int userId, string displayName)
        {
            UserId = userId;
            DisplayName = displayName;
        }

        public int UserId { get; }

        public string DisplayName { get; }

        public int InvoiceCount { get; set; }

        public Money NetSales { get; set; }
    }
}
