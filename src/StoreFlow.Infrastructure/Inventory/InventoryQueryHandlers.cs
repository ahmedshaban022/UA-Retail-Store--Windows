using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Inventory;

/// <summary>
/// Reads the purchase register (PRD PUR-05).
/// </summary>
public sealed class ListPurchasesQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) : IQueryHandler<ListPurchasesQuery, Result<IReadOnlyList<PurchaseSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PurchaseSummary>>> HandleAsync(
        ListPurchasesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.InventoryView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<PurchaseSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<PurchaseDocument> documents = context.PurchaseDocuments.AsNoTracking();

        PurchaseFilter filter = query.Filter;

        if (filter.SupplierId is int supplierId)
        {
            documents = documents.Where(document => document.SupplierId == supplierId);
        }

        if (filter.From is DateOnly from)
        {
            documents = documents.Where(document => document.BusinessDate >= from);
        }

        if (filter.To is DateOnly to)
        {
            documents = documents.Where(document => document.BusinessDate <= to);
        }

        if (filter.Status is DocumentStatus status)
        {
            documents = documents.Where(document => document.Status == status);
        }

        string? search = filter.Search?.Trim();

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = InventorySearch.LikePattern(search);

            documents = documents.Where(document =>
                EF.Functions.Like(document.Number, pattern, InventorySearch.LikeEscapeCharacter)
                || (document.Supplier != null
                    && EF.Functions.Like(document.Supplier.Name, pattern, InventorySearch.LikeEscapeCharacter)));
        }

        List<PurchaseDocument> rows = await documents
            .Include(document => document.Supplier)
            .Include(document => document.Lines)
            .OrderByDescending(document => document.BusinessDate)
            .ThenByDescending(document => document.Id)
            .Take(Math.Clamp(query.Limit, 1, 5000))
            .ToListAsync(cancellationToken);

        var summaries = new List<PurchaseSummary>();

        foreach (PurchaseDocument document in rows)
        {
            decimal totalQuantity = 0m;

            foreach (PurchaseLine line in document.Lines)
            {
                totalQuantity += line.QuantityScaled / (decimal)Math.Pow(10, line.QuantityScale);
            }

            summaries.Add(new PurchaseSummary(
                document.Id,
                document.Number,
                document.BusinessDate,
                document.SupplierId,
                document.Supplier?.Name,
                document.Status,
                document.Lines.Count,
                totalQuantity,
                document.Total,
                document.PostedUtc,
                document.ReversesDocumentId));
        }

        return Result<IReadOnlyList<PurchaseSummary>>.Success(summaries);
    }
}

/// <summary>Reads one purchase document with its lines.</summary>
public sealed class GetPurchaseQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) : IQueryHandler<GetPurchaseQuery, Result<PurchaseDetails>>
{
    /// <inheritdoc />
    public async Task<Result<PurchaseDetails>> HandleAsync(
        GetPurchaseQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.InventoryView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<PurchaseDetails>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        PurchaseDocument? document = await context.PurchaseDocuments
            .AsNoTracking()
            .Include(candidate => candidate.Supplier)
            .Include(candidate => candidate.Lines)
            .ThenInclude(line => line.Product)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.PurchaseId, cancellationToken);

        if (document is null)
        {
            return Result<PurchaseDetails>.Failure(
                InventoryErrorCodes.NotFound,
                "This purchase document no longer exists.");
        }

        var lines = document.Lines
            .OrderBy(line => line.Id)
            .Select(line => new PurchaseLineView(
                line.Id,
                line.ProductId,
                line.Product?.Sku ?? string.Empty,
                line.Product?.Name ?? string.Empty,
                line.QuantityScaled / (decimal)Math.Pow(10, line.QuantityScale),
                line.QuantityScale,
                line.UnitCost,
                line.Tax,
                line.Total,
                line.Notes))
            .ToList();

        return Result<PurchaseDetails>.Success(new PurchaseDetails(
            document.Id,
            document.Number,
            document.BusinessDate,
            document.SupplierId,
            document.Supplier?.Name,
            document.Status,
            document.Subtotal,
            document.Discount,
            document.Tax,
            document.Total,
            document.PostedUtc,
            document.Notes,
            document.ReversesDocumentId,
            lines));
    }
}

/// <summary>
/// Reads the stock balances (PRD INV-01). A product that has never been received still shows up,
/// with a quantity of zero.
/// </summary>
public sealed class ListStockBalancesQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) : IQueryHandler<ListStockBalancesQuery, Result<IReadOnlyList<StockBalanceSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StockBalanceSummary>>> HandleAsync(
        ListStockBalancesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.InventoryView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<StockBalanceSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<Product> products = context.Products.AsNoTracking();

        if (!query.IncludeInactive)
        {
            products = products.Where(product => product.IsActive);
        }

        string? search = query.Search?.Trim();

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = InventorySearch.LikePattern(search);

            products = products.Where(product =>
                EF.Functions.Like(product.Name, pattern, InventorySearch.LikeEscapeCharacter)
                || (product.NameAr != null
                    && EF.Functions.Like(product.NameAr, pattern, InventorySearch.LikeEscapeCharacter))
                || EF.Functions.Like(product.Sku, pattern, InventorySearch.LikeEscapeCharacter));
        }

        List<ProductRow> rows = await products
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Select(product => new ProductRow(
                product.Id,
                product.Sku,
                product.Name,
                product.NameAr,
                product.Unit == null ? string.Empty : product.Unit.Name,
                product.Unit == null ? 0 : product.Unit.Precision,
                product.ReorderLevelScaled,
                product.IsActive))
            .ToListAsync(cancellationToken);

        Dictionary<int, StockBalance> balances = await context.StockBalances
            .AsNoTracking()
            .Where(balance => products.Any(product => product.Id == balance.ProductId))
            .ToDictionaryAsync(balance => balance.ProductId, cancellationToken);

        var summaries = new List<StockBalanceSummary>();

        foreach (ProductRow row in rows)
        {
            balances.TryGetValue(row.Id, out StockBalance? balance);

            long onHand = balance?.QuantityOnHandScaled ?? 0;
            long damaged = balance?.DamagedQuantityScaled ?? 0;
            Money average = balance?.AverageUnitCost ?? Money.Zero;

            // The balance is read in the scale it was written in, not in the product's current unit.
            // The two are the same for every product whose unit has not been changed, and when they
            // are not the same it is because the unit was changed before the shop's rule about that
            // was put in place — and the number in the books is still the number in the books.
            int scale = balance?.QuantityScale ?? row.Precision;

            decimal quantity = onHand / (decimal)Math.Pow(10, scale);
            decimal reorderLevel = row.ReorderLevelScaled / (decimal)Math.Pow(10, row.Precision);

            // The reorder level belongs to the product's current unit, so it is brought into the
            // balance's scale before the two are compared.
            long reorderLevelScaled = InventoryLedger.Rescale(row.ReorderLevelScaled, row.Precision, scale);

            bool belowReorder = reorderLevelScaled > 0 && onHand <= reorderLevelScaled;

            if (query.OnlyLowStock && !belowReorder)
            {
                continue;
            }

            summaries.Add(new StockBalanceSummary(
                row.Id,
                row.Sku,
                row.Name,
                row.NameAr,
                row.UnitName,
                row.Precision,
                quantity,
                damaged / (decimal)Math.Pow(10, scale),
                average,
                InventoryLedger.ValueOf(onHand, scale, average),
                reorderLevel,
                belowReorder,
                row.IsActive));
        }

        return Result<IReadOnlyList<StockBalanceSummary>>.Success(summaries);
    }
}

/// <summary>Reads the stock ledger (PRD INV-02).</summary>
public sealed class ListStockMovementsQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) : IQueryHandler<ListStockMovementsQuery, Result<IReadOnlyList<StockMovementSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StockMovementSummary>>> HandleAsync(
        ListStockMovementsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.InventoryView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<StockMovementSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<StockMovement> movements = context.StockMovements
            .AsNoTracking()
            .Include(movement => movement.Product);

        if (query.ProductId is int productId)
        {
            movements = movements.Where(movement => movement.ProductId == productId);
        }

        if (query.From is DateOnly from)
        {
            movements = movements.Where(movement => movement.BusinessDate >= from);
        }

        if (query.To is DateOnly to)
        {
            movements = movements.Where(movement => movement.BusinessDate <= to);
        }

        List<StockMovement> rows = await movements
            .OrderByDescending(movement => movement.Id)
            .Take(Math.Clamp(query.Limit, 1, 5000))
            .ToListAsync(cancellationToken);

        var summaries = new List<StockMovementSummary>(rows.Count);

        foreach (StockMovement movement in rows)
        {
            summaries.Add(new StockMovementSummary(
                movement.Id,
                movement.ProductId,
                movement.Product?.Sku ?? string.Empty,
                movement.Product?.Name ?? string.Empty,
                movement.MovementType,
                movement.Disposition,
                movement.BusinessDate,
                movement.QuantityDeltaScaled / (decimal)Math.Pow(10, movement.QuantityScale),
                movement.QuantityScale,
                movement.UnitCost,
                movement.RunningBalanceScaled / (decimal)Math.Pow(10, movement.QuantityScale),
                movement.Reason,
                movement.PostedUtc,
                movement.DocumentId));
        }

        return Result<IReadOnlyList<StockMovementSummary>>.Success(summaries);
    }
}

/// <summary>Reads the products at or below their reorder level (PRD INV-03).</summary>
public sealed class ListLowStockQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) : IQueryHandler<ListLowStockQuery, Result<IReadOnlyList<LowStockItem>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LowStockItem>>> HandleAsync(
        ListLowStockQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.InventoryView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<LowStockItem>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var items = new List<LowStockItem>();

        IQueryable<Product> filteredProducts = context.Products
            .AsNoTracking()
            .Where(product => product.IsActive && product.ReorderLevelScaled > 0);

        List<Product> products = await filteredProducts
            .Include(product => product.Unit)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
        {
            return Result<IReadOnlyList<LowStockItem>>.Success(items);
        }

        Dictionary<int, StockBalance> balances = await context.StockBalances
            .AsNoTracking()
            .Where(balance => filteredProducts.Any(product => product.Id == balance.ProductId))
            .ToDictionaryAsync(balance => balance.ProductId, cancellationToken);

        foreach (Product product in products)
        {
            balances.TryGetValue(product.Id, out StockBalance? balance);

            long onHand = balance?.QuantityOnHandScaled ?? 0;

            if (onHand > product.ReorderLevelScaled)
            {
                continue;
            }

            int precision = product.Unit?.Precision ?? 0;
            Money average = balance?.AverageUnitCost ?? Money.Zero;

            items.Add(new LowStockItem(
                product.Id,
                product.Sku,
                product.Name,
                product.NameAr,
                product.Unit?.Name ?? string.Empty,
                precision,
                onHand / (decimal)Math.Pow(10, precision),
                product.ReorderLevelScaled / (decimal)Math.Pow(10, precision),
                average,
                InventoryLedger.ValueOf(onHand, precision, average)));
        }

        return Result<IReadOnlyList<LowStockItem>>.Success(items);
    }
}

/// <summary>
/// Reads the inventory valuation at a report date (PRD INV-07). The value uses the moving weighted
/// average, the same policy the ledger is written with.
/// </summary>
public sealed class GetInventoryValuationQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IClock clock) : IQueryHandler<GetInventoryValuationQuery, Result<InventoryValuation>>
{
    /// <inheritdoc />
    public async Task<Result<InventoryValuation>> HandleAsync(
        GetInventoryValuationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.InventoryView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<InventoryValuation>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        DateOnly asOfDate = query.AsOfDate ?? clock.LocalToday;

        // The valuation is always replayed from the ledger. The cached balance is a cache; the
        // ledger is the record, and a report date that falls before a backdated movement has to
        // leave that movement out.
        List<StockMovement> movements = await context.StockMovements
            .AsNoTracking()
            .Where(movement => movement.BusinessDate <= asOfDate)
            .OrderBy(movement => movement.Id)
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<int, ReplayedPosition> positions =
            InventoryLedgerReplay.Replay(movements);

        Dictionary<int, int> scales = await context.Products
            .AsNoTracking()
            .Select(product => new { product.Id, Precision = product.Unit == null ? 0 : product.Unit.Precision })
            .ToDictionaryAsync(row => row.Id, row => row.Precision, cancellationToken);

        Dictionary<int, long> reorderLevels = await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive && product.ReorderLevelScaled > 0)
            .Select(product => new { product.Id, product.ReorderLevelScaled })
            .ToDictionaryAsync(row => row.Id, row => row.ReorderLevelScaled, cancellationToken);

        int productCount = 0;
        int lowStockCount = 0;
        decimal totalQuantity = 0m;
        decimal totalValue = 0m;

        foreach (KeyValuePair<int, ReplayedPosition> entry in positions)
        {
            long quantityScaled = entry.Value.Quantity;

            if (quantityScaled == 0)
            {
                continue;
            }

            productCount += 1;

            // The position is read in the scale the movements were written in, which is the scale the
            // balance carries and not necessarily the product's current unit.
            int precision = entry.Value.Scale;
            decimal quantity = quantityScaled / (decimal)Math.Pow(10, precision);
            Money average = Money.FromMinorUnits(entry.Value.AverageMinor);
            Money value = InventoryLedger.ValueOf(quantityScaled, precision, average);

            totalQuantity += quantity;
            totalValue += value.Amount;

            if (reorderLevels.TryGetValue(entry.Key, out long reorderLevel))
            {
                // The reorder level belongs to the product's current unit, so it is brought into the
                // position's scale before the two are compared.
                long reorderLevelScaled = InventoryLedger.Rescale(
                    reorderLevel,
                    scales.TryGetValue(entry.Key, out int productScale) ? productScale : precision,
                    precision);

                if (quantityScaled <= reorderLevelScaled)
                {
                    lowStockCount += 1;
                }
            }
        }

        return Result<InventoryValuation>.Success(new InventoryValuation(
            asOfDate,
            productCount,
            totalQuantity,
            new Money(decimal.Round(totalValue, 2, MidpointRounding.AwayFromZero)),
            lowStockCount));
    }
}

/// <summary>Reads the stock count sessions.</summary>
public sealed class ListStockCountsQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) : IQueryHandler<ListStockCountsQuery, Result<IReadOnlyList<StockCountSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StockCountSummary>>> HandleAsync(
        ListStockCountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.InventoryView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<StockCountSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<StockCount> counts = await context.StockCounts
            .AsNoTracking()
            .Include(count => count.Lines)
            .OrderByDescending(count => count.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var rows = new List<StockCountSummary>(counts.Count);

        foreach (StockCount count in counts)
        {
            decimal differenceQuantity = 0m;
            decimal differenceValue = 0m;

            foreach (StockCountLine line in count.Lines)
            {
                differenceQuantity += line.DifferenceScaled / (decimal)Math.Pow(10, line.QuantityScale);
                differenceValue += line.DifferenceValue.Amount;
            }

            rows.Add(new StockCountSummary(
                count.Id,
                count.Number,
                count.BusinessDate,
                count.Status,
                count.Lines.Count,
                differenceQuantity,
                new Money(decimal.Round(differenceValue, 2, MidpointRounding.AwayFromZero)),
                count.CreatedUtc,
                count.ApprovedUtc,
                count.Reason));
        }

        return Result<IReadOnlyList<StockCountSummary>>.Success(rows);
    }
}

/// <summary>Reads one stock count session with its lines.</summary>
public sealed class GetStockCountQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) : IQueryHandler<GetStockCountQuery, Result<StockCountDetails>>
{
    /// <inheritdoc />
    public async Task<Result<StockCountDetails>> HandleAsync(
        GetStockCountQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.InventoryView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<StockCountDetails>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        StockCount? count = await context.StockCounts
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .ThenInclude(line => line.Product)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.StockCountId, cancellationToken);

        if (count is null)
        {
            return Result<StockCountDetails>.Failure(
                InventoryErrorCodes.NotFound,
                "This stock count no longer exists.");
        }

        var lines = count.Lines
            .OrderBy(line => line.Id)
            .Select(line => new StockCountLineView(
                line.Id,
                line.ProductId,
                line.Product?.Sku ?? string.Empty,
                line.Product?.Name ?? string.Empty,
                line.ExpectedQuantityScaled / (decimal)Math.Pow(10, line.QuantityScale),
                line.CountedQuantityScaled / (decimal)Math.Pow(10, line.QuantityScale),
                line.DifferenceScaled / (decimal)Math.Pow(10, line.QuantityScale),
                line.QuantityScale,
                line.UnitCost,
                line.DifferenceValue,
                line.Reason))
            .ToList();

        return Result<StockCountDetails>.Success(new StockCountDetails(
            count.Id,
            count.Number,
            count.BusinessDate,
            count.Status,
            count.CreatedUtc,
            count.ApprovedUtc,
            count.Reason,
            lines));
    }
}

/// <summary>Reads the products and suppliers the inventory screens offer as pickers.</summary>
public sealed class GetInventoryReferenceDataQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization) : IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>>
{
    /// <inheritdoc />
    public async Task<Result<InventoryReferenceData>> HandleAsync(
        GetInventoryReferenceDataQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.InventoryView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<InventoryReferenceData>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // What the books say is on the shelf. A stock count is not blind: the counter can see the
        // recorded quantity, so the reference data carries it.
        Dictionary<int, long> balances = await context.StockBalances
            .AsNoTracking()
            .ToDictionaryAsync(balance => balance.ProductId, balance => balance.QuantityOnHandScaled, cancellationToken);

        List<InventoryReferenceProduct> products = (await context.Products
            .AsNoTracking()
            .Include(product => product.Unit)
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new InventoryReferenceProduct(
                product.Id,
                product.Sku,
                product.Name,
                product.NameAr,
                product.Unit == null ? string.Empty : product.Unit.Name,
                product.Unit == null ? 0 : product.Unit.Precision,
                product.CostPrice))
            .ToListAsync(cancellationToken))
            .Select(product => product with
            {
                QuantityOnHand = QuantityOnHand(product, balances),
            })
            .ToList();

        List<InventoryReferenceSupplier> suppliers = await context.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.IsActive)
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => new InventoryReferenceSupplier(supplier.Id, supplier.Name, supplier.NameAr))
            .ToListAsync(cancellationToken);

        return Result<InventoryReferenceData>.Success(new InventoryReferenceData(products, suppliers));
    }

    /// <summary>
    /// How much of the product the books say is on the shelf, in the selling unit's own scale.
    /// </summary>
    private static decimal QuantityOnHand(
        InventoryReferenceProduct product,
        IReadOnlyDictionary<int, long> balances)
    {
        if (!balances.TryGetValue(product.Id, out long scaled))
        {
            return 0m;
        }

        return scaled / (decimal)Math.Pow(10, product.QuantityScale);
    }
}

/// <summary>
/// The shared free-text matching used by the inventory lists.
/// </summary>
internal static class InventorySearch
{
    /// <summary>The character that escapes the wildcards inside a search pattern.</summary>
    public const string LikeEscapeCharacter = "\\";

    /// <summary>
    /// Wraps the search text in the wildcards SQLite expects.
    /// </summary>
    /// <param name="search">The text the owner typed.</param>
    /// <returns>The pattern.</returns>
    public static string LikePattern(string search)
    {
        string escaped = search
            .Replace($"{LikeEscapeCharacter}", $"{LikeEscapeCharacter}{LikeEscapeCharacter}", StringComparison.Ordinal)
            .Replace("%", $"{LikeEscapeCharacter}%", StringComparison.Ordinal)
            .Replace("_", $"{LikeEscapeCharacter}_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}

/// <summary>The product columns the balance list needs, read before the balances are joined.</summary>
internal sealed record ProductRow(
    int Id,
    string Sku,
    string Name,
    string? NameAr,
    string UnitName,
    int Precision,
    long ReorderLevelScaled,
    bool IsActive);
