using StoreFlow.Domain.Money;
using StoreFlow.Domain.Quantity;

namespace StoreFlow.Domain.Valuation;

/// <summary>
/// Moving weighted average cost, the product's stock valuation policy (PRD §6.1).
/// </summary>
/// <remarks>
/// <para>The average is recomputed in <b>posting order</b> — the order of <c>StockMovements.Id</c> — and
/// never by business date, so backdating a document cannot silently rewrite historical costing.</para>
/// <para>A return is modelled as a <b>receipt at the original invoice line's cost snapshot</b>. Because a
/// receipt and a return-restock use the same formula, a return restores the pre-sale average exactly when
/// no other movement intervened, and produces a correctly different average when one did.</para>
/// </remarks>
public static class MovingAverageCost
{
    /// <summary>
    /// Recomputes the moving average after stock enters the shelf (a purchase receipt, an opening
    /// balance, or a resellable return at its original cost).
    /// </summary>
    /// <param name="quantityOnHandScaled">Current sellable quantity, scaled by 10^<paramref name="quantityScale"/>.</param>
    /// <param name="currentAverageCostMinor">Current average cost per base unit, in minor units.</param>
    /// <param name="quantityInScaled">Quantity entering stock, scaled by 10^<paramref name="quantityScale"/>.</param>
    /// <param name="unitCostMinor">Cost per base unit of the incoming stock, in minor units.</param>
    /// <param name="quantityScale">Precision of the selling unit.</param>
    /// <returns>The new average cost per base unit, in minor units.</returns>
    public static long AfterStockIn(
        long quantityOnHandScaled,
        long currentAverageCostMinor,
        long quantityInScaled,
        long unitCostMinor,
        int quantityScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantityOnHandScaled);
        ArgumentOutOfRangeException.ThrowIfNegative(quantityInScaled);

        if (quantityInScaled == 0)
        {
            return currentAverageCostMinor;
        }

        var scale = QuantityScale.PowerOfTen(quantityScale);
        var quantityOnHand = quantityOnHandScaled / scale;
        var quantityIn = quantityInScaled / scale;
        var totalQuantity = quantityOnHand + quantityIn;

        if (totalQuantity <= 0m)
        {
            // Nothing was on hand: the average simply becomes the incoming cost.
            return unitCostMinor;
        }

        var totalValue = (quantityOnHand * currentAverageCostMinor) + (quantityIn * unitCostMinor);

        return MoneyMath.RoundToWholeMinorUnits(totalValue / totalQuantity);
    }

    /// <summary>
    /// Recomputes the moving average after a resellable return is put back on the shelf. Identical to
    /// <see cref="AfterStockIn"/> by construction: a return is a receipt at the original cost.
    /// </summary>
    public static long AfterResellableReturn(
        long quantityOnHandScaled,
        long currentAverageCostMinor,
        long quantityReturnedScaled,
        long originalUnitCostMinor,
        int quantityScale) =>
        AfterStockIn(
            quantityOnHandScaled,
            currentAverageCostMinor,
            quantityReturnedScaled,
            originalUnitCostMinor,
            quantityScale);

    /// <summary>
    /// The cost of goods sold per base unit for a sale, taken from the current moving average and
    /// snapshotted onto the invoice line.
    /// </summary>
    public static long CostOfGoodsSoldPerUnit(long currentAverageCostMinor, long quantitySoldScaled) =>
        quantitySoldScaled == 0 ? 0L : currentAverageCostMinor;
}
