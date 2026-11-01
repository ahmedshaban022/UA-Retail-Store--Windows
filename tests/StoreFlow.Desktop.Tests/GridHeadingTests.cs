using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using StoreFlow.Desktop.Controls;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Checks that a table's column heading finds its words, however the table is shown and whenever it
/// is realised.
/// </summary>
/// <remarks>
/// <para>A column of a data grid is not part of the visual tree, so a heading cannot reach the page
/// the way the rest of the screen does. The heading is handed the table's own context instead -
/// that is what <c>GridHeadingContext</c> does - and reads the words straight from it.</para>
/// </remarks>
public sealed class GridHeadingTests
{
    [Fact]
    public void A_heading_reads_its_words_from_the_table_it_belongs_to()
    {
        TestHost.RunOnStaThread(() =>
        {
            var words = new Words();

            string page =
                "<UserControl xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
                + "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"
                + "<TabControl x:Name='Tabs'>"
                + "<TabItem Header='First'><TextBlock Text='First' /></TabItem>"
                + "<TabItem Header='Second'>"
                + "<DataGrid x:Name='TheGrid' EnableColumnVirtualization='False'>"
                + "<DataGrid.Columns>"
                + "<DataGridTextColumn Width='200'>"
                + "<DataGridTextColumn.Header><TextBlock Text='{Binding Heading}' /></DataGridTextColumn.Header>"
                + "</DataGridTextColumn>"
                + "</DataGrid.Columns>"
                + "</DataGrid>"
                + "</TabItem>"
                + "</TabControl>"
                + "</UserControl>";

            var pageElement = (UserControl)XamlReader.Parse(page);
            pageElement.DataContext = words;

            var window = new Window { Content = pageElement, Width = 600, Height = 300 };
            window.Show();

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var tabs = (TabControl)pageElement.FindName("Tabs");
            var grid = (DataGrid)pageElement.FindName("TheGrid");
            grid.ItemsSource = new[] { words };

            tabs.SelectedIndex = 1;

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            // The heading is handed the table's own words, the way the application does it.
            GridHeadingContext.SetIsEnabled(grid, true);

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var heading = (TextBlock)grid.Columns[0].Header;

            heading.Text.ShouldBe("The heading");

            window.Close();
        });
    }

    /// <summary>The words the heading has to show.</summary>
    private sealed class Words
    {
#pragma warning disable CA1822 // The heading is read through a binding, so it has to be an instance.
        public string Heading => "The heading";
#pragma warning restore CA1822
    }
}
