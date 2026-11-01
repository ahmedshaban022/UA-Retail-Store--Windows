using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Pins the ways a table's column heading must not try to reach its words.
/// </summary>
/// <remarks>
/// <para>A column of a data grid is not part of the visual tree, so a heading that tries to reach
/// the page the way the rest of the screen does finds nothing:</para>
/// <list type="bullet">
/// <item>naming the table by element name reads nothing once the table is on a tab that is opened
/// later, because the heading is written down long before the table is realised;</item>
/// <item>naming the table by reference cannot even be loaded, because the table holds the heading
/// that names it and the markup loader cannot resolve that without a cycle;</item>
/// <item>walking up to the table reads nothing for the same reason as the element name.</item>
/// </list>
/// <para>What works is handing the heading the table's own context, which is what
/// <c>GridHeadingContext</c> does.</para>
/// </remarks>
public sealed class GridHeadingSourceTests
{
    public static TheoryData<string> WaysThatDoNotWork() => new()
    {
        { "ElementName" },
        { "Reference" },
        { "Ancestor" },
    };

    [Theory]
    [MemberData(nameof(WaysThatDoNotWork))]
    public void A_heading_that_looks_for_the_table_on_its_own_finds_nothing(string way)
    {
        TestHost.RunOnStaThread(() =>
        {
            string heading =
                way switch
                {
                    "ElementName" => "{Binding DataContext.Heading, ElementName=TheGrid}",
                    "Reference" => "{Binding DataContext.Heading, Source={x:Reference TheGrid}}",
                    _ => "{Binding DataContext.Heading, RelativeSource={RelativeSource AncestorType={x:Type DataGrid}}}",
                };

            string page =
                "<UserControl xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
                + "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"
                + "<TabControl x:Name='Tabs'>"
                + "<TabItem Header='First'><TextBlock Text='First' /></TabItem>"
                + "<TabItem Header='Second'>"
                + "<DataGrid x:Name='TheGrid' EnableColumnVirtualization='False'>"
                + "<DataGrid.Columns>"
                + "<DataGridTextColumn Width='200'>"
                + "<DataGridTextColumn.Header><TextBlock Text='" + heading + "' /></DataGridTextColumn.Header>"
                + "</DataGridTextColumn>"
                + "</DataGrid.Columns>"
                + "</DataGrid>"
                + "</TabItem>"
                + "</TabControl>"
                + "</UserControl>";

            var words = new Words();

            object? parsed = null;
            Exception? failure = null;

            try
            {
                parsed = XamlReader.Parse(page);
            }
#pragma warning disable CA1031 // The failure has to travel back to the test thread.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ex;
            }

            if (failure is not null)
            {
                // A form that cannot even be loaded is not a form that works.
                failure.Message.ShouldContain("cyclical");

                return;
            }

            var pageElement = (UserControl)parsed!;
            pageElement.DataContext = words;

            var window = new Window { Content = pageElement, Width = 600, Height = 300 };
            window.Show();

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var tabs = (TabControl)pageElement.FindName("Tabs");
            var grid = (DataGrid)pageElement.FindName("TheGrid");
            grid.ItemsSource = new[] { words };

            tabs.SelectedIndex = 1;

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var headingText = (TextBlock)grid.Columns[0].Header;

            window.Close();

            headingText.Text.ShouldNotBe(
                "The heading",
                $"{way} was expected to find nothing on a tab that is opened later.");
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
