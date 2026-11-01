using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Application.Abstractions;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Domain.Money;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Writes a monetary amount the way the shop's currency settings ask for, so every screen shows
/// prices the same way.
/// </summary>
public sealed class MoneyValueConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Money amount = value switch
        {
            Money money => money,
            decimal number => new Money(number),
            _ => default,
        };

        IThemeService? theme = App.Host?.Services.GetService(typeof(IThemeService)) as IThemeService;

        return theme is null ? amount.ToString() : theme.FormatMoney(amount);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}

/// <summary>
/// Writes the state of a catalogue record, in the language the application is running in.
/// </summary>
public sealed class CatalogStatusConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        IStrings? strings = App.Host?.Services.GetService(typeof(IStrings)) as IStrings;

        bool isActive = value is true;

        if (strings is null)
        {
            return isActive ? "Active" : "Inactive";
        }

        return isActive
            ? strings[StringKeys.ProductsActive]
            : strings[StringKeys.ProductsInactive];
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}

/// <summary>
/// Writes a moment in time the way the audit trail reads it: sorted, unambiguous, and always in
/// Western digits so the trail looks the same in both languages.
/// </summary>
public sealed class TimestampValueConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string written = value switch
        {
            DateTimeOffset moment => moment.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateTime moment => moment.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            _ => string.Empty,
        };

        return written;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}

/// <summary>
/// Shows a product's picture as a small thumbnail. The picture is decoded once and kept, so
/// scrolling the list does not decode the same file again and again.
/// </summary>
public sealed class ProductImageConverter : IValueConverter
{
    private const int ThumbnailWidth = 48;

    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null!;
        }

        if (Cache.TryGetValue(path, out ImageSource? cached))
        {
            return cached;
        }

        IProductImageStore? store = App.Host?.Services.GetService(typeof(IProductImageStore)) as IProductImageStore;

        if (store is null)
        {
            return null!;
        }

        try
        {
            string absolute = store.ResolvePath(path);

            if (!File.Exists(absolute))
            {
                return null!;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = ThumbnailWidth;
            bitmap.UriSource = new Uri(absolute, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            Cache[path] = bitmap;

            return bitmap;
        }
        catch (IOException)
        {
            return null!;
        }
        catch (UnauthorizedAccessException)
        {
            return null!;
        }
        catch (NotSupportedException)
        {
            return null!;
        }
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}
