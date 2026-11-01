using System.Globalization;
using System.Resources;
using StoreFlow.Application.Catalog;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// The import preview tells the owner what is wrong with a row. Every problem the mapper can find
/// must have a message of its own in the language the interface is in, and not fall back to the
/// catch-all sentence that says nothing.
/// </summary>
public sealed class ImportProblemMessageTests
{
    private const string ResourceBaseName = "StoreFlow.Desktop.Resources.Strings";

    private static readonly string[] ImportCodes =
    [
        ProductCsvMapper.MissingValueCode,
        ProductCsvMapper.NotANumberCode,
        ProductCsvMapper.NotABooleanCode,
        ProductCsvMapper.UnknownCategoryCode,
        ProductCsvMapper.UnknownUnitCode,
        ProductCsvMapper.UnknownTaxCategoryCode,
        ProductCsvMapper.SkuExistsCode,
        ProductCsvMapper.DuplicateSkuInFileCode,
        ProductCsvMapper.DuplicateBarcodeInFileCode,
        ProductCsvMapper.BarcodeInUseCode,
        ProductCsvMapper.UnknownFieldCode,
    ];

    [Fact]
    public void Every_problem_the_importer_finds_has_a_message_of_its_own()
    {
        foreach (string code in ImportCodes)
        {
            string key = CatalogErrorMessages.KeyFor(code);

            key.ShouldNotBe(StringKeys.ErrorUnexpected, $"the problem {code} has no message of its own");
        }
    }

    [Fact]
    public void Every_problem_the_importer_finds_is_written_in_both_languages()
    {
        var manager = new ResourceManager(ResourceBaseName, typeof(Strings).Assembly);

        foreach (string code in ImportCodes)
        {
            string key = CatalogErrorMessages.KeyFor(code);

            string english = manager.GetString(key, new CultureInfo("en")) ?? string.Empty;
            string arabic = manager.GetString(key, new CultureInfo("ar")) ?? string.Empty;

            english.ShouldNotBeNullOrWhiteSpace($"the message for {code} is missing in English");
            arabic.ShouldNotBeNullOrWhiteSpace($"the message for {code} is missing in Arabic");
        }
    }

    [Fact]
    public void A_problem_that_is_about_a_field_names_that_field()
    {
        var manager = new ResourceManager(ResourceBaseName, typeof(Strings).Assembly);

        string english = manager.GetString(
            CatalogErrorMessages.KeyFor(ProductCsvMapper.MissingValueCode),
            new CultureInfo("en")) ?? string.Empty;

        english.ShouldContain("{0}");
    }

    [Fact]
    public void A_problem_that_is_about_a_value_carries_that_value()
    {
        var manager = new ResourceManager(ResourceBaseName, typeof(Strings).Assembly);

        string english = manager.GetString(
            CatalogErrorMessages.KeyFor(ProductCsvMapper.SkuExistsCode),
            new CultureInfo("en")) ?? string.Empty;

        english.ShouldContain("{0}");
    }
}
