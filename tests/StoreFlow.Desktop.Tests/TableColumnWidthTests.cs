using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Pins the width of every table column against the value it has to hold.
/// </summary>
/// <remarks>
/// <para>A column that is too narrow does not push its neighbour away - it cuts the value off in
/// the middle, and the reader is left with a figure that is not the figure. The widths in the
/// views are therefore measured against the real faces of the application: Segoe UI for the Latin
/// interface and the embedded Tajawal for the Arabic one, at the size the theme actually uses.</para>
/// <para>The check reads the widths out of the views themselves, so a column that is narrowed later
/// fails here rather than in a screenshot somebody has to notice.</para>
/// </remarks>
public sealed class TableColumnWidthTests
{
    /// <summary>
    /// The room the text engine may round away. Glyph advances are whole numbers of pixels, so a
    /// value that measures exactly as wide as its column can still lose its last character.
    /// </summary>
    private const double Tolerance = 4d;

    private const double BodySize = 14d;

    private const double SmallSize = 12d;

    /// <summary>What a column has to be able to show.</summary>
    /// <param name="Column">The name of the column, as its heading names it.</param>
    /// <param name="Values">The longest values the column can carry, in either language.</param>
    /// <param name="Badge">True when the column carries a badge rather than plain text.</param>
    private sealed record Expectation(string Column, string[] Values, bool Badge = false);

    /// <summary>One table of one screen.</summary>
    /// <param name="View">The file the table is written in.</param>
    /// <param name="Grid">The name of the table.</param>
    /// <param name="Expectations">What each of its columns has to be able to show.</param>
    private sealed record Table(string View, string Grid, Expectation[] Expectations);

    private static readonly Table[] Tables =
    [
        new("InventoryView.xaml", "BalancesGrid",
        [
            new("Sku", ["RICE-001"]),
            new("Unit", ["Kilogram"]),
            new("OnHand", ["1250"]),
            new("Damaged", ["10"]),
            new("AverageCost", ["6,460.00 EGP", "6,460.00 \u062c.\u0645"]),
            new("StockValue", ["6,460.00 EGP", "6,460.00 \u062c.\u0645"]),
            new("ReorderLevel", ["1000"]),
            new("Status", ["Low", "\u0645\u0646\u062e\u0641\u0636"], Badge: true),
        ]),
        new("InventoryView.xaml", "MovementsGrid",
        [
            new("Date", ["2026-11-01"]),
            new("Sku", ["RICE-001"]),
            new("MovementType", ["Purchase receipt", "Count correction", "\u0627\u0633\u062a\u0644\u0627\u0645 \u0645\u0634\u062a\u0631\u064a\u0627\u062a"]),
            new("QuantityDelta", ["-20"]),
            new("AverageCost", ["6,460.00 EGP", "6,460.00 \u062c.\u0645"]),
            new("RunningBalance", ["1250"]),
            new("Posted", ["2026-11-01 13:45"]),
        ]),
        new("InventoryView.xaml", "CountsGrid",
        [
            new("CountNumber", ["CNT-2026-0001"]),
            new("Date", ["2026-11-01"]),
            new("CountLines", ["100"]),
            new("CountDifference", ["1000"]),
            new("CountDifferenceValue", ["6,460.00 EGP", "6,460.00 \u062c.\u0645"]),
            new("Status", ["Awaiting approval", "\u0628\u0627\u0646\u062a\u0638\u0627\u0631 \u0627\u0644\u0627\u0639\u062a\u0645\u0627\u062f"], Badge: true),
        ]),
        new("PurchasesView.xaml", "PurchasesGrid",
        [
            new("Number", ["PUR-2026-0001"]),
            new("Date", ["2026-11-01"]),
            new("Total", ["7,600.00 EGP", "7,600.00 \u062c.\u0645"]),
            new("Reverses", ["PUR-2026-0002"]),
            new("Status", ["Cancelled", "\u0645\u0644\u063a\u0649"], Badge: true),
        ]),
        new("ProductsView.xaml", "ProductsGrid",
        [
            new("Sku", ["RICE-001"]),
            new("Unit", ["Kilogram"]),
            new("SalePrice", ["7,600.00 EGP", "7,600.00 \u062c.\u0645"]),
            new("CostPrice", ["6,460.00 EGP", "6,460.00 \u062c.\u0645"]),
            new("Barcode", ["6221031492018"]),
            new("Status", ["Inactive", "\u063a\u064a\u0631 \u0646\u0634\u0637"], Badge: true),
        ]),
    ];

    [Fact]
    public void Every_column_is_wide_enough_for_the_value_it_holds()
    {
        foreach ((Table table, Column column, Expectation expectation) in Columns())
        {
            double required = RequiredWidth(expectation);

            column.Width.ShouldBeGreaterThanOrEqualTo(
                required + Tolerance,
                $"{table.View} / {table.Grid} / {expectation.Column} is {column.Width} wide, but "
                + $"{string.Join(" / ", expectation.Values)} needs {required}.");
        }
    }

    [Fact]
    public void A_column_that_takes_a_share_of_the_width_keeps_at_least_its_minimum()
    {
        foreach ((Table table, Column column, Expectation expectation) in Columns())
        {
            if (!column.IsShare)
            {
                continue;
            }

            double required = RequiredWidth(expectation);

            column.Minimum.ShouldBeGreaterThanOrEqualTo(
                required + Tolerance,
                $"{table.View} / {table.Grid} / {expectation.Column} may shrink to {column.Minimum}, but "
                + $"{string.Join(" / ", expectation.Values)} needs {required}.");
        }
    }

    private static IEnumerable<(Table Table, Column Column, Expectation Expectation)> Columns()
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            // The sources are not beside the test, so there is nothing to read.
            yield break;
        }

        foreach (Table table in Tables)
        {
            string path = Path.Combine(root, "src", "StoreFlow.Desktop", "Views", table.View);
            if (!File.Exists(path))
            {
                continue;
            }

            IReadOnlyDictionary<string, Column> columns = ColumnsOf(File.ReadAllText(path), table.Grid);

            foreach (Expectation expectation in table.Expectations)
            {
                if (!columns.TryGetValue(expectation.Column, out Column? column) || column is null)
                {
                    throw new InvalidOperationException(
                        $"{table.View} / {table.Grid} has no column named {expectation.Column}.");
                }

                yield return (table, column, expectation);
            }
        }
    }

    /// <summary>One column of a table, as the view declares it.</summary>
    /// <param name="Width">The width the column asks for.</param>
    /// <param name="Minimum">The smallest the column may be.</param>
    /// <param name="IsShare">True when the column takes a share of the width that is left over.</param>
    private sealed record Column(double Width, double Minimum, bool IsShare);

    private static IReadOnlyDictionary<string, Column> ColumnsOf(string xaml, string grid)
    {
        Match block = Regex.Match(
            xaml,
            "<DataGrid x:Name=\"" + grid + "\".*?</DataGrid>",
            RegexOptions.Singleline);

        if (!block.Success)
        {
            return new Dictionary<string, Column>();
        }

        var columns = new Dictionary<string, Column>(StringComparer.Ordinal);

        string[] parts = Regex.Split(block.Value, "(?=<DataGrid\\w*Column(?=[\\s>]))");

        foreach (string part in parts.Skip(1))
        {
            Match heading = Regex.Match(part, @"\{Binding (\w+?)Header\}");
            if (!heading.Success)
            {
                continue;
            }

            string name = heading.Groups[1].Value;

            Match width = Regex.Match(part, @"(?<!\w)Width=""([^""]+)""");
            Match minimum = Regex.Match(part, @"MinWidth=""([^""]+)""");

            if (!width.Success)
            {
                continue;
            }

            string text = width.Groups[1].Value;
            bool isShare = text.EndsWith('*');

            columns[name] = new Column(
                isShare ? Parse(minimum.Success ? minimum.Groups[1].Value : text) : Parse(text),
                minimum.Success ? Parse(minimum.Groups[1].Value) : 0d,
                isShare);
        }

        return columns;
    }

    private static double Parse(string text) =>
        double.Parse(text.TrimEnd('*'), CultureInfo.InvariantCulture);

    private static double RequiredWidth(Expectation expectation) =>
        expectation.Values
            .Select(value => Measure(value, expectation.Badge ? SmallSize : BodySize))
            .Max()
        + (expectation.Badge ? BadgeChrome() : 0d)
        + HorizontalMarginOfCell();

    private static double Measure(string value, double size)
    {
        var typeface = new Typeface(
            FamilyFor(value),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        var formatted = new FormattedText(
            value,
            CultureInfo.InvariantCulture,
            value.Any(IsArabic) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            typeface,
            size,
            Brushes.Black,
            1.0);

        return formatted.Width;
    }

    private static bool IsArabic(char character) => character is >= '\u0600' and <= '\u06FF';

    private static FontFamily? _arabic;

    /// <summary>
    /// The face of the Arabic interface, taken from the sources of the application so that the
    /// measurement is made with the same file that is shipped.
    /// </summary>
    private static FontFamily FamilyFor(string value)
    {
        if (!value.Any(IsArabic))
        {
            return new FontFamily("Segoe UI");
        }

        if (_arabic is not null)
        {
            return _arabic;
        }

        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return new FontFamily("Segoe UI");
        }

        string font = Path.Combine(root, "src", "StoreFlow.Desktop", "Fonts", "Tajawal-Regular.ttf");
        if (!File.Exists(font))
        {
            return new FontFamily("Segoe UI");
        }

        _arabic = new FontFamily("file:///" + font.Replace('\\', '/') + "#Tajawal");

        return _arabic;
    }

    /// <summary>The margin the cell keeps on both sides, which the value has to live inside.</summary>
    private static double HorizontalMarginOfCell()
    {
        Style style = (Style)Themes()[typeof(DataGridCell)];

        Setter? setter = style.Setters.OfType<Setter>()
            .FirstOrDefault(candidate => candidate.Property == Control.PaddingProperty);

        return setter?.Value is Thickness thickness ? thickness.Left + thickness.Right : 0d;
    }

    /// <summary>What a badge adds around the word inside it.</summary>
    private static double BadgeChrome()
    {
        Style style = (Style)Themes()["Badge.Status"];

        double padding = 0d;
        double border = 0d;

        foreach (Setter setter in style.Setters.OfType<Setter>())
        {
            if (setter.Property == Control.PaddingProperty && setter.Value is Thickness thickness)
            {
                padding = thickness.Left + thickness.Right;
            }

            if (setter.Property == Control.BorderThicknessProperty && setter.Value is Thickness edges)
            {
                border = edges.Left + edges.Right;
            }
        }

        return padding + border;
    }

    private static ResourceDictionary Themes()
    {
        var themes = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/StoreFlow.Desktop;component/Themes/Base.xaml"),
        };

        themes.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/StoreFlow.Desktop;component/Themes/Controls.xaml"),
        });

        return themes;
    }

    /// <summary>Finds the folder that holds the solution, walking up from the test.</summary>
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreFlow.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
