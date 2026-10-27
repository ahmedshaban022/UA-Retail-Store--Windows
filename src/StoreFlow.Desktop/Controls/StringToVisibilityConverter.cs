using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Shows an element only when the bound string has content. Used for inline messages, so an empty
/// error never leaves a gap in the layout.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true,
        };

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}
