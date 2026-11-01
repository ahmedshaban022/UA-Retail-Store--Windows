using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Pricing;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Pos;

/// <summary>
/// The reads the till needs: the product a barcode names, the products a search finds, and the
/// suspended carts waiting to be picked up.
/// </summary>
public sealed class LookupProductByBarcodeQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<LookupProductByBarcodeQuery, Result<PosProductFacts>>
{
    /// <inheritdoc />
    public async Task<Result<PosProductFacts>> HandleAsync(
        LookupProductByBarcodeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesPost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<PosProductFacts>.Failure(permitted.Error);
        }

        string barcode = query.Barcode?.Trim() ?? string.Empty;

        if (barcode.Length == 0)
        {
            return Result<PosProductFacts>.Failure(
                PosErrorCodes.ProductNotFound,
                "Nothing was scanned.");
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Product? product = await context.Products
            .Include(candidate => candidate.Unit)
            .Include(candidate => candidate.TaxCategory)
            .Include(candidate => candidate.Barcodes)
            .FirstOrDefaultAsync(
                candidate => candidate.Barcodes.Any(code => code.Barcode == barcode)
                    || candidate.Barcode == barcode,
                cancellationToken);

        if (product is null)
        {
            return Result<PosProductFacts>.Failure(
                PosErrorCodes.ProductNotFound,
                "That barcode is not in the catalogue.");
        }

        Dictionary<int, PosProductFacts> facts = await PosProductReader.LoadAsync(
            context, [product.Id], cancellationToken);

        return facts.TryGetValue(product.Id, out PosProductFacts? found)
            ? Result<PosProductFacts>.Success(found)
            : Result<PosProductFacts>.Failure(
                PosErrorCodes.ProductNotFound,
                "That barcode is not in the catalogue.");
    }
}

/// <summary>Finds the products a search term matches, so the cashier can add one by hand.</summary>
public sealed class SearchSellableProductsQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<SearchSellableProductsQuery, Result<IReadOnlyList<PosProductFacts>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PosProductFacts>>> HandleAsync(
        SearchSellableProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesPost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<PosProductFacts>>.Failure(permitted.Error);
        }

        string term = query.Search?.Trim() ?? string.Empty;

        if (term.Length == 0)
        {
            return Result<IReadOnlyList<PosProductFacts>>.Success([]);
        }

        int limit = Math.Clamp(query.Limit, 1, 100);

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<int> ids = await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .Where(product =>
                product.Name.Contains(term)
                || (product.NameAr != null && product.NameAr.Contains(term))
                || product.Sku.Contains(term)
                || product.Barcodes.Any(code => code.Barcode.Contains(term)))
            .OrderBy(product => product.Sku)
            .Select(product => product.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        Dictionary<int, PosProductFacts> facts = await PosProductReader.LoadAsync(
            context, ids, cancellationToken);

        var ordered = ids.Where(facts.ContainsKey).Select(id => facts[id]).ToList();

        return Result<IReadOnlyList<PosProductFacts>>.Success(ordered);
    }
}

/// <summary>Lists the suspended carts.</summary>
public sealed class ListHeldCartsQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListHeldCartsQuery, Result<IReadOnlyList<HeldCartSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<HeldCartSummary>>> HandleAsync(
        ListHeldCartsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesPost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<HeldCartSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The carts are ordered in memory, because the database cannot sort a moment in time: it
        // stores it as text and refuses to order by it. There are only a handful of carts waiting
        // at a time, so the whole list is read and then put in order.
        List<HeldCart> carts = await context.HeldCarts
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var summaries = carts
            .OrderBy(cart => cart.HeldUtc)
            .ThenBy(cart => cart.Number, StringComparer.Ordinal)
            .Select(cart =>
            {
                HeldCartStorage storage = HeldCartStorageCodec.Decode(cart.ContentJson);
                (IReadOnlyList<SaleLineInput> lines, _) = HeldCartStorageCodec.ToTill(storage);

                return HoldCartCommandHandler.Summarise(cart, lines);
            })
            .ToList();

        return Result<IReadOnlyList<HeldCartSummary>>.Success(summaries);
    }
}

/// <summary>Reads one suspended cart, so the till can put it back together.</summary>
public sealed class GetHeldCartQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<GetHeldCartQuery, Result<HeldCartDetails>>
{
    /// <inheritdoc />
    public async Task<Result<HeldCartDetails>> HandleAsync(
        GetHeldCartQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.SalesPost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<HeldCartDetails>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        HeldCart? cart = await context.HeldCarts
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.HeldCartId, cancellationToken);

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

        return Result<HeldCartDetails>.Success(new HeldCartDetails(summary, lines, invoiceDiscount));
    }
}
