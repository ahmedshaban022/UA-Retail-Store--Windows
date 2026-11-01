using StoreFlow.Application.Sales;
using StoreFlow.Domain.Money;
using Shouldly;

namespace StoreFlow.Application.Tests;

/// <summary>
/// The rules of the sales register, tested on their own: the numbers, the refund arithmetic and the
/// sign the register shows.
/// </summary>
public sealed class SalesRulesTests
{
    [Fact]
    public void A_document_number_carries_its_family_its_year_and_its_sequence()
    {
        SalesRules.BuildNumber(SalesRules.ReturnNumberPrefix, new DateOnly(2026, 3, 9), 7)
            .ShouldBe("RET-2026-0007");

        SalesRules.BuildNumber(SalesRules.ReversalNumberPrefix, new DateOnly(2025, 12, 31), 12)
            .ShouldBe("REV-2025-0012");
    }

    [Fact]
    public void The_whole_line_comes_back_as_the_whole_line_was_paid()
    {
        // The exact case, with no rounding at all.
        SalesRules.RefundForQuantity(new Money(1350m), 3, 3).ShouldBe(new Money(1350m));

        // And a part of it, which is the same money pro rata.
        SalesRules.RefundForQuantity(new Money(1350m), 1, 3).ShouldBe(new Money(450m));
        SalesRules.RefundForQuantity(new Money(100m), 1, 4).ShouldBe(new Money(25m));
    }

    [Fact]
    public void A_refund_is_rounded_once_to_the_nearest_minor_unit()
    {
        // A third of 10.00 is 3.333..., which becomes 3.33.
        SalesRules.RefundForQuantity(new Money(10m), 1, 3).ShouldBe(new Money(3.33m));

        // Two thirds of 10.00 is 6.666..., which becomes 6.67.
        SalesRules.RefundForQuantity(new Money(10m), 2, 3).ShouldBe(new Money(6.67m));
    }

    [Fact]
    public void A_refund_of_nothing_is_nothing()
    {
        SalesRules.RefundForQuantity(new Money(100m), 0, 3).ShouldBe(Money.Zero);
        SalesRules.RefundForQuantity(new Money(100m), 1, 0).ShouldBe(Money.Zero);
    }

    [Fact]
    public void The_remainder_of_a_line_is_what_is_left_to_return()
    {
        SalesRules.RemainingQuantity(10, 3).ShouldBe(7);
        SalesRules.RemainingQuantity(10, 10).ShouldBe(0);
        SalesRules.RemainingQuantity(10, 12).ShouldBe(0);
    }

    [Fact]
    public void The_register_shows_a_sale_as_positive_and_a_correction_as_negative()
    {
        SalesRules.SignedAmount(SalesDocumentKind.Invoice, new Money(450m)).ShouldBe(new Money(450m));
        SalesRules.SignedAmount(SalesDocumentKind.Return, new Money(150m)).ShouldBe(new Money(-150m));
        SalesRules.SignedAmount(SalesDocumentKind.Reversal, new Money(450m)).ShouldBe(new Money(-450m));
    }

    [Fact]
    public void A_quantity_the_unit_cannot_express_does_not_fit()
    {
        SalesRules.FitsScale(2m, 0).ShouldBeTrue();
        SalesRules.FitsScale(2.5m, 1).ShouldBeTrue();
        SalesRules.FitsScale(2.55m, 1).ShouldBeFalse();
        SalesRules.FitsScale(0.001m, 3).ShouldBeTrue();
        SalesRules.FitsScale(0.0001m, 3).ShouldBeFalse();
    }

    [Fact]
    public void A_quantity_is_scaled_and_unscaled_without_losing_anything()
    {
        SalesRules.Scale(2.5m, 1).ShouldBe(25);
        SalesRules.Unscale(25, 1).ShouldBe(2.5m);
        SalesRules.Scale(3m, 0).ShouldBe(3);
        SalesRules.Unscale(3, 0).ShouldBe(3m);
    }
}
