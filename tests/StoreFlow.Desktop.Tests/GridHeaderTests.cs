using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Checks how a table's column headings behave when the table sits on a tab that is opened later.
/// </summary>
/// <remarks>
/// <para>A column of a data grid is not part of the visual tree, so a heading that tries to walk up
/// to the table - with a relative source or with an element name - is resolved against a name
/// scope it does not have, and the heading comes out empty. The heading that works names the table
/// directly, which is what a reference to a named element gives. The test that reads the views
/// pins that rule: every heading in the application names its table.</para>
/// </remarks>
public sealed class GridHeaderTests
{
    /// <summary>
    /// A heading written out in full reads as expected when the table is the one that is open from
    /// the start.
    /// </summary>
    [Fact]
    public void A_heading_written_out_in_full_reads_when_the_table_is_open_from_the_start()
    {
        RunOnAStaThread(() =>
        {
            var model = new Model();

            var grid = BuildGrid(model, "Literal", out _);

            var window = new Window { Content = grid, Width = 600, Height = 300 };
            window.Show();

            Pump();

            Heading(grid).Text.ShouldBe("The heading");

            window.Close();
        });
    }

    /// <summary>
    /// A heading that names the table directly reads as expected even when the table is on a tab
    /// that is opened later. This is the form the application uses.
    /// </summary>
    [Fact]
    public void A_heading_that_names_its_table_reads_when_the_table_is_on_a_later_tab()
    {
        RunOnAStaThread(() =>
        {
            var model = new Model();

            var grid = BuildGrid(model, "Named", out TextBlock? caption);

            ShowOnALaterTab(grid);

            caption!.Text.ShouldBe("The heading");
        });
    }

    /// <summary>
    /// A heading built from a template that names the table directly reads as expected when the
    /// table is on a tab that is opened later.
    /// </summary>
    [Fact]
    public void A_heading_built_from_a_template_that_names_its_table_reads_on_a_later_tab()
    {
        RunOnAStaThread(() =>
        {
            var model = new Model();

            var grid = BuildGrid(model, "Template", out _);

            ShowOnALaterTab(grid);

            Heading(grid).Text.ShouldBe("The heading");
        });
    }

    /// <summary>The model the heading is bound to.</summary>
    private sealed class Model
    {
#pragma warning disable CA1822 // The heading is read through a binding, so it has to be an instance.
        public string Heading => "The heading";
#pragma warning restore CA1822

        public ObservableCollection<string> Rows { get; } = ["One"];
    }

    private static void ShowOnALaterTab(FrameworkElement content)
    {
        var tabs = new TabControl { Width = 600, Height = 300 };
        tabs.Items.Add(new TabItem { Header = "First", Content = new TextBlock { Text = "First" } });
        tabs.Items.Add(new TabItem { Header = "Second", Content = content });

        var window = new Window { Content = tabs };
        window.Show();

        Pump();

        tabs.SelectedIndex = 1;

        Pump();

        window.Close();
    }

    private static FrameworkElement BuildGrid(Model model, string style, out TextBlock? caption)
    {
        var grid = new DataGrid { ItemsSource = model.Rows, AutoGenerateColumns = false, DataContext = model };

        var column = new DataGridTextColumn { Width = 200 };

        caption = null;

        switch (style)
        {
            case "Literal":
                column.Header = new TextBlock { Text = "The heading" };
                break;

            case "Template":
                var template = new DataTemplate();

                FrameworkElementFactory framed = new FrameworkElementFactory(typeof(TextBlock));
                framed.SetBinding(
                    TextBlock.TextProperty,
                    new Binding("DataContext.Heading")
                    {
                        Source = grid,
                    });

                template.VisualTree = framed;

                column.HeaderTemplate = template;
                break;

            default:
                var named = new TextBlock();
                named.SetBinding(
                    TextBlock.TextProperty,
                    new Binding("DataContext.Heading")
                    {
                        Source = grid,
                    });

                column.Header = named;
                caption = named;
                break;
        }

        grid.Columns.Add(column);

        return grid;
    }

    private static TextBlock Heading(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);

        for (int index = 0; index < count; index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);

            if (child is TextBlock text && text.Text == "The heading")
            {
                return text;
            }

            TextBlock? found = Heading(child);

            if (found is not null)
            {
                return found;
            }
        }

        return null!;
    }

    private static void RunOnAStaThread(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // The failure has to travel back to the test thread.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
