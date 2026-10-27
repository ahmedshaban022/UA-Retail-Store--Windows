using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Shows an element when an integer equals the value given as the converter parameter. The wizard
/// uses it to swap its steps without any code behind the panel.
/// </summary>
public sealed class IntEqualsVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return Visibility.Collapsed;
        }

        int current = System.Convert.ToInt32(value, CultureInfo.InvariantCulture);

        if (parameter is null ||
            !int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int expected))
        {
            return Visibility.Collapsed;
        }

        return current == expected ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}
