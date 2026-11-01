using StoreFlow.Application.Sales;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Turns the stable error codes the sales layer returns into the text the owner reads.
/// </summary>
/// <remarks>
/// The handlers answer with a code and an English fallback message. The interface shows the
/// localised message built from the same code, so an Arabic user is never shown an English
/// sentence the rest of the screen does not speak.
/// </remarks>
public static class SalesErrorMessages
{
    /// <summary>
    /// Builds the localised message for an error code.
    /// </summary>
    /// <param name="strings">The application's strings.</param>
    /// <param name="code">The stable error code.</param>
    /// <param name="arguments">The values the message needs.</param>
    /// <returns>The message to show.</returns>
    public static string Resolve(IStrings strings, string code, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(arguments);

        string key = KeyFor(code);

        return arguments.Length == 0
            ? strings[key]
            : strings.Format(key, arguments);
    }

    /// <summary>
    /// The resource key that carries the message for an error code.
    /// </summary>
    /// <param name="code">The stable error code.</param>
    /// <returns>The resource key.</returns>
    public static string KeyFor(string code) => code switch
    {
        SalesErrorCodes.InvoiceNotFound => StringKeys.ErrorSalesInvoiceNotFound,
        SalesErrorCodes.InvoiceNotPosted => StringKeys.ErrorSalesInvoiceNotPosted,
        SalesErrorCodes.InvoiceAlreadyReversed => StringKeys.ErrorSalesInvoiceAlreadyReversed,
        SalesErrorCodes.InvoiceIsReversal => StringKeys.ErrorSalesInvoiceIsReversal,
        SalesErrorCodes.NoLines => StringKeys.ErrorSalesNoLines,
        SalesErrorCodes.LineNotOnInvoice => StringKeys.ErrorSalesLineNotOnInvoice,
        SalesErrorCodes.InvalidQuantity => StringKeys.ErrorSalesInvalidQuantity,
        SalesErrorCodes.QuantityExceedsSold => StringKeys.ErrorSalesQuantityExceedsSold,
        SalesErrorCodes.QuantityExceedsRemaining => StringKeys.ErrorSalesQuantityExceedsRemaining,
        SalesErrorCodes.ReasonRequired => StringKeys.ErrorSalesReasonRequired,
        SalesErrorCodes.NothingToReturn => StringKeys.ErrorSalesNothingToReturn,
        SalesErrorCodes.ReversalNotAllowed => StringKeys.ErrorSalesReversalNotAllowed,
        SalesErrorCodes.RefundTooLarge => StringKeys.ErrorSalesRefundTooLarge,
        SalesErrorCodes.StockNotRestored => StringKeys.ErrorSalesStockNotRestored,
        _ => StringKeys.ErrorUnexpected,
    };
}
