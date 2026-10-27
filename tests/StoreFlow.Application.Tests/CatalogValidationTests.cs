using StoreFlow.Application.Catalog;
using StoreFlow.Domain.Money;
using Shouldly;

namespace StoreFlow.Application.Tests;

public sealed class CatalogValidationTests
{
    private static readonly Money Nothing = new(0m);

    [Fact]
    public void A_product_needs_a_code_and_a_name()
    {
        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateProduct(
            null,
            "  ",
            null,
            null,
            Nothing,
            Nothing,
            0m,
            null,
            [],
            0);

        issues.ShouldNotBeEmpty();
        issues.Select(issue => issue.Field).ShouldContain("sku");
        issues.Select(issue => issue.Field).ShouldContain("name");
        issues.ShouldAllBe(issue => issue.Code == CatalogValidation.RequiredCode);
    }

    [Fact]
    public void A_price_cannot_be_negative()
    {
        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateProduct(
            "RICE-001",
            "Rice",
            null,
            null,
            new Money(-1m),
            new Money(-2m),
            0m,
            null,
            [],
            0);

        issues.Select(issue => issue.Field).ShouldContain("salePrice");
        issues.Select(issue => issue.Field).ShouldContain("costPrice");
    }

    [Fact]
    public void A_reorder_level_cannot_be_negative()
    {
        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateProduct(
            "RICE-001",
            "Rice",
            null,
            null,
            Nothing,
            Nothing,
            -1m,
            null,
            [],
            0);

        issues.ShouldHaveSingleItem().Code.ShouldBe(CatalogValidation.NegativeQuantityCode);
    }

    [Fact]
    public void A_valid_product_has_no_issues()
    {
        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateProduct(
            "RICE-001",
            "Rice",
            "أرز",
            null,
            new Money(450m),
            new Money(400m),
            2m,
            "6221000000017",
            [],
            0);

        issues.ShouldBeEmpty();
    }

    [Fact]
    public void A_barcode_cannot_carry_a_space()
    {
        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateBarcodes("622 100", []);

        issues.ShouldHaveSingleItem().Code.ShouldBe(CatalogValidation.InvalidBarcodeCode);
    }

    [Fact]
    public void The_same_barcode_cannot_be_listed_twice()
    {
        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateBarcodes(
            "6221000000017",
            ["6221000000017"]);

        issues.ShouldHaveSingleItem().Code.ShouldBe(CatalogValidation.DuplicateBarcodeCode);
    }

    [Fact]
    public void A_barcode_is_allowed_to_repeat_a_product_code_because_they_are_different_fields()
    {
        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateBarcodes(
            "6221000000017",
            ["6221000000018"]);

        issues.ShouldBeEmpty();
    }

    [Fact]
    public void A_value_that_is_too_long_is_refused()
    {
        string longName = new('a', CatalogValidation.MaxNameLength + 1);

        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateCategory(longName, null);

        issues.ShouldHaveSingleItem().Code.ShouldBe(CatalogValidation.TooLongCode);
    }

    [Fact]
    public void A_unit_precision_outside_the_supported_range_is_refused()
    {
        CatalogValidation.ValidateUnit("Kilogram", null, 0).ShouldBeEmpty();
        CatalogValidation.ValidateUnit("Kilogram", null, 6).ShouldBeEmpty();

        IReadOnlyList<ValidationIssue> tooMany = CatalogValidation.ValidateUnit("Kilogram", null, 7);
        tooMany.ShouldHaveSingleItem().Code.ShouldBe(CatalogValidation.InvalidPrecisionCode);

        IReadOnlyList<ValidationIssue> negative = CatalogValidation.ValidateUnit("Kilogram", null, -1);
        negative.ShouldHaveSingleItem().Code.ShouldBe(CatalogValidation.InvalidPrecisionCode);
    }

    [Fact]
    public void A_supplier_needs_a_name()
    {
        CatalogValidation.ValidateSupplier(null, null, null, null)
            .ShouldHaveSingleItem().Code.ShouldBe(CatalogValidation.RequiredCode);

        CatalogValidation.ValidateSupplier("Test Supplier", null, "0100 123 4567", "Delivers on Sunday")
            .ShouldBeEmpty();
    }
}
