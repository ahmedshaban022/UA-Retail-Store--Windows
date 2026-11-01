using StoreFlow.Application.Pos;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Turns the stable error codes the point of sale returns into the text the cashier reads.
/// </summary>
/// <remarks>
/// The handlers answer with a code and an English fallback message. The interface shows the
/// localised message built from the same code, so an Arabic cashier is never shown an English
/// sentence the rest of the screen does not speak.
/// </remarks>
public static class PosErrorMessages
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
        PosErrorCodes.EmptyCart => StringKeys.ErrorPosEmptyCart,
        PosErrorCodes.ProductNotFound => StringKeys.ErrorPosProductNotFound,
        PosErrorCodes.ProductInactive => StringKeys.ErrorPosProductInactive,
        PosErrorCodes.DuplicateProduct => StringKeys.ErrorPosDuplicateProduct,
        PosErrorCodes.InvalidQuantity => StringKeys.ErrorPosInvalidQuantity,
        PosErrorCodes.InvalidUnitPrice => StringKeys.ErrorPosInvalidUnitPrice,
        PosErrorCodes.InsufficientStock => StringKeys.ErrorPosInsufficientStock,
        PosErrorCodes.NegativeTotal => StringKeys.ErrorPosNegativeTotal,
        PosErrorCodes.DiscountOverLimit => StringKeys.ErrorPosDiscountOverLimit,
        PosErrorCodes.PriceOverrideNotAllowed => StringKeys.ErrorPosPriceOverride,
        PosErrorCodes.InsufficientPayment => StringKeys.ErrorPosInsufficientPayment,
        PosErrorCodes.InsufficientCash => StringKeys.ErrorPosInsufficientCash,
        PosErrorCodes.IdempotencyConflict => StringKeys.ErrorPosIdempotencyConflict,
        PosErrorCodes.HeldCartNotFound => StringKeys.ErrorPosHeldCartNotFound,
        PosErrorCodes.HeldCartLabelRequired => StringKeys.ErrorPosHeldCartLabelRequired,
        _ => StringKeys.ErrorUnexpected,
    };
}
