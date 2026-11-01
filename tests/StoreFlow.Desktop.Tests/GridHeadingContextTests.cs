using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using StoreFlow.Desktop.Controls;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Checks what a table's column heading can see before it is handed anything.
/// </summary>
/// <remarks>
/// The heading of a column is not in the visual tree of the page, so it does not inherit the page's
/// context the way everything else on the screen does. WPF hands it a context of its own, which is
/// why a heading that reads from it finds nothing - and why the application hands the heading the
/// table's own words instead.
/// </remarks>
public sealed class GridHeadingContextTests
{
    [Fact]
    public void A_heading_is_not_handed_the_page_by_itself()
    {
        TestHost.RunOnStaThread(() =>
        {
            var words = new Words();

            string page =
                "<UserControl xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
                + "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"
                + "<DataGrid x:Name='TheGrid' EnableColumnVirtualization='False'>"
                + "<DataGrid.Columns>"
                + "<DataGridTextColumn Width='200'>"
                + "<DataGridTextColumn.Header><TextBlock Text='{Binding Heading}' /></DataGridTextColumn.Header>"
                + "</DataGridTextColumn>"
                + "</DataGrid.Columns>"
                + "</DataGrid>"
                + "</UserControl>";

            var pageElement = (UserControl)XamlReader.Parse(page);
            pageElement.DataContext = words;

            var window = new Window { Content = pageElement, Width = 600, Height = 300 };
            window.Show();

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var grid = (DataGrid)pageElement.FindName("TheGrid");
            grid.ItemsSource = new[] { words };

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var heading = (TextBlock)grid.Columns[0].Header;

            heading.DataContext.ShouldNotBeSameAs(words);
            heading.Text.ShouldNotBe("The heading");

            // Handing the heading the table's own words is what makes it read.
            GridHeadingContext.SetIsEnabled(grid, true);

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

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
