using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using Shouldly;

namespace StoreFlow.Application.Tests;

public sealed class CsvWriterTests
{
    [Fact]
    public void Headers_and_rows_are_written_in_order()
    {
        var table = new ExportTable(
            [new ExportColumn("sku", "SKU"), new ExportColumn("name", "Name")],
            [new object?[] { "A-1", "Water" }]);

        string csv = CsvWriter.Write(table);

        csv.ShouldContain("SKU,Name");
        csv.ShouldContain("A-1,Water");
    }

    [Fact]
    public void Arabic_text_is_written_unchanged()
    {
        var table = new ExportTable(
            [new ExportColumn("name", "الاسم")],
            [new object?[] { "مياه" }]);

        string csv = CsvWriter.Write(table);

        csv.ShouldContain("مياه");
    }

    [Fact]
    public void A_cell_with_a_comma_or_quote_is_quoted()
    {
        CsvWriter.Escape("a,b").ShouldBe("\"a,b\"");
        CsvWriter.Escape("say \"hi\"").ShouldBe("\"say \"\"hi\"\"\"");
    }

    [Fact]
    public void A_cell_that_looks_like_a_formula_is_neutralised()
    {
        CsvWriter.Escape("=1+1").ShouldBe("'=1+1");
        CsvWriter.Escape("@SUM(A1)").ShouldBe("'@SUM(A1)");
    }

    [Fact]
    public void A_negative_number_stays_a_number()
    {
        // The formula guard is for text. A number that begins with a minus sign is a number, and
        // turning it into text would make a spreadsheet ignore it in a sum or in a pivot — and a
        // loss-making product's margin is exactly the figure that is negative.
        CsvWriter.Escape(-12.50m).ShouldBe("-12.50");
        CsvWriter.Escape(-12.50f).ShouldBe("-12.5");
        CsvWriter.Escape(-12).ShouldBe("-12");
    }

    [Fact]
    public void A_negative_number_in_an_export_is_written_as_a_number()
    {
        var table = new ExportTable(
            [new ExportColumn("margin", "Margin")],
            [new object?[] { -12.50m }]);

        string csv = CsvWriter.Write(table);

        csv.ShouldContain("-12.50");
        csv.ShouldNotContain("'-12.50");
    }

    [Fact]
    public void The_byte_order_mark_is_written_for_excel()
    {
        CsvWriter.ByteOrderMark.ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    public void A_null_cell_becomes_an_empty_field()
    {
        CsvWriter.Escape(null).ShouldBe(string.Empty);
    }
}
