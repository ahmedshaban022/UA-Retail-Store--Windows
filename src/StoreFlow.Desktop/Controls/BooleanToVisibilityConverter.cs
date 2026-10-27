using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Shows an element when a boolean is true. Pass <c>Invert</c> as the converter parameter to show
/// it when the boolean is false instead.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>The converter parameter that reverses the result.</summary>
    public const string InvertParameter = "Invert";

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;

        if (parameter is string text &&
            string.Equals(text, InvertParameter, StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}
