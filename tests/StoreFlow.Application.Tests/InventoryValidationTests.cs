using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Money;
using Shouldly;

namespace StoreFlow.Application.Tests;

/// <summary>
/// The inventory rules, tested on their own: the editor and the command handlers both use them, so
/// they have to agree on what a valid purchase line, adjustment or count looks like.
/// </summary>
public sealed class InventoryValidationTests
{
    [Fact]
    public void A_purchase_line_needs_a_quantity_above_zero()
    {
        InventoryValidation.ValidatePurchaseLine(0m, new Money(10m), Money.Zero, 0)
            .ShouldContain(issue => issue.Code == InventoryValidation.NonPositiveQuantityCode);

        InventoryValidation.ValidatePurchaseLine(-1m, new Money(10m), Money.Zero, 0)
            .ShouldContain(issue => issue.Code == InventoryValidation.NonPositiveQuantityCode);
    }

    [Fact]
    public void A_purchase_line_refuses_a_quantity_the_unit_cannot_express()
    {
        InventoryValidation.ValidatePurchaseLine(1.5m, new Money(10m), Money.Zero, 0)
            .ShouldContain(issue => issue.Code == InventoryValidation.QuantityTooPreciseCode);

        InventoryValidation.ValidatePurchaseLine(1.5m, new Money(10m), Money.Zero, 1)
            .ShouldBeEmpty();
    }

    [Fact]
    public void A_purchase_line_refuses_a_negative_cost_or_tax()
    {
        InventoryValidation.ValidatePurchaseLine(1m, new Money(-1m), Money.Zero, 0)
            .ShouldContain(issue => issue.Code == InventoryValidation.NegativePriceCode);

        InventoryValidation.ValidatePurchaseLine(1m, new Money(10m), new Money(-1m), 0)
            .ShouldContain(issue => issue.Code == InventoryValidation.NegativePriceCode);
    }

    [Fact]
    public void A_document_needs_at_least_one_line()
    {
        InventoryValidation.ValidatePurchase([])
            .ShouldContain(issue => issue.Code == InventoryValidation.NoLinesCode);
    }

    [Fact]
    public void A_document_refuses_the_same_product_twice()
    {
        IReadOnlyList<InventoryValidationIssue> issues = InventoryValidation.ValidatePurchase(
        [
            new PurchaseLineInput(1, 1m, new Money(10m), Money.Zero),
            new PurchaseLineInput(1, 2m, new Money(10m), Money.Zero),
        ]);

        issues.ShouldContain(issue => issue.Code == InventoryValidation.DuplicateProductCode);
    }

    [Fact]
    public void An_adjustment_needs_a_reason()
    {
        InventoryValidation.ValidateAdjustment(1m, new Money(10m), "  ")
            .ShouldContain(issue => issue.Code == InventoryValidation.RequiredCode);

        InventoryValidation.ValidateAdjustment(1m, new Money(10m), "Broken")
            .ShouldBeEmpty();
    }

    [Fact]
    public void An_adjustment_needs_a_quantity_that_is_not_zero()
    {
        InventoryValidation.ValidateAdjustment(0m, new Money(10m), "Broken")
            .ShouldContain(issue => issue.Code == InventoryValidation.NonPositiveQuantityCode);
    }

    [Fact]
    public void A_counted_quantity_cannot_be_negative()
    {
        InventoryValidation.ValidateCountLine(-1m, 0)
            .ShouldContain(issue => issue.Code == InventoryValidation.NegativeQuantityCode);

        InventoryValidation.ValidateCountLine(0m, 0).ShouldBeEmpty();
    }

    [Fact]
    public void An_opening_quantity_needs_to_be_above_zero()
    {
        InventoryValidation.ValidateOpeningStock(0m, new Money(10m), 0)
            .ShouldContain(issue => issue.Code == InventoryValidation.NonPositiveQuantityCode);

        InventoryValidation.ValidateOpeningStock(5m, new Money(10m), 0).ShouldBeEmpty();
    }

    [Fact]
    public void A_large_adjustment_needs_a_second_approver()
    {
        InventoryRules.RequiresApproval(200m, new Money(10m)).ShouldBeTrue();
        InventoryRules.RequiresApproval(-200m, new Money(10m)).ShouldBeTrue();

        // A small quantity of an expensive item is still a large adjustment.
        InventoryRules.RequiresApproval(1m, new Money(5000m)).ShouldBeTrue();

        InventoryRules.RequiresApproval(2m, new Money(20m)).ShouldBeFalse();
    }

    [Fact]
    public void A_document_number_carries_its_year_and_a_sequence()
    {
        InventoryRules.BuildNumber("PUR", new DateOnly(2026, 3, 4), 7).ShouldBe("PUR-2026-0007");
        InventoryRules.BuildNumber("CNT", new DateOnly(2025, 12, 31), 1).ShouldBe("CNT-2025-0001");
    }

    [Fact]
    public void A_quantity_that_fits_the_unit_is_recognised()
    {
        InventoryValidation.FitsScale(1.25m, 2).ShouldBeTrue();
        InventoryValidation.FitsScale(1.256m, 2).ShouldBeFalse();
        InventoryValidation.FitsScale(2m, 0).ShouldBeTrue();
        InventoryValidation.FitsScale(2.5m, 0).ShouldBeFalse();
    }
}
