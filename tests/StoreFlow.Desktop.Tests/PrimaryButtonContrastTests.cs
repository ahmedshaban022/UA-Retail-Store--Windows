using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Shouldly;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Theming;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Pins the contrast of the primary button, written down as a test.
/// </summary>
/// <remarks>
/// <para>A button's label is a piece of text, and text needs a colour of its own. Left to
/// inheritance the label took the page's ink, so a primary button on the dark petrol accent
/// carried dark text on a dark fill — a label nobody could read.</para>
/// <para>The label now takes the button's colour by an explicit binding, and the primary button
/// says which colour that is: the one the theme reserves for text on the accent.</para>
/// </remarks>
public sealed class PrimaryButtonContrastTests
{
    [Fact]
    public void A_primary_button_keeps_the_colour_the_theme_reserves_for_text_on_the_accent()
    {
        TestHost.RunOnStaThread(() =>
        {
            var theme = new ThemeDefinition();
            Button button = BuildPrimaryButton(theme);

            ((SolidColorBrush)button.Foreground).Color.ShouldBe(Colors.White);
        });
    }

    [Fact]
    public void The_label_of_a_primary_button_is_not_the_ink_of_the_page()
    {
        TestHost.RunOnStaThread(() =>
        {
            var theme = new ThemeDefinition();
            Button button = BuildPrimaryButton(theme);

            ContentPresenter? presenter = FindVisualChild<ContentPresenter>(button);
            presenter.ShouldNotBeNull();

            // The label the presenter builds out of the button's text is the one the shopkeeper
            // reads, so this is the colour that matters.
            TextBlock? label = FindVisualChild<TextBlock>(presenter);
            label.ShouldNotBeNull();

            var labelColour = (SolidColorBrush)label!.Foreground;

            labelColour.Color.ShouldBe(Colors.White);
            labelColour.Color.ShouldNotBe(((SolidColorBrush)ThemeEngine.Brush(theme.Colors.TextPrimary)).Color);
        });
    }

    [Fact]
    public void A_bare_label_carries_no_colour_of_its_own()
    {
        TestHost.RunOnStaThread(() =>
        {
            // A label takes the colour of the thing it sits in. If the style for a label set a
            // colour, that colour would win over the inheritance and every label in the
            // application would be painted the same shade, whatever it was sitting on.
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(ThemeEngine.Build(new ThemeDefinition()));
            resources.MergedDictionaries.Add(LoadStyles("Base.xaml"));

            var host = new Border();
            host.SetValue(TextElement.ForegroundProperty, Brushes.White);
            host.Resources.MergedDictionaries.Add(resources);

            var label = new TextBlock { Text = "Save" };
            host.Child = label;

            host.Measure(new Size(200, 40));
            host.Arrange(new Rect(0, 0, 200, 40));
            host.UpdateLayout();

            // The style for a bare label was found and applied.
            label.Style.ShouldNotBeNull();

            // It brought typography and nothing else: the colour is inherited from the host.
            label.ReadLocalValue(TextElement.ForegroundProperty)
                .ShouldBe(DependencyProperty.UnsetValue);

            ((SolidColorBrush)label.Foreground).Color.ShouldBe(Colors.White);
        });
    }

    [Fact]
    public void A_primary_button_is_set_in_the_face_of_the_interface_and_given_weight()
    {
        TestHost.RunOnStaThread(() =>
        {
            var theme = new ThemeDefinition();
            Button button = BuildPrimaryButton(theme);

            button.FontWeight.ShouldBe(FontWeights.SemiBold);
            button.FontFamily.ShouldBe(ThemeEngine.Build(theme)["Font.Family.Ui"] as FontFamily);
        });
    }

    /// <summary>Builds a primary button carrying the application's own styles and theme.</summary>
    private static Button BuildPrimaryButton(ThemeDefinition theme)
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(ThemeEngine.Build(theme));
        resources.MergedDictionaries.Add(LoadStyles("Base.xaml"));
        resources.MergedDictionaries.Add(LoadStyles("Controls.xaml"));

        var button = new Button { Content = "Save" };
        button.Resources.MergedDictionaries.Add(resources);
        button.Style = (Style)resources["Button.Primary"];
        button.ApplyTemplate();
        button.Measure(new Size(200, 40));
        button.Arrange(new Rect(0, 0, 200, 40));
        button.UpdateLayout();

        return button;
    }

    private static ResourceDictionary LoadStyles(string fileName) => new()
    {
        Source = new Uri($"pack://application:,,,/StoreFlow.Desktop;component/Themes/{fileName}"),
    };

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
}
