using StoreFlow.Domain.Pricing;
using StoreFlow.Domain.Tax;
using Shouldly;

namespace StoreFlow.Domain.Tests;

public sealed class TaxAndDiscountTests
{
    [Fact]
    public void Tax_on_an_exclusive_amount_is_added_on_top()
    {
        TaxCalculator.OnExclusiveAmount(100m, 1450).ShouldBe(14.50m);
    }

    [Fact]
    public void Tax_on_an_inclusive_amount_is_extracted_from_the_gross()
    {
        // 114.50 gross at 14.5 % is 100.00 net, so the tax is 14.50.
        TaxCalculator.OnInclusiveAmount(114.50m, 1450).ShouldBe(14.50m);
    }

    [Fact]
    public void Taxable_base_for_an_inclusive_price_excludes_the_tax()
    {
        TaxCalculator.TaxableBase(114.50m, 1450, priceIncludesTax: true).ShouldBe(100m);
    }

    [Fact]
    public void A_percentage_discount_is_computed_on_the_base_amount()
    {
        DiscountCalculator.DiscountAmount(200m, DiscountSpec.Percentage(10m)).ShouldBe(20m);
    }

    [Fact]
    public void A_fixed_discount_is_taken_off_the_base_amount()
    {
        DiscountCalculator.DiscountAmount(200m, DiscountSpec.FixedAmount(35m)).ShouldBe(35m);
    }

    [Fact]
    public void A_discount_can_never_make_the_amount_negative()
    {
        DiscountCalculator.Apply(50m, DiscountSpec.FixedAmount(80m)).ShouldBe(0m);
    }

    [Fact]
    public void The_effective_percentage_of_a_fixed_discount_is_reported()
    {
        DiscountCalculator.EffectivePercent(200m, DiscountSpec.FixedAmount(20m)).ShouldBe(10m);
    }

    [Fact]
    public void A_percentage_above_one_hundred_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DiscountSpec.Percentage(101m));
    }
}
