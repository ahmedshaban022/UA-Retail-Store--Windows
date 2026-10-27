using System.Globalization;
using System.Windows.Data;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Reverses a boolean, so two options that are each other's opposite can share one property.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}
