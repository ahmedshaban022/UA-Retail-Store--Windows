using StoreFlow.Application.Pos;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using Shouldly;

namespace StoreFlow.Application.Tests;

/// <summary>
/// The cart builder, tested on its own: the till, the invoice and the ledger all carry the
/// numbers it produces, so the lines, the money and the problems have to agree with each other.
/// </summary>
public sealed class PosCartBuilderTests
{
    [Fact]
    public void An_empty_cart_is_refused()
    {
        PosCart cart = PosCartBuilder.Build([], Catalogue(), DiscountSpec.None);

        cart.IsValid.ShouldBeFalse();
        cart.Issues.ShouldContain(issue => issue.Code == PosErrorCodes.EmptyCart);
        cart.Issues[0].Code.ShouldBe("pos.empty_cart");
        cart.Lines.ShouldBeEmpty();
        cart.Totals.Total.ShouldBe(Money.Zero);
    }

    [Fact]
    public void A_line_with_a_quantity_of_zero_or_less_is_refused()
    {
        PosCart zero = PosCartBuilder.Build(
            [new SaleLineInput(1, 0m, new Money(100m), DiscountSpec.None)],
            Catalogue(Product(1)),
            DiscountSpec.None);

        zero.IsValid.ShouldBeFalse();
        zero.Issues.ShouldContain(issue => issue.Code == PosErrorCodes.InvalidQuantity);
        zero.Issues[0].Code.ShouldBe("pos.invalid_quantity");
        zero.Lines.ShouldBeEmpty();

        PosCart negative = PosCartBuilder.Build(
            [new SaleLineInput(1, -2m, new Money(100m), DiscountSpec.None)],
            Catalogue(Product(1)),
            DiscountSpec.None);

        negative.IsValid.ShouldBeFalse();
        negative.Issues.ShouldContain(issue => issue.Code == PosErrorCodes.InvalidQuantity);
        negative.Issues[0].Code.ShouldBe("pos.invalid_quantity");
        negative.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void A_line_for_a_product_that_is_not_in_the_catalogue_is_refused()
    {
        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(99, 1m, new Money(100m), DiscountSpec.None)],
            Catalogue(Product(1)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeFalse();
        cart.Issues.ShouldContain(issue => issue.Code == PosErrorCodes.ProductNotFound);
        cart.Issues[0].Code.ShouldBe("pos.product_not_found");
        cart.Issues[0].ProductId.ShouldBe(99);
        cart.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void A_line_for_a_deactivated_product_is_refused()
    {
        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(1, 1m, new Money(100m), DiscountSpec.None)],
            Catalogue(Product(1, isActive: false)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeFalse();
        cart.Issues.ShouldContain(issue => issue.Code == PosErrorCodes.ProductInactive);
        cart.Issues[0].Code.ShouldBe("pos.product_inactive");
        cart.Issues[0].ProductId.ShouldBe(1);
        cart.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void A_line_for_more_than_the_shelf_holds_is_refused()
    {
        IReadOnlyDictionary<int, PosProductFacts> catalogue = Catalogue(Product(1, onHandScaled: 5));

        PosCart tooMany = PosCartBuilder.Build(
            [new SaleLineInput(1, 10m, new Money(100m), DiscountSpec.None)],
            catalogue,
            DiscountSpec.None);

        tooMany.IsValid.ShouldBeFalse();
        tooMany.Issues.ShouldContain(issue => issue.Code == PosErrorCodes.InsufficientStock);
        tooMany.Issues[0].Code.ShouldBe("pos.insufficient_stock");
        tooMany.Lines.ShouldBeEmpty();

        PosCart exactly = PosCartBuilder.Build(
            [new SaleLineInput(1, 5m, new Money(100m), DiscountSpec.None)],
            catalogue,
            DiscountSpec.None);

        exactly.IsValid.ShouldBeTrue();
        exactly.Issues.ShouldBeEmpty();
        exactly.Lines.Count.ShouldBe(1);
    }

    [Fact]
    public void The_same_product_twice_on_the_cart_is_refused()
    {
        PosCart cart = PosCartBuilder.Build(
            [
                new SaleLineInput(1, 1m, new Money(100m), DiscountSpec.None),
                new SaleLineInput(1, 2m, new Money(100m), DiscountSpec.None),
            ],
            Catalogue(Product(1)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeFalse();
        cart.Issues.ShouldContain(issue => issue.Code == PosErrorCodes.DuplicateProduct);
        cart.Issues[0].Code.ShouldBe("pos.duplicate_product");
        cart.Lines.Count.ShouldBe(1);
    }

    [Fact]
    public void A_price_that_excludes_tax_gets_the_tax_added_on_top()
    {
        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(1, 2m, new Money(100m), DiscountSpec.None)],
            Catalogue(Product(1, salePrice: 100m, taxRateBasisPoints: 1000)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeTrue();

        CartLine line = cart.Lines[0];
        line.Gross.Amount.ShouldBe(200.00m);
        line.Net.Amount.ShouldBe(200.00m);
        line.TaxableBase.Amount.ShouldBe(200.00m);
        line.Tax.Amount.ShouldBe(20.00m);
        line.Total.Amount.ShouldBe(220.00m);

        cart.Totals.Tax.Amount.ShouldBe(20.00m);
        cart.Totals.Total.Amount.ShouldBe(220.00m);
    }

    [Fact]
    public void A_price_that_includes_tax_has_the_tax_taken_out_of_it()
    {
        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(1, 2m, new Money(100m), DiscountSpec.None)],
            Catalogue(Product(1, salePrice: 100m, taxRateBasisPoints: 1000, priceIncludesTax: true)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeTrue();

        CartLine line = cart.Lines[0];
        line.Gross.Amount.ShouldBe(200.00m);
        line.Net.Amount.ShouldBe(200.00m);
        line.TaxableBase.Amount.ShouldBe(181.82m);
        line.Tax.Amount.ShouldBe(18.18m);
        line.Total.Amount.ShouldBe(200.00m);

        cart.Totals.Tax.Amount.ShouldBe(18.18m);
        cart.Totals.Total.Amount.ShouldBe(200.00m);
    }

    [Fact]
    public void A_fixed_line_discount_reduces_the_line_total_by_that_amount()
    {
        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(1, 2m, new Money(100m), DiscountSpec.FixedAmount(50m))],
            Catalogue(Product(1, salePrice: 100m, taxRateBasisPoints: 1000, priceIncludesTax: true)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeTrue();

        CartLine line = cart.Lines[0];
        line.Gross.Amount.ShouldBe(200.00m);
        line.DiscountAmount.Amount.ShouldBe(50.00m);
        line.Net.Amount.ShouldBe(150.00m);
        line.Total.Amount.ShouldBe(150.00m);

        cart.Totals.LineDiscount.Amount.ShouldBe(50.00m);
        cart.Totals.Discount.Amount.ShouldBe(50.00m);
        cart.Totals.Total.Amount.ShouldBe(150.00m);
    }

    [Fact]
    public void A_percentage_line_discount_reduces_the_line_total_by_that_percentage()
    {
        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(1, 2m, new Money(100m), DiscountSpec.Percentage(10m))],
            Catalogue(Product(1, salePrice: 100m, taxRateBasisPoints: 1000, priceIncludesTax: true)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeTrue();

        CartLine line = cart.Lines[0];
        line.Gross.Amount.ShouldBe(200.00m);
        line.DiscountAmount.Amount.ShouldBe(20.00m);
        line.Net.Amount.ShouldBe(180.00m);
        line.Total.Amount.ShouldBe(180.00m);

        cart.Totals.LineDiscount.Amount.ShouldBe(20.00m);
        cart.Totals.Total.Amount.ShouldBe(180.00m);
    }

    [Fact]
    public void An_invoice_discount_is_spread_over_the_lines_in_proportion()
    {
        PosCart cart = PosCartBuilder.Build(
            [
                new SaleLineInput(1, 1m, new Money(30m), DiscountSpec.None),
                new SaleLineInput(2, 1m, new Money(10m), DiscountSpec.None),
            ],
            Catalogue(Product(1, salePrice: 30m), Product(2, salePrice: 10m)),
            DiscountSpec.FixedAmount(4m));

        cart.IsValid.ShouldBeTrue();

        cart.Lines[0].InvoiceDiscountShare.Amount.ShouldBe(3.00m);
        cart.Lines[1].InvoiceDiscountShare.Amount.ShouldBe(1.00m);

        cart.Totals.InvoiceDiscount.Amount.ShouldBe(4.00m);
        cart.Totals.Discount.Amount.ShouldBe(4.00m);
        cart.Totals.Total.Amount.ShouldBe(36.00m);
    }

    [Fact]
    public void The_invoice_discount_shares_add_up_to_the_discount_exactly()
    {
        PosCart cart = PosCartBuilder.Build(
            [
                new SaleLineInput(1, 1m, new Money(10m), DiscountSpec.None),
                new SaleLineInput(2, 1m, new Money(10m), DiscountSpec.None),
                new SaleLineInput(3, 1m, new Money(10m), DiscountSpec.None),
            ],
            Catalogue(Product(1, salePrice: 10m), Product(2, salePrice: 10m), Product(3, salePrice: 10m)),
            DiscountSpec.FixedAmount(1m));

        cart.IsValid.ShouldBeTrue();

        decimal shares = cart.Lines.Sum(line => line.InvoiceDiscountShare.Amount);

        shares.ShouldBe(1.00m);
        cart.Lines.All(line => line.InvoiceDiscountShare.Amount is 0.33m or 0.34m).ShouldBeTrue();

        cart.Totals.InvoiceDiscount.Amount.ShouldBe(1.00m);
        cart.Totals.Total.Amount.ShouldBe(29.00m);
    }

    [Fact]
    public void The_totals_reconcile_with_the_lines()
    {
        PosCart cart = PosCartBuilder.Build(
            [
                new SaleLineInput(1, 2m, new Money(100m), DiscountSpec.None),
                new SaleLineInput(2, 1m, new Money(50m), DiscountSpec.None),
            ],
            Catalogue(Product(1, salePrice: 100m, taxRateBasisPoints: 1000), Product(2, salePrice: 50m)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeTrue();

        cart.Lines.Sum(line => line.Total.Amount).ShouldBe(cart.Totals.Total.Amount);
        cart.Lines.Sum(line => line.Gross.Amount).ShouldBe(cart.Totals.Subtotal.Amount);

        cart.Totals.Subtotal.Amount.ShouldBe(250.00m);
        cart.Totals.Tax.Amount.ShouldBe(20.00m);
        cart.Totals.Total.Amount.ShouldBe(270.00m);
    }

    [Fact]
    public void The_effective_discount_percent_is_the_discount_over_the_subtotal()
    {
        PosCartBuilder.EffectiveDiscountPercent(new Money(100m), new Money(10m)).ShouldBe(10m);
        PosCartBuilder.EffectiveDiscountPercent(new Money(100m), Money.Zero).ShouldBe(0m);
    }

    [Fact]
    public void A_price_that_differs_from_the_catalogue_is_an_override()
    {
        PosProductFacts product = Product(1, salePrice: 100m);

        PosCartBuilder.IsPriceOverride(product, new Money(90m)).ShouldBeTrue();
    }

    [Fact]
    public void A_price_that_matches_the_catalogue_is_not_an_override()
    {
        PosProductFacts product = Product(1, salePrice: 100m);

        PosCartBuilder.IsPriceOverride(product, new Money(100m)).ShouldBeFalse();
    }

    [Fact]
    public void A_quantity_the_unit_cannot_express_is_refused()
    {
        // The product is sold by the piece, so two and a half pieces is not a quantity of anything.
        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(1, 2.6m, new Money(100m), DiscountSpec.None)],
            Catalogue(Product(1, unitScale: 0)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeFalse();
        cart.Issues.ShouldContain(issue => issue.Code == PosErrorCodes.InvalidQuantity);
        cart.Issues[0].Code.ShouldBe("pos.invalid_quantity");
        cart.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void A_quantity_the_unit_can_express_is_accepted()
    {
        // The same product, sold by the kilogram, takes the quantity exactly.
        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(1, 2.6m, new Money(100m), DiscountSpec.None)],
            Catalogue(Product(1, unitScale: 2)),
            DiscountSpec.None);

        cart.IsValid.ShouldBeTrue();
        cart.Lines.ShouldHaveSingleItem();
        cart.Lines[0].Quantity.ShouldBe(2.6m);
        cart.Lines[0].QuantityScaled.ShouldBe(260L);
    }

    [Fact]
    public void A_product_that_is_not_in_the_catalogue_is_reported_before_a_duplicate()
    {
        // Both lines are for a product the shop does not sell. The cashier is told the product is
        // missing, not that the same product is on the cart twice.
        PosCart cart = PosCartBuilder.Build(
        [
            new SaleLineInput(99, 1m, new Money(100m), DiscountSpec.None),
            new SaleLineInput(99, 1m, new Money(100m), DiscountSpec.None),
        ],
        Catalogue(Product(1)),
        DiscountSpec.None);

        cart.IsValid.ShouldBeFalse();
        cart.Issues[0].Code.ShouldBe("pos.product_not_found");
        cart.Issues.ShouldNotContain(issue => issue.Code == PosErrorCodes.DuplicateProduct);
    }

    private static PosProductFacts Product(
        int id,
        decimal salePrice = 100m,
        int taxRateBasisPoints = 0,
        bool priceIncludesTax = false,
        bool isActive = true,
        long onHandScaled = 1000,
        int unitScale = 0) =>
        new(
            id,
            $"SKU-{id:D3}",
            $"Product {id}",
            $"Product {id} AR",
            null,
            "Piece",
            unitScale,
            new Money(salePrice),
            taxRateBasisPoints,
            priceIncludesTax,
            isActive,
            onHandScaled);

    private static IReadOnlyDictionary<int, PosProductFacts> Catalogue(params PosProductFacts[] products)
    {
        var catalogue = new Dictionary<int, PosProductFacts>();

        foreach (PosProductFacts product in products)
        {
            catalogue[product.Id] = product;
        }

        return catalogue;
    }
}
