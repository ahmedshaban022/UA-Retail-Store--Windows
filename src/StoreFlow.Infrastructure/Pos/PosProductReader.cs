using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Pos;

/// <summary>
/// Loads what the till needs to know about the products on the cart: their identity, their unit,
/// their price, their tax treatment and how much of them is on the shelf.
/// </summary>
internal static class PosProductReader
{
    /// <summary>Reads the products named by the cart.</summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="productIds">The products to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The facts about each product, by id. A product that does not exist is absent.</returns>
    public static async Task<Dictionary<int, PosProductFacts>> LoadAsync(
        StoreFlowDbContext context,
        IReadOnlyCollection<int> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return [];
        }

        List<Product> products = await context.Products
            .AsNoTracking()
            .Include(product => product.Unit)
            .Include(product => product.TaxCategory)
            .Include(product => product.Barcodes)
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        Dictionary<int, long> balances = await context.StockBalances
            .AsNoTracking()
            .Where(balance => productIds.Contains(balance.ProductId))
            .ToDictionaryAsync(balance => balance.ProductId, balance => balance.QuantityOnHandScaled, cancellationToken);

        var facts = new Dictionary<int, PosProductFacts>(products.Count);

        foreach (Product product in products)
        {
            balances.TryGetValue(product.Id, out long onHand);

            string? barcode = product.Barcodes.FirstOrDefault(candidate => candidate.IsPrimary)?.Barcode
                ?? product.Barcodes.FirstOrDefault()?.Barcode
                ?? product.Barcode;

            facts[product.Id] = new PosProductFacts(
                product.Id,
                product.Sku,
                product.Name,
                product.NameAr ?? string.Empty,
                barcode,
                product.Unit?.Name ?? string.Empty,
                product.Unit?.Precision ?? 0,
                product.SalePrice,
                product.TaxCategory?.RateBasisPoints ?? 0,
                product.TaxCategory?.PricesIncludeTax ?? false,
                product.IsActive,
                onHand);
        }

        return facts;
    }
}
