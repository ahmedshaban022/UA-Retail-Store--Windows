using StoreFlow.Application.Catalog;
using Shouldly;

namespace StoreFlow.Application.Tests;

public sealed class ProductCsvMapperTests
{
    private static CatalogSnapshot Snapshot(
        IReadOnlyDictionary<string, int>? skus = null,
        IReadOnlyDictionary<string, int>? barcodes = null,
        IReadOnlyDictionary<string, int>? categories = null,
        IReadOnlyDictionary<string, int>? units = null) =>
        new(
            skus ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            barcodes ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            categories ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Grocery"] = 1 },
            units ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Piece"] = 1 },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["standard"] = 1 },
            1,
            1);

    private static CsvColumnMap Map(IReadOnlyList<string> headers, IReadOnlyDictionary<string, string>? aliases = null) =>
        ProductCsvMapper.ResolveHeaders(headers, aliases ?? new Dictionary<string, string>());

    [Fact]
    public void Headings_are_matched_without_regard_to_case_or_spacing()
    {
        CsvColumnMap map = Map(["SKU", "Sale Price", "Name"]);

        map.ColumnIndexes[ProductCsvMapper.FieldSku].ShouldBe(0);
        map.ColumnIndexes[ProductCsvMapper.FieldSalePrice].ShouldBe(1);
        map.ColumnIndexes[ProductCsvMapper.FieldName].ShouldBe(2);
    }

    [Fact]
    public void A_heading_the_importer_knows_by_another_name_is_matched()
    {
        CsvColumnMap map = Map(["Item Code", "Price", "Product Name"]);

        map.ColumnIndexes[ProductCsvMapper.FieldSku].ShouldBe(0);
        map.ColumnIndexes[ProductCsvMapper.FieldSalePrice].ShouldBe(1);
        map.ColumnIndexes[ProductCsvMapper.FieldName].ShouldBe(2);
    }

    [Fact]
    public void The_localised_heading_the_application_writes_is_read_back()
    {
        var aliases = new Dictionary<string, string> { [ProductCsvMapper.FieldSku] = "الكود" };

        CsvColumnMap map = Map(["الكود", "Name"], aliases);

        map.ColumnIndexes[ProductCsvMapper.FieldSku].ShouldBe(0);
    }

    [Fact]
    public void A_heading_that_maps_to_nothing_is_reported_and_not_fatal()
    {
        CsvColumnMap map = Map(["SKU", "Colour", "Name"]);

        map.UnmappedHeaders.ShouldContain("Colour");
        map.ColumnIndexes.ContainsKey(ProductCsvMapper.FieldSku).ShouldBeTrue();
    }

    [Fact]
    public void A_good_row_becomes_a_product()
    {
        IReadOnlyList<string[]> rows =
        [
            ["SKU", "Name", "Category", "Unit", "SalePrice", "CostPrice", "Barcode"],
            ["RICE-001", "Rice", "Grocery", "Piece", "450.00", "400.00", "6221000000017"],
        ];

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(),
            ProductImportMode.CreateOnly);

        ProductImportRow row = result.ShouldHaveSingleItem();
        row.IsValid.ShouldBeTrue();
        row.IsUpdate.ShouldBeFalse();
        row.Resolved.ShouldNotBeNull();
        row.Resolved.Sku.ShouldBe("RICE-001");
        row.Resolved.Name.ShouldBe("Rice");
        row.Resolved.CategoryId.ShouldBe(1);
        row.Resolved.UnitId.ShouldBe(1);
        row.Resolved.SalePrice.ShouldBe(450m);
        row.Resolved.CostPrice.ShouldBe(400m);
        row.Resolved.Barcode.ShouldBe("6221000000017");
    }

    [Fact]
    public void A_row_without_a_code_is_refused()
    {
        IReadOnlyList<string[]> rows = [["SKU", "Name", "SalePrice"], ["", "Rice", "450.00"]];

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(),
            ProductImportMode.CreateOnly);

        result.ShouldHaveSingleItem().Issues
            .ShouldContain(issue => issue.Code == ProductCsvMapper.MissingValueCode);
    }

    [Fact]
    public void A_code_that_appears_twice_in_the_file_is_refused_on_the_second_row()
    {
        IReadOnlyList<string[]> rows =
        [
            ["SKU", "Name", "SalePrice"],
            ["RICE-001", "Rice", "450.00"],
            ["rice-001", "Rice again", "450.00"],
        ];

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(),
            ProductImportMode.CreateOnly);

        result.Count.ShouldBe(2);
        result[0].IsValid.ShouldBeTrue();
        result[1].IsValid.ShouldBeFalse();
        result[1].Issues.ShouldContain(issue => issue.Code == ProductCsvMapper.DuplicateSkuInFileCode);
    }

    [Fact]
    public void A_code_that_already_exists_is_refused_when_the_import_only_creates()
    {
        IReadOnlyList<string[]> rows = [["SKU", "Name", "SalePrice"], ["RICE-001", "Rice", "450.00"]];

        var skus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["RICE-001"] = 7 };

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(skus: skus),
            ProductImportMode.CreateOnly);

        ProductImportRow row = result.ShouldHaveSingleItem();
        row.IsValid.ShouldBeFalse();
        row.Issues.ShouldContain(issue => issue.Code == ProductCsvMapper.SkuExistsCode);
    }

    [Fact]
    public void A_code_that_already_exists_updates_the_product_when_the_import_is_told_to()
    {
        IReadOnlyList<string[]> rows = [["SKU", "Name", "SalePrice"], ["RICE-001", "Rice", "450.00"]];

        var skus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["RICE-001"] = 7 };

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(skus: skus),
            ProductImportMode.UpsertBySku);

        ProductImportRow row = result.ShouldHaveSingleItem();
        row.IsValid.ShouldBeTrue();
        row.IsUpdate.ShouldBeTrue();
        row.ExistingProductId.ShouldBe(7);
    }

    [Fact]
    public void A_barcode_that_belongs_to_another_product_is_refused()
    {
        IReadOnlyList<string[]> rows = [["SKU", "Name", "Barcode"], ["RICE-001", "Rice", "6221000000017"]];

        var barcodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["6221000000017"] = 7 };

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(barcodes: barcodes),
            ProductImportMode.CreateOnly);

        result.ShouldHaveSingleItem().Issues
            .ShouldContain(issue => issue.Code == ProductCsvMapper.BarcodeInUseCode);
    }

    [Fact]
    public void A_barcode_that_belongs_to_the_product_being_updated_is_allowed()
    {
        IReadOnlyList<string[]> rows = [["SKU", "Name", "Barcode", "SalePrice"], ["RICE-001", "Rice", "6221000000017", "450.00"]];

        var skus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["RICE-001"] = 7 };
        var barcodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["6221000000017"] = 7 };

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(skus: skus, barcodes: barcodes),
            ProductImportMode.UpsertBySku);

        result.ShouldHaveSingleItem().IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_category_that_does_not_exist_is_refused_with_a_message_that_says_what_to_do()
    {
        IReadOnlyList<string[]> rows = [["SKU", "Name", "Category", "SalePrice"], ["RICE-001", "Rice", "Bakery", "450.00"]];

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(),
            ProductImportMode.CreateOnly);

        ImportIssue issue = result.ShouldHaveSingleItem().Issues
            .ShouldHaveSingleItem();

        issue.Code.ShouldBe(ProductCsvMapper.UnknownCategoryCode);
        issue.Message.ShouldContain("Bakery");
    }

    [Fact]
    public void A_price_that_is_not_a_number_is_refused()
    {
        IReadOnlyList<string[]> rows = [["SKU", "Name", "SalePrice"], ["RICE-001", "Rice", "a lot"]];

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(),
            ProductImportMode.CreateOnly);

        result.ShouldHaveSingleItem().Issues
            .ShouldContain(issue => issue.Code == ProductCsvMapper.NotANumberCode);
    }

    [Fact]
    public void A_price_with_its_currency_attached_is_still_read()
    {
        IReadOnlyList<string[]> rows = [["SKU", "Name", "SalePrice"], ["RICE-001", "Rice", "450.00 EGP"]];

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(),
            ProductImportMode.CreateOnly);

        result.ShouldHaveSingleItem().Resolved!.SalePrice.ShouldBe(450m);
    }

    [Fact]
    public void The_active_column_reads_both_ways_of_saying_yes()
    {
        IReadOnlyList<string[]> rows =
        [
            ["SKU", "Name", "IsActive", "SalePrice"],
            ["A-1", "One", "false", "450.00"],
            ["A-2", "Two", "لا", "450.00"],
        ];

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(),
            ProductImportMode.CreateOnly);

        result[0].Resolved!.IsActive.ShouldBeFalse();
        result[1].Resolved!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void A_blank_line_is_skipped_rather_than_reported()
    {
        IReadOnlyList<string[]> rows =
        [
            ["SKU", "Name", "SalePrice"],
            ["RICE-001", "Rice", "450.00"],
            ["", "", ""],
        ];

        IReadOnlyList<ProductImportRow> result = ProductCsvMapper.BuildRows(
            rows,
            Map(rows[0]),
            Snapshot(),
            ProductImportMode.CreateOnly);

        result.ShouldHaveSingleItem();
    }
}
