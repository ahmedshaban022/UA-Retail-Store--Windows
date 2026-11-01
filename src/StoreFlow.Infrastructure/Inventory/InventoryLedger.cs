using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Valuation;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Inventory;

/// <summary>
/// The single place a stock movement is written. Every entry to the ledger and every change to the
/// cached balance goes through here, so the ledger and the balance cannot drift apart inside a
/// transaction (PRD §5.4 invariant 1).
/// </summary>
/// <remarks>
/// The moving average is recomputed in posting order and never by business date, so backdating a
/// document cannot rewrite historical costing (ADR-0003).
/// </remarks>
internal static class InventoryLedger
{
    /// <summary>The error code when the movement would take the sellable quantity below zero.</summary>
    public const string NegativeStockCode = InventoryErrorCodes.NegativeStock;

    /// <summary>The error code when the movement would take the damaged quantity below zero.</summary>
    public const string NegativeDamagedCode = InventoryErrorCodes.NegativeDamaged;

    /// <summary>
    /// Applies one movement: writes the ledger entry and updates the cached balance together.
    /// </summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="product">The product the movement is for.</param>
    /// <param name="quantityScale">The precision of the product's selling unit.</param>
    /// <param name="movementType">The kind of movement.</param>
    /// <param name="disposition">Whether the movement affects the sellable quantity or the damaged bucket.</param>
    /// <param name="quantityDeltaScaled">The signed quantity change, scaled by the unit's precision.</param>
    /// <param name="unitCost">The cost per unit the movement carries.</param>
    /// <param name="businessDate">The user-facing document date.</param>
    /// <param name="reason">The reason, required for adjustments and counts.</param>
    /// <param name="userId">The actor.</param>
    /// <param name="documentId">The source document, when there is one.</param>
    /// <param name="documentLineId">The source document line, when there is one.</param>
    /// <param name="now">The posting timestamp.</param>
    /// <returns>The written movement, or the reason it could not be written.</returns>
    public static async Task<(StockMovement? Movement, string? ErrorCode, string? ErrorMessage)> ApplyAsync(
        StoreFlowDbContext context,
        Product product,
        int quantityScale,
        StockMovementType movementType,
        StockDisposition disposition,
        long quantityDeltaScaled,
        Money unitCost,
        DateOnly businessDate,
        string? reason,
        int? userId,
        int? documentId,
        int? documentLineId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(product);

        StockBalance? balance = await context.StockBalances
            .FirstOrDefaultAsync(candidate => candidate.ProductId == product.Id);

        if (balance is null)
        {
            balance = new StockBalance
            {
                ProductId = product.Id,
                QuantityOnHandScaled = 0,
                DamagedQuantityScaled = 0,
                AverageUnitCost = Money.Zero,
                QuantityScale = quantityScale,
                Version = 0,
            };

            context.StockBalances.Add(balance);
        }

        long sellable = balance.QuantityOnHandScaled;
        long damaged = balance.DamagedQuantityScaled;
        Money average = balance.AverageUnitCost;

        if (disposition == StockDisposition.Damaged)
        {
            long updatedDamaged = damaged + quantityDeltaScaled;

            if (updatedDamaged < 0)
            {
                return (null, NegativeDamagedCode, "The damaged quantity cannot go below zero.");
            }

            damaged = updatedDamaged;
        }
        else
        {
            long updatedSellable = sellable + quantityDeltaScaled;

            if (updatedSellable < 0)
            {
                return (null, NegativeStockCode, "There is not enough stock for this movement.");
            }

            if (quantityDeltaScaled > 0)
            {
                average = Money.FromMinorUnits(MovingAverageCost.AfterStockIn(
                    sellable,
                    average.ToMinorUnits(),
                    quantityDeltaScaled,
                    unitCost.ToMinorUnits(),
                    quantityScale));
            }

            sellable = updatedSellable;
        }

        var movement = new StockMovement
        {
            ProductId = product.Id,
            MovementType = movementType,
            Disposition = disposition,
            DocumentId = documentId,
            DocumentLineId = documentLineId,
            QuantityDeltaScaled = quantityDeltaScaled,
            QuantityScale = quantityScale,
            UnitCost = unitCost,
            RunningBalanceScaled = sellable,
            Reason = reason,
            UserId = userId,
            PostedUtc = now,
            BusinessDate = businessDate,
        };

        context.StockMovements.Add(movement);

        balance.QuantityOnHandScaled = sellable;
        balance.DamagedQuantityScaled = damaged;
        balance.AverageUnitCost = average;
        balance.QuantityScale = quantityScale;
        balance.Version += 1;
        balance.UpdatedUtc = now;

        return (movement, null, null);
    }

    /// <summary>
    /// The value of a quantity at the given average cost.
    /// </summary>
    /// <param name="quantityScaled">The quantity, scaled by the unit's precision.</param>
    /// <param name="quantityScale">The precision of the unit.</param>
    /// <param name="averageUnitCost">The average cost per base unit.</param>
    /// <returns>The value of the quantity.</returns>
    public static Money ValueOf(long quantityScaled, int quantityScale, Money averageUnitCost)
    {
        decimal quantity = quantityScaled / (decimal)Math.Pow(10, quantityScale);

        return new Money(decimal.Round(quantity * averageUnitCost.Amount, 2, MidpointRounding.AwayFromZero));
    }
}
