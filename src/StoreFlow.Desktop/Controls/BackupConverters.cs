using System.Globalization;
using System.Windows.Data;
using StoreFlow.Desktop.Localization;
using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Writes the outcome of a backup in the language the application is running in.
/// </summary>
public sealed class BackupResultConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        IStrings? strings = App.Host?.Services.GetService(typeof(IStrings)) as IStrings;

        if (value is not BackupResult result)
        {
            return string.Empty;
        }

        if (strings is null)
        {
            return result.ToString();
        }

        return result switch
        {
            BackupResult.Succeeded => strings[StringKeys.BackupResultSucceeded],
            BackupResult.Failed => strings[StringKeys.BackupResultFailed],
            _ => strings[StringKeys.BackupResultCancelled],
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}

/// <summary>
/// Writes the name of a backup destination in the language the application is running in.
/// </summary>
public sealed class BackupTargetConverter : IValueConverter
{
    private readonly IStrings? _strings;

    /// <summary>
    /// Creates the converter. It reads the application's strings when it converts.
    /// </summary>
    public BackupTargetConverter()
    {
    }

    /// <summary>
    /// Creates the converter with the strings it should write in.
    /// </summary>
    /// <param name="strings">The application's strings.</param>
    public BackupTargetConverter(IStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        _strings = strings;
    }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string kind || string.IsNullOrWhiteSpace(kind))
        {
            return string.Empty;
        }

        IStrings? strings = _strings
            ?? App.Host?.Services.GetService(typeof(IStrings)) as IStrings;

        if (strings is null)
        {
            return kind;
        }

        return kind switch
        {
            "LocalFolder" => strings[StringKeys.BackupTargetLocalFolder],
            "S3" => strings[StringKeys.BackupTargetS3],
            _ => kind,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}

/// <summary>
/// Writes a size in bytes the way a person reads it, so that the backup log does not show a row of
/// digits.
/// </summary>
public sealed class BackupSizeConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long size)
        {
            return string.Empty;
        }

        string[] units = ["bytes", "KB", "MB", "GB"];

        double amount = size;
        int unit = 0;

        while (amount >= 1024 && unit < units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{size} {units[unit]}")
            : string.Create(CultureInfo.InvariantCulture, $"{amount:0.0} {units[unit]}");
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");
}
