using StoreFlow.Application.Abstractions;

namespace StoreFlow.Application.Printing;

/// <summary>How a piece of text sits on the page.</summary>
public enum PrintTextAlignment
{
    /// <summary>Against the left edge.</summary>
    Left = 0,

    /// <summary>In the middle.</summary>
    Center = 1,

    /// <summary>Against the right edge.</summary>
    Right = 2,
}

/// <summary>
/// One piece of text placed on the page, with everything needed to draw it and nothing else.
/// </summary>
/// <remarks>
/// The page is laid out once, here, and the same layout drives the on-screen preview and the
/// printed sheet. That is what makes the preview worth trusting: it is not a second rendering of
/// the sale, it is the same one (PRD §5.1 GEN-05).
/// </remarks>
/// <param name="Text">The text to draw.</param>
/// <param name="Top">How far down the page, in points.</param>
/// <param name="Left">How far from the left edge, in points.</param>
/// <param name="Width">How wide the text may be, in points.</param>
/// <param name="Height">How tall the line is, in points.</param>
/// <param name="Alignment">Where the text sits inside its width.</param>
/// <param name="IsBold">True when the text is set in bold.</param>
/// <param name="FontSize">The size of the text, in points.</param>
/// <param name="IsRule">True when the line is a rule rather than text.</param>
public sealed record PrintTextLine(
    string Text,
    double Top,
    double Left,
    double Width,
    double Height,
    PrintTextAlignment Alignment,
    bool IsBold,
    double FontSize,
    bool IsRule);

/// <summary>
/// Turns a printable document into the lines that will be drawn on the page.
/// </summary>
/// <remarks>
/// This is pure arithmetic on the document: no fonts are measured, no device is asked. The width
/// of the page is given, and the result is the same wherever it is drawn.
/// </remarks>
public static class PrintLayout
{
    /// <summary>The size of the body text, in points.</summary>
    public const double BodyFontSize = 9d;

    /// <summary>The size of the title, in points.</summary>
    public const double TitleFontSize = 12d;

    /// <summary>The size of the grand total, in points.</summary>
    public const double TotalFontSize = 11d;

    /// <summary>The height of one line of body text, in points.</summary>
    public const double LineHeight = 13d;

    /// <summary>The height of a rule, in points.</summary>
    public const double RuleHeight = 4d;

    /// <summary>
    /// Lays the document out on a page of the given width.
    /// </summary>
    /// <param name="data">The document to lay out.</param>
    /// <param name="contentWidth">The width of the page, in points.</param>
    /// <returns>The lines, top to bottom.</returns>
    public static IReadOnlyList<PrintTextLine> Build(PrintDocumentData data, double contentWidth)
    {
        ArgumentNullException.ThrowIfNull(data);

        var lines = new List<PrintTextLine>();
        double top = 0d;

        top = Add(lines, data.Title, top, contentWidth, PrintTextAlignment.Center, true, TitleFontSize);
        top += LineHeight;

        foreach (PrintLine line in data.HeaderLines)
        {
            top = Add(lines, line.Text, top, contentWidth, AlignmentOf(line.Alignment), IsBold(line.Style), FontOf(line.Style));
        }

        if (data.Table is not null)
        {
            top = AddTable(lines, data.Table, top, contentWidth);
        }

        foreach (PrintLine line in data.Lines)
        {
            top = Add(lines, line.Text, top, contentWidth, AlignmentOf(line.Alignment), IsBold(line.Style), FontOf(line.Style));
        }

        if (data.FooterLines is not null)
        {
            foreach (PrintLine line in data.FooterLines)
            {
                top = Add(lines, line.Text, top, contentWidth, AlignmentOf(line.Alignment), IsBold(line.Style), FontOf(line.Style));
            }
        }

        return lines;
    }

    /// <summary>The total height the laid-out lines occupy, in points.</summary>
    /// <param name="lines">The laid-out lines.</param>
    /// <returns>The height, in points.</returns>
    public static double HeightOf(IReadOnlyList<PrintTextLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        double height = 0d;

        foreach (PrintTextLine line in lines)
        {
            height = Math.Max(height, line.Top + line.Height);
        }

        return height;
    }

    private static double AddTable(
        List<PrintTextLine> lines,
        PrintTable table,
        double top,
        double contentWidth)
    {
        double totalWeight = 0d;

        foreach (PrintColumn column in table.Columns)
        {
            totalWeight += column.Width;
        }

        if (totalWeight <= 0d)
        {
            return top;
        }

        double[] widths = new double[table.Columns.Count];

        for (int index = 0; index < table.Columns.Count; index++)
        {
            widths[index] = contentWidth * table.Columns[index].Width / totalWeight;
        }

        double left = 0d;

        for (int index = 0; index < table.Columns.Count; index++)
        {
            PrintColumn column = table.Columns[index];

            top = Add(
                lines,
                column.Header,
                top,
                widths[index],
                AlignmentOf(column.Alignment),
                true,
                BodyFontSize,
                left);

            left += widths[index];
        }

        top += LineHeight;

        foreach (IReadOnlyList<string> row in table.Rows)
        {
            left = 0d;

            for (int index = 0; index < table.Columns.Count; index++)
            {
                string text = index < row.Count ? row[index] : string.Empty;

                top = Add(
                    lines,
                    text,
                    top,
                    widths[index],
                    AlignmentOf(table.Columns[index].Alignment),
                    false,
                    BodyFontSize,
                    left);

                left += widths[index];
            }
        }

        return top + LineHeight;
    }

    private static double Add(
        List<PrintTextLine> lines,
        string text,
        double top,
        double width,
        PrintTextAlignment alignment,
        bool isBold,
        double fontSize,
        double left = 0d)
    {
        double height = fontSize >= TitleFontSize ? LineHeight + 2d : LineHeight;

        lines.Add(new PrintTextLine(
            text ?? string.Empty,
            top,
            left,
            width,
            height,
            alignment,
            isBold,
            fontSize,
            false));

        return top + height;
    }

    private static PrintTextAlignment AlignmentOf(string? alignment) => alignment switch
    {
        "center" => PrintTextAlignment.Center,
        "right" => PrintTextAlignment.Right,
        _ => PrintTextAlignment.Left,
    };

    private static bool IsBold(string? style) =>
        style is not null && style.Contains("bold", StringComparison.OrdinalIgnoreCase);

    private static double FontOf(string? style) => style switch
    {
        "title" => TitleFontSize,
        "total" => TotalFontSize,
        _ => BodyFontSize,
    };
}
