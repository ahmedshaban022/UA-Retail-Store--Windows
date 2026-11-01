using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Pins the way a data grid column carries its heading, and the way a money column is written.
/// </summary>
/// <remarks>
/// <para>A data grid column is not part of the visual tree, so it inherits no data context of its
/// own. A binding written straight on the column therefore has nothing to read and the heading of
/// the column comes out empty. The heading is a label instead, and a label sits in the tree and
/// reads its words from the grid.</para>
/// <para>A money column keeps the direction of the page. Forcing it to read left to right puts
/// the currency on the wrong side of the amount in the Arabic interface, where the amount is read
/// first and the currency after it.</para>
/// </remarks>
public sealed class DataGridHeaderTests
{
    private static readonly string[] CatalogueViews =
    [
        "ProductsView.xaml",
        "CategoriesView.xaml",
        "UnitsView.xaml",
        "SuppliersView.xaml",
        "AuditView.xaml",
        "CsvImportWindow.xaml",
        "InventoryView.xaml",
        "PurchasesView.xaml",
        "SalesView.xaml",
        "ReturnDialogWindow.xaml",
        "ReverseDialogWindow.xaml",
    ];

    private sealed class Labels
    {
        public string SkuHeader { get; } = "Stock-keeping unit";
    }

    [Fact]
    public void A_heading_that_is_a_label_reads_its_label_from_what_holds_it()
    {
        TestHost.RunOnStaThread(() =>
        {
            // A heading is a label, and a label sits in the visual tree. The headings of a grid are
            // created inside the grid's own template, so a label there reads the data context of the
            // grid and shows the word the screen asked for.
            var caption = new TextBlock();
            caption.SetBinding(TextBlock.TextProperty, new Binding(nameof(Labels.SkuHeader)));

            var holder = new Border { DataContext = new Labels(), Child = caption };
            Layout(holder);

            caption.Text.ShouldBe("Stock-keeping unit");
        });
    }

    [Fact]
    public void The_heading_of_a_column_in_a_grid_reads_the_label_from_the_grid()
    {
        TestHost.RunOnStaThread(() =>
        {
            var grid = new DataGrid { DataContext = new Labels() };
            grid.ItemsSource = new[] { new Labels() };

            var column = new DataGridTextColumn();
            var caption = new TextBlock();
            caption.SetBinding(
                TextBlock.TextProperty,
                new Binding("DataContext." + nameof(Labels.SkuHeader))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1),
                });
            column.Header = caption;
            grid.Columns.Add(column);

            var window = new Window { Content = grid, Width = 400, Height = 200, ShowActivated = false };
            window.Show();
            window.UpdateLayout();

            try
            {
                var chain = new List<string>();
                DependencyObject? node = caption;
                while (node is not null)
                {
                    chain.Add(node.GetType().Name);
                    node = VisualTreeHelper.GetParent(node);
                }

                caption.Text.ShouldBe(
                    "Stock-keeping unit",
                    "chain=" + string.Join(" > ", chain)
                        + " dc=" + (caption.DataContext?.GetType().Name ?? "null")
                        + " parent=" + (VisualTreeHelper.GetParent(caption)?.GetType().Name ?? "null"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_binding_written_straight_on_the_column_finds_nothing_to_read()
    {
        TestHost.RunOnStaThread(() =>
        {
            // This is the trap the headings fell into. The column is not in the visual tree, so the
            // binding has no data context to read the label from and the heading stays empty.
            var grid = new DataGrid { DataContext = new Labels() };
            grid.ItemsSource = new[] { new Labels() };

            var column = new DataGridTextColumn { Header = new Binding(nameof(Labels.SkuHeader)) };
            grid.Columns.Add(column);

            grid.ApplyTemplate();
            Layout(grid);

            column.Header.ShouldNotBe("Stock-keeping unit");
        });
    }

    [Fact]
    public void A_money_column_keeps_the_direction_of_the_page()
    {
        TestHost.RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/StoreFlow.Desktop;component/Themes/Base.xaml"),
            };

            var host = new Border { FlowDirection = FlowDirection.RightToLeft };
            host.Resources.MergedDictionaries.Add(resources);

            var label = new TextBlock();
            label.Style = (Style)resources["Type.Numeric"];
            host.Child = label;

            host.Measure(new Size(300, 40));
            host.Arrange(new Rect(0, 0, 300, 40));
            host.UpdateLayout();

            // The style did not force a direction of its own, so the column reads in the direction
            // of the page and the amount comes before the currency in Arabic.
            label.FlowDirection.ShouldBe(FlowDirection.RightToLeft);
        });
    }

    [Fact]
    public void No_catalogue_column_carries_a_heading_that_cannot_be_read()
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            // The sources are not beside the test, so there is nothing to read.
            return;
        }

        string views = Path.Combine(root, "src", "StoreFlow.Desktop", "Views");

        foreach (string name in CatalogueViews)
        {
            string path = Path.Combine(views, name);
            if (!File.Exists(path))
            {
                continue;
            }

            string text = File.ReadAllText(path);

            // A binding written straight on the column finds nothing to read: the column is not in
            // the visual tree and inherits no data context.
            Match straight = Regex.Match(text, @"<DataGrid\w*Column[^>]*\sHeader\s*=\s*""\{Binding");
            straight.Success.ShouldBeFalse(
                $"{name} binds a heading straight on the column, which reads nothing: {straight.Value}");

            // A heading that is a label has to read its words from the table's own context. A
            // column is not part of the visual tree, so the heading cannot reach the page the way
            // the rest of the screen does, and WPF hands it a context of its own.
            foreach (Match heading in Regex.Matches(text, @"<DataGrid\w*Column\.Header>.*?</DataGrid\w*Column\.Header>", RegexOptions.Singleline))
            {
                heading.Value.ShouldNotContain(
                    "DataContext.",
                    customMessage: $"{name} has a heading that reads the page instead of the table.");

                heading.Value.ShouldNotContain(
                    "x:Reference",
                    customMessage: $"{name} has a heading that names the table by reference, which the markup loader cannot resolve without a cycle.");

                heading.Value.ShouldNotContain(
                    "ElementName=",
                    customMessage: $"{name} has a heading that names the table by element name, which finds nothing on a tab that is opened later.");

                heading.Value.ShouldNotContain(
                    "RelativeSource",
                    customMessage: $"{name} has a heading that walks up to the table, which finds nothing on a tab that is opened later.");
            }
        }
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(600, 300));
        element.Arrange(new Rect(0, 0, 600, 300));
        element.UpdateLayout();
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);

        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);

            if (child is T match)
            {
                return match;
            }

            T? found = FindVisualChild<T>(child);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
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
