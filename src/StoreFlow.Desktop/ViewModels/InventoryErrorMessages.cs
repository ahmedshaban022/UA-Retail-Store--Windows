using StoreFlow.Application.Inventory;
using StoreFlow.Desktop.Localization;
namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Turns the stable error codes the inventory layer returns into the text the owner reads.
/// </summary>
/// <remarks>
/// The handlers answer with a code and an English fallback message. The interface shows the
/// localised message built from the same code, so an Arabic user is never shown an English
/// sentence the rest of the screen does not speak.
/// </remarks>
public static class InventoryErrorMessages
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
        InventoryValidation.RequiredCode => StringKeys.ErrorInventoryRequired,
        InventoryValidation.TooLongCode => StringKeys.ErrorTooLong,
        InventoryValidation.NegativePriceCode => StringKeys.ErrorNegativePrice,
        InventoryValidation.NegativeQuantityCode => StringKeys.ErrorNegativeQuantity,
        InventoryValidation.NonPositiveQuantityCode => StringKeys.ErrorInventoryQuantity,
        InventoryValidation.QuantityTooPreciseCode => StringKeys.ErrorInventoryQuantity,
        InventoryValidation.NoLinesCode => StringKeys.ErrorInventoryNoLines,
        InventoryValidation.DuplicateProductCode => StringKeys.ErrorInventoryDuplicateProduct,
        InventoryErrorCodes.NegativeStock => StringKeys.ErrorInventoryNegativeStock,
        InventoryErrorCodes.NegativeDamaged => StringKeys.ErrorInventoryNegativeDamaged,
        InventoryErrorCodes.NotFound => StringKeys.ErrorNotFound,
        InventoryErrorCodes.NotDraft => StringKeys.ErrorInventoryNotDraft,
        InventoryErrorCodes.NotPosted => StringKeys.ErrorInventoryNotPosted,
        InventoryErrorCodes.AlreadyReversed => StringKeys.ErrorInventoryAlreadyReversed,
        InventoryErrorCodes.InvalidReference => StringKeys.ErrorInvalidReference,
        InventoryErrorCodes.InvalidDiscount => StringKeys.ErrorInventoryDiscount,
        InventoryErrorCodes.EmptyDocument => StringKeys.ErrorInventoryNoLines,
        InventoryErrorCodes.EmptyCount => StringKeys.ErrorInventoryEmptyCount,
        InventoryErrorCodes.AlreadyStocked => StringKeys.ErrorInventoryAlreadyStocked,
        InventoryErrorCodes.NotAwaitingApproval => StringKeys.ErrorInventoryAwaitingApproval,
        InventoryErrorCodes.NotApproved => StringKeys.ErrorInventoryNotApproved,
        InventoryErrorCodes.AlreadyPosted => StringKeys.ErrorInventoryAlreadyPosted,
        InventoryErrorCodes.ApprovalNotFound => StringKeys.ErrorNotFound,
        InventoryErrorCodes.ApprovalNotPending => StringKeys.ErrorInventoryNotPending,
        InventoryErrorCodes.SelfApproval => StringKeys.ErrorInventorySelfApproval,
        InventoryErrorCodes.NoActor => StringKeys.ErrorInventoryNoActor,
        InventoryErrorCodes.NumberClash => StringKeys.ErrorInventoryNumberClash,
        NotAuthorizedCode => StringKeys.ErrorNotAuthorized,
        _ => StringKeys.ErrorUnexpected,
    };

    /// <summary>The error code returned when the signed-in user may not do what they asked.</summary>
    public const string NotAuthorizedCode = "auth.not_authorized";
}
