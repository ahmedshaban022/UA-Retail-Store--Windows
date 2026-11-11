namespace StoreFlow.Application.Common;

using StoreFlow.Application.Abstractions;

/// <summary>
/// Writes CSV text. Pure and side-effect free so it can be tested directly and reused by the
/// backup manifest and the report exporters.
/// </summary>
/// <remarks>
/// The output is UTF-8 with a byte-order mark, which is what makes Arabic product names open
/// correctly in Excel. Cells that begin with a character Excel treats as a formula are prefixed
/// with an apostrophe so exported data cannot be executed as a formula.
/// </remarks>
public static class CsvWriter
{
    /// <summary>The UTF-8 byte-order mark, written at the start of every CSV file.</summary>
    public static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>Characters Excel interprets as the start of a formula.</summary>
    private static readonly char[] FormulaPrefixes = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>Renders a table as CSV text, without the byte-order mark.</summary>
    public static string Write(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var builder = new System.Text.StringBuilder();

        builder.AppendLine(string.Join(',', table.Columns.Select(column => Escape(column.Header))));

        foreach (var row in table.Rows)
        {
            builder.AppendLine(string.Join(',', row.Select(Escape)));
        }

        return builder.ToString();
    }

    /// <summary>Escapes a single cell value.</summary>
    public static string Escape(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTimeOffset dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        // The formula guard is for text. A number that begins with a minus sign is a number, and
        // turning it into text would make a spreadsheet ignore it in a sum or in a pivot — and a
        // loss-making product's margin is exactly the figure that is negative.
        if (value is string && text.Length > 0 && FormulaPrefixes.Contains(text[0]))
        {
            text = string.Concat("'", text);
        }

        if (text.Contains('"', StringComparison.Ordinal) ||
            text.Contains(',', StringComparison.Ordinal) ||
            text.Contains('\n', StringComparison.Ordinal) ||
            text.Contains('\r', StringComparison.Ordinal))
        {
            return string.Concat("\"", text.Replace("\"", "\"\"", StringComparison.Ordinal), "\"");
        }

        return text;
    }
}
