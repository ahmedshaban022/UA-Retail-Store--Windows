using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Valuation;

namespace StoreFlow.Infrastructure.Inventory;

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
    /// <param name="movements">The movements, in posting order.</param>
    /// <returns>The position per product.</returns>
    public static IReadOnlyDictionary<int, (long Quantity, long AverageMinor)> Replay(
        IEnumerable<StockMovement> movements)
    {
        var positions = new Dictionary<int, (long Quantity, long AverageMinor)>();

        foreach (StockMovement movement in movements.OrderBy(candidate => candidate.Id))
        {
            (long Quantity, long AverageMinor) current = positions.TryGetValue(movement.ProductId, out var position)
                ? position
                : (0L, 0L);

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

            positions[movement.ProductId] = (quantity, average);
        }

        return positions;
    }
}
