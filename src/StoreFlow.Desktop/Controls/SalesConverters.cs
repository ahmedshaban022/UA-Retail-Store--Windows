using System.Globalization;
using System.Windows.Data;
using StoreFlow.Application.Sales;
using StoreFlow.Desktop.Localization;
using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Writes the kind of a sales document in the language the application is running in, so the
/// register reads the same in both languages.
/// </summary>
public sealed class SalesLabelConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        IStrings? strings = App.Host?.Services.GetService(typeof(IStrings)) as IStrings;

        return value switch
        {
            SalesDocumentKind kind => strings is null
                ? kind.ToString()
                : strings[KeyFor(kind)],
            PaymentMethod method => strings is null
                ? method.ToString()
                : strings[KeyFor(method)],
            _ => string.Empty,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");

    /// <summary>
    /// The resource key that carries the label of a kind of sales document.
    /// </summary>
    /// <param name="kind">The kind of document.</param>
    /// <returns>The resource key.</returns>
    public static string KeyFor(SalesDocumentKind kind) => kind switch
    {
        SalesDocumentKind.Invoice => StringKeys.SalesKindInvoice,
        SalesDocumentKind.Return => StringKeys.SalesKindReturn,
        SalesDocumentKind.Reversal => StringKeys.SalesKindReversal,
        _ => StringKeys.SalesKindInvoice,
    };

    /// <summary>
    /// The resource key that carries the name of a way of paying.
    /// </summary>
    /// <param name="method">The way of paying.</param>
    /// <returns>The resource key.</returns>
    public static string KeyFor(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => StringKeys.PosCash,
        PaymentMethod.Card => StringKeys.PosCard,
        PaymentMethod.Other => StringKeys.PosOther,
        _ => StringKeys.PosOther,
    };
}
