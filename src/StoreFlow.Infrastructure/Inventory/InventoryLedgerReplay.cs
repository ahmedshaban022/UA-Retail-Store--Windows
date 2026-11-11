using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Valuation;

namespace StoreFlow.Infrastructure.Inventory;

/// <summary>
/// A product's position after the ledger has been replayed.
/// </summary>
/// <param name="Quantity">The sellable quantity, scaled by <paramref name="Scale"/>.</param>
/// <param name="AverageMinor">The moving average cost, in minor units.</param>
/// <param name="Scale">The precision the quantity is scaled by.</param>
internal readonly record struct ReplayedPosition(long Quantity, long AverageMinor, int Scale);

/// <summary>
/// Replays the stock ledger to a point in time. The replay follows exactly the same rules as the
/// ledger writer, so a replayed position always equals the cached balance for the same moment
/// (PRD §5.4 invariant 1, ADR-0003).
/// </summary>
internal static class InventoryLedgerReplay
{
    /// <summary>
    /// The sellable quantity and moving average per product after the given movements.
    /// </summary>
    /// <remarks>
    /// The scale is the one the ledger writer would have left on the balance: the scale of the
    /// movement just posted. A product whose unit was changed while its stock was moving keeps the
    /// scale of its last movement, exactly as the balance does, so the two always agree.
    /// </remarks>
    /// <param name="movements">The movements, in posting order.</param>
    /// <returns>The position per product.</returns>
    public static IReadOnlyDictionary<int, ReplayedPosition> Replay(IEnumerable<StockMovement> movements)
    {
        var positions = new Dictionary<int, ReplayedPosition>();

        foreach (StockMovement movement in movements.OrderBy(candidate => candidate.Id))
        {
            ReplayedPosition current = positions.TryGetValue(movement.ProductId, out var position)
                ? position
                : default;

            if (movement.Disposition == StockDisposition.Damaged)
            {
                // A damaged movement never touches the sellable quantity or the average.
                positions[movement.ProductId] = current;
                continue;
            }

            long quantity = current.Quantity + movement.QuantityDeltaScaled;
            long average = current.AverageMinor;

            if (movement.QuantityDeltaScaled > 0)
            {
                average = MovingAverageCost.AfterStockIn(
                    current.Quantity,
                    current.AverageMinor,
                    movement.QuantityDeltaScaled,
                    movement.UnitCost.ToMinorUnits(),
                    movement.QuantityScale);
            }

            positions[movement.ProductId] = new ReplayedPosition(quantity, average, movement.QuantityScale);
        }

        return positions;
    }
}
