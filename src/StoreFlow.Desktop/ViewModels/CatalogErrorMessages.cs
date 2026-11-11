using StoreFlow.Application.Catalog;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Turns the stable error codes the application layer returns into the text the owner reads.
/// </summary>
/// <remarks>
/// The handlers answer with a code and an English fallback message. The interface shows the
/// localised message built from the same code, so an Arabic user is never shown an English
/// sentence the rest of the screen does not speak.
/// </remarks>
public static class CatalogErrorMessages
{
    /// <summary>
    /// Builds the localised message for an error code.
    /// </summary>
    /// <param name="strings">The application's strings.</param>
    /// <param name="code">The stable error code.</param>
    /// <param name="arguments">The values the message needs, such as the barcode that is taken.</param>
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
        CatalogValidation.RequiredCode => StringKeys.ErrorRequired,
        CatalogValidation.TooLongCode => StringKeys.ErrorTooLong,
        CatalogValidation.NegativePriceCode => StringKeys.ErrorNegativePrice,
        CatalogValidation.NegativeQuantityCode => StringKeys.ErrorNegativeQuantity,
        CatalogValidation.InvalidBarcodeCode => StringKeys.ErrorInvalidBarcode,
        CatalogValidation.DuplicateBarcodeCode => StringKeys.ErrorDuplicateBarcode,
        CatalogValidation.DuplicateSkuCode => StringKeys.ErrorDuplicateSku,
        CatalogValidation.InvalidPrecisionCode => StringKeys.ErrorInvalidPrecision,
        CatalogValidation.UnitLockedCode => StringKeys.ErrorUnitLocked,
        ProductCsvMapper.BarcodeInUseCode => StringKeys.ErrorBarcodeInUse,
        ProductCsvMapper.MissingValueCode => StringKeys.ErrorImportMissingValue,
        ProductCsvMapper.NotANumberCode => StringKeys.ErrorImportNotANumber,
        ProductCsvMapper.NotABooleanCode => StringKeys.ErrorImportNotABoolean,
        ProductCsvMapper.UnknownCategoryCode => StringKeys.ErrorImportUnknownCategory,
        ProductCsvMapper.UnknownUnitCode => StringKeys.ErrorImportUnknownUnit,
        ProductCsvMapper.UnknownTaxCategoryCode => StringKeys.ErrorImportUnknownTaxCategory,
        ProductCsvMapper.SkuExistsCode => StringKeys.ErrorImportSkuExists,
        ProductCsvMapper.DuplicateSkuInFileCode => StringKeys.ErrorImportDuplicateSku,
        ProductCsvMapper.DuplicateBarcodeInFileCode => StringKeys.ErrorImportDuplicateBarcode,
        ProductCsvMapper.UnknownFieldCode => StringKeys.ErrorImportUnknownField,
        DuplicateNameCode => StringKeys.ErrorDuplicateName,
        NotFoundCode => StringKeys.ErrorNotFound,
        InvalidReferenceCode => StringKeys.ErrorInvalidReference,
        PrecisionInUseCode => StringKeys.ErrorPrecisionInUse,
        ImageNotFoundCode => StringKeys.ErrorImageNotFound,
        ImageUnsupportedCode => StringKeys.ErrorImageUnsupported,
        ImageTooLargeCode => StringKeys.ErrorImageTooLarge,
        ImageWriteFailedCode => StringKeys.ErrorImageWriteFailed,
        ImportFileNotReadableCode => StringKeys.ErrorImportFileNotReadable,
        NotAuthorizedCode => StringKeys.ErrorNotAuthorized,
        _ => StringKeys.ErrorUnexpected,
    };

    /// <summary>The error code returned when a record's name is already taken.</summary>
    public const string DuplicateNameCode = "catalog.duplicate_name";

    /// <summary>The error code returned when a record does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <summary>The error code returned when a referenced record does not exist.</summary>
    public const string InvalidReferenceCode = "catalog.invalid_reference";

    /// <summary>The error code returned when a unit's precision cannot change.</summary>
    public const string PrecisionInUseCode = "catalog.precision_in_use";

    /// <summary>The error code returned when a picture is no longer on disk.</summary>
    public const string ImageNotFoundCode = "catalog.image_not_found";

    /// <summary>The error code returned when a file is not a picture the store accepts.</summary>
    public const string ImageUnsupportedCode = "catalog.image_unsupported";

    /// <summary>The error code returned when a picture is larger than the store accepts.</summary>
    public const string ImageTooLargeCode = "catalog.image_too_large";

    /// <summary>The error code returned when a picture could not be written.</summary>
    public const string ImageWriteFailedCode = "catalog.image_write_failed";

    /// <summary>The error code returned when a CSV file cannot be read.</summary>
    public const string ImportFileNotReadableCode = "catalog.import.file_not_readable";

    /// <summary>The error code returned when the signed-in user may not do what they asked.</summary>
    public const string NotAuthorizedCode = "auth.not_authorized";
}
