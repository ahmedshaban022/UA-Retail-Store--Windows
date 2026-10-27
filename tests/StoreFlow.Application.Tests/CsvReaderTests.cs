using StoreFlow.Application.Common;
using Shouldly;

namespace StoreFlow.Application.Tests;

public sealed class CsvReaderTests
{
    [Fact]
    public void Rows_and_fields_are_split_on_the_delimiter()
    {
        IReadOnlyList<string[]> rows = CsvReader.Parse("a,b,c\n1,2,3\n");

        rows.Count.ShouldBe(2);
        rows[0].ShouldBe(new[] { "a", "b", "c" });
        rows[1].ShouldBe(new[] { "1", "2", "3" });
    }

    [Fact]
    public void A_file_without_a_trailing_line_break_still_has_its_last_row()
    {
        IReadOnlyList<string[]> rows = CsvReader.Parse("a,b\n1,2");

        rows.Count.ShouldBe(2);
        rows[1].ShouldBe(new[] { "1", "2" });
    }

    [Fact]
    public void A_quoted_field_keeps_its_commas_and_line_breaks()
    {
        IReadOnlyList<string[]> rows = CsvReader.Parse("\"a,b\",\"line one\nline two\"");

        rows.Count.ShouldBe(1);
        rows[0][0].ShouldBe("a,b");
        rows[0][1].ShouldBe("line one\nline two");
    }

    [Fact]
    public void A_doubled_quote_inside_a_quoted_field_is_one_quote()
    {
        IReadOnlyList<string[]> rows = CsvReader.Parse("\"say \"\"hi\"\"\"");

        rows[0][0].ShouldBe("say \"hi\"");
    }

    [Fact]
    public void A_windows_line_break_ends_one_row_not_two()
    {
        IReadOnlyList<string[]> rows = CsvReader.Parse("a,b\r\n1,2\r\n");

        rows.Count.ShouldBe(2);
    }

    [Fact]
    public void A_byte_order_mark_does_not_end_up_in_the_first_heading()
    {
        IReadOnlyList<string[]> rows = CsvReader.Parse("\uFEFFSKU,Name\nA-1,Water\n");

        rows[0][0].ShouldBe("SKU");
    }

    [Fact]
    public void The_delimiter_is_detected_from_the_file()
    {
        CsvReader.DetectDelimiter("a;b;c\n1;2;3").ShouldBe(';');
        CsvReader.DetectDelimiter("a\tb\tc").ShouldBe('\t');
        CsvReader.DetectDelimiter("a,b,c").ShouldBe(',');
    }

    [Fact]
    public void A_delimiter_inside_a_quoted_field_is_not_mistaken_for_the_delimiter()
    {
        CsvReader.DetectDelimiter("\"a,b\";c;d").ShouldBe(';');
    }

    [Fact]
    public void A_file_is_read_and_parsed()
    {
        string path = Path.Combine(Path.GetTempPath(), $"storeflow-{Guid.NewGuid():N}.csv");

        try
        {
            File.WriteAllText(path, "SKU,Name\nA-1,Water\n", new System.Text.UTF8Encoding(false));

            IReadOnlyList<string[]> rows = CsvReader.ParseFile(path);

            rows.Count.ShouldBe(2);
            rows[1][1].ShouldBe("Water");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_price_is_read_with_a_point_as_the_separator()
    {
        CsvReader.TryParseDecimal("450.50", out decimal value).ShouldBeTrue();
        value.ShouldBe(450.50m);

        CsvReader.TryParseDecimal(" 12 ", out decimal whole).ShouldBeTrue();
        whole.ShouldBe(12m);
    }

    [Fact]
    public void A_price_that_is_not_a_number_is_refused()
    {
        CsvReader.TryParseDecimal("forty five", out _).ShouldBeFalse();
        CsvReader.TryParseDecimal(string.Empty, out _).ShouldBeFalse();
        CsvReader.TryParseDecimal(null, out _).ShouldBeFalse();
    }
}
