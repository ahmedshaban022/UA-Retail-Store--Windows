using StoreFlow.Domain.Valuation;
using Shouldly;

namespace StoreFlow.Domain.Tests;

public sealed class MovingAverageCostTests
{
    [Fact]
    public void Receipt_recomputes_the_average_as_the_prd_example_shows()
    {
        // 10 pieces at 50, then 10 more at 70: the average becomes 60 per piece.
        long average = MovingAverageCost.AfterStockIn(
            quantityOnHandScaled: 10,
            currentAverageCostMinor: 5_000,
            quantityInScaled: 10,
            unitCostMinor: 7_000,
            quantityScale: 0);

        average.ShouldBe(6_000);
    }

    [Fact]
    public void Receipt_into_empty_stock_sets_the_average_to_the_incoming_cost()
    {
        long average = MovingAverageCost.AfterStockIn(0, 0, 5, 1_250, 0);

        average.ShouldBe(1_250);
    }

    [Fact]
    public void A_return_at_the_original_cost_restores_the_average_when_nothing_else_moved()
    {
        // 10 at 60, sell 3 at 60, then return those 3 at their original cost of 60.
        long afterSale = MovingAverageCost.AfterStockIn(10, 6_000, 0, 0, 0);
        afterSale.ShouldBe(6_000);

        long afterReturn = MovingAverageCost.AfterResellableReturn(
            quantityOnHandScaled: 7,
            currentAverageCostMinor: 6_000,
            quantityReturnedScaled: 3,
            originalUnitCostMinor: 6_000,
            quantityScale: 0);

        afterReturn.ShouldBe(6_000);
    }

    [Fact]
    public void A_return_after_an_intervening_receipt_produces_a_different_but_correct_average()
    {
        // 10 at 60, sell 3, receive 10 at 70, then return the 3 sold units at their original 60.
        long afterReceipt = MovingAverageCost.AfterStockIn(7, 6_000, 10, 7_000, 0);

        // (7 * 6000 + 10 * 7000) / 17 = 6588.23...
        afterReceipt.ShouldBe(6_588);

        long afterReturn = MovingAverageCost.AfterResellableReturn(14, 6_588, 3, 6_000, 0);

        // (14 * 6588 + 3 * 6000) / 17 = 6483.52...
        afterReturn.ShouldBe(6_484);
    }

    [Fact]
    public void Fractional_quantities_scale_correctly()
    {
        // 2.00 kg at 60.00, then 1.00 kg at 90.00: (2 * 6000 + 1 * 9000) / 3 = 7000.
        long average = MovingAverageCost.AfterStockIn(200, 6_000, 100, 9_000, 2);

        average.ShouldBe(7_000);
    }
}
