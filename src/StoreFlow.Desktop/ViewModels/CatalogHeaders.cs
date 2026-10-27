using StoreFlow.Application.Catalog;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The headings used for the catalogue's CSV file. The export writes them and the import accepts
/// them, so a file the application produced can be read straight back.
/// </summary>
public static class CatalogHeaders
{
    /// <summary>Builds the heading of every field the catalogue's file carries.</summary>
    /// <param name="strings">The application's strings.</param>
    /// <returns>The heading of each field, keyed by its canonical field name.</returns>
    public static IReadOnlyDictionary<string, string> Build(IStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return new Dictionary<string, string>
        {
            [ProductCsvMapper.FieldSku] = strings[StringKeys.ProductsSku],
            [ProductCsvMapper.FieldName] = strings[StringKeys.ProductsName],
            [ProductCsvMapper.FieldNameAr] = strings[StringKeys.ProductsNameAr],
            [ProductCsvMapper.FieldCategory] = strings[StringKeys.ProductsCategory],
            [ProductCsvMapper.FieldUnit] = strings[StringKeys.ProductsUnit],
            [ProductCsvMapper.FieldTaxCategory] = strings[StringKeys.ProductEditorTaxCategory],
            [ProductCsvMapper.FieldSalePrice] = strings[StringKeys.ProductsSalePrice],
            [ProductCsvMapper.FieldCostPrice] = strings[StringKeys.ProductsCostPrice],
            [ProductCsvMapper.FieldReorderLevel] = strings[StringKeys.ProductEditorReorderLevel],
            [ProductCsvMapper.FieldBarcode] = strings[StringKeys.ProductsBarcode],
            [ProductCsvMapper.FieldAdditionalBarcodes] = strings[StringKeys.ProductEditorAdditionalBarcodes],
            [ProductCsvMapper.FieldDescription] = strings[StringKeys.ProductEditorDescription],
            [ProductCsvMapper.FieldIsActive] = strings[StringKeys.CommonStatus],
        };
    }
}
