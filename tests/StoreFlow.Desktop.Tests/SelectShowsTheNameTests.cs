using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shouldly;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Theming;
using StoreFlow.Desktop.ViewModels;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// A select shows the name of the record it holds, not the record itself.
/// </summary>
/// <remarks>
/// A closed select used to show what the record printed itself as, which reads as a line of code
/// to a shopkeeper: <c>AuditEntityTypeOption { Key = , DisplayName = All }</c>. The select now shows
/// the selected item through the same template the rows in the dropdown use, so the name the
/// interface means to show is the name it shows.
/// </remarks>
public sealed class SelectShowsTheNameTests
{
    [Fact]
    public void A_closed_select_shows_the_name_of_the_record_and_not_the_record()
    {
        TestHost.RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(ThemeEngine.Build(new ThemeDefinition()));
            resources.MergedDictionaries.Add(LoadStyles("Base.xaml"));
            resources.MergedDictionaries.Add(LoadStyles("Controls.xaml"));

            var select = new ComboBox { DisplayMemberPath = "DisplayName", Width = 200 };
            select.ItemsSource = new[] { new AuditEntityTypeOption(string.Empty, "All") };
            select.SelectedIndex = 0;
            select.Resources.MergedDictionaries.Add(resources);
            select.Style = (Style)resources[typeof(ComboBox)];

            select.ApplyTemplate();
            select.Measure(new Size(200, 40));
            select.Arrange(new Rect(0, 0, 200, 40));
            select.UpdateLayout();

            string shown = string.Join(" | ", FindTexts(select).Select(text => text.Text));

            shown.ShouldContain("All");
            shown.ShouldNotContain("{");
        });
    }

    [Fact]
    public void An_option_that_goes_into_a_select_says_what_it_is_called()
    {
        new AuditEntityTypeOption("product", "Product").ToString().ShouldBe("Product");
    }

    [Fact]
    public void A_select_of_plain_names_still_shows_the_name()
    {
        TestHost.RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(ThemeEngine.Build(new ThemeDefinition()));
            resources.MergedDictionaries.Add(LoadStyles("Base.xaml"));
            resources.MergedDictionaries.Add(LoadStyles("Controls.xaml"));

            var select = new ComboBox { Width = 200 };
            select.ItemsSource = new[] { "العربية", "English" };
            select.SelectedIndex = 1;
            select.Resources.MergedDictionaries.Add(resources);
            select.Style = (Style)resources[typeof(ComboBox)];

            select.ApplyTemplate();
            select.Measure(new Size(200, 40));
            select.Arrange(new Rect(0, 0, 200, 40));
            select.UpdateLayout();

            string shown = string.Join(" | ", FindTexts(select).Select(text => text.Text));

            shown.ShouldContain("English");
        });
    }

    private static ResourceDictionary LoadStyles(string fileName) => new()
    {
        Source = new Uri($"pack://application:,,,/StoreFlow.Desktop;component/Themes/{fileName}"),
    };

    private static List<TextBlock> FindTexts(DependencyObject parent)
    {
        var found = new List<TextBlock>();
        int count = VisualTreeHelper.GetChildrenCount(parent);

        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);

            if (child is TextBlock text)
            {
                found.Add(text);
            }

            found.AddRange(FindTexts(child));
        }

        return found;
    }
}
