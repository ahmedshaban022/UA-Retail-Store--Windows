using System.IO;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// The screens that read in the background: an answer that has been overtaken must never land on
/// the screen, because the owner has already asked for something else.
/// </summary>
public sealed class StaleReadTests
{
    [Fact]
    public async Task A_read_that_has_been_overtaken_does_not_land_on_the_screen()
    {
        // The owner reads the file, changes the mode, and the file is read again. The first read is
        // slower than the second, so its answer arrives last. It is stale: the rows on screen are
        // the rows of the mode the owner chose, not the ones they chose before it.
        var handler = new SlowFirstPreviewHandler();

        var viewModel = new CsvImportViewModel(
            handler,
            new UnusedImportHandler(),
            Strings(),
            new LanguageService());

        viewModel.FilePath = "products.csv";

        Task first = viewModel.RefreshPreviewAsync(CancellationToken.None);

        // The first read is on its way. The owner changes the mode, and the file is read again.
        viewModel.UpdateExisting = true;

        await WaitForAsync(() => viewModel.Rows.Count == 1 && viewModel.Rows[0].Sku == "NEW");

        // The first answer arrives now, and it is the one that is out of date.
        handler.Release();

        await first;

        viewModel.Rows.ShouldHaveSingleItem();
        viewModel.Rows[0].Sku.ShouldBe("NEW");
    }

    [Fact]
    public async Task A_read_that_fails_is_reported_on_the_screen_and_not_thrown()
    {
        // A file that cannot be read is a thing the owner can be told about. An exception that
        // escapes into the void is not: the screen would simply stop changing, and nothing would
        // say why.
        var viewModel = new CsvImportViewModel(
            new FailingPreviewHandler(),
            new UnusedImportHandler(),
            Strings(),
            new LanguageService());

        viewModel.FilePath = "products.csv";

        await viewModel.RefreshPreviewAsync(CancellationToken.None);

        viewModel.StatusMessage.ShouldNotBeNullOrWhiteSpace();
        viewModel.Rows.ShouldBeEmpty();
    }

    /// <summary>Waits for a condition to hold.</summary>
    /// <param name="condition">The condition.</param>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        condition().ShouldBeTrue("the screen never caught up with the newest read");
    }

    /// <summary>The strings of the application, in English.</summary>
    /// <returns>The strings.</returns>
    private static IStrings Strings()
    {
        var language = new LanguageService();

        language.SetLanguage("en");

        return new Strings(language);
    }

    /// <summary>A preview whose first read is slow and whose second is quick.</summary>
    private sealed class SlowFirstPreviewHandler
        : IQueryHandler<PreviewProductImportQuery, Result<ProductImportPreview>>
    {
        private readonly TaskCompletionSource _gate = new();
        private int _calls;

        /// <summary>Let the slow read finish.</summary>
        public void Release() => _gate.SetResult();

        /// <inheritdoc />
        public async Task<Result<ProductImportPreview>> HandleAsync(
            PreviewProductImportQuery query,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _calls);

            if (call == 1)
            {
                // The first read is slow. It ignores the token on purpose: a file that is read
                // after the owner has moved on is exactly the case the screen has to survive.
                await _gate.Task;
            }

            return Result<ProductImportPreview>.Success(Preview(call == 1 ? "OLD" : "NEW"));
        }
    }

    /// <summary>A preview that cannot be read.</summary>
    private sealed class FailingPreviewHandler
        : IQueryHandler<PreviewProductImportQuery, Result<ProductImportPreview>>
    {
        /// <inheritdoc />
        public Task<Result<ProductImportPreview>> HandleAsync(
            PreviewProductImportQuery query,
            CancellationToken cancellationToken) =>
            throw new IOException("The file could not be read.");
    }

    /// <summary>A preview with one row.</summary>
    /// <param name="sku">The code on the row.</param>
    /// <returns>The preview.</returns>
    private static ProductImportPreview Preview(string sku) =>
        new(
            [
                new ProductImportRow(
                    1,
                    new Dictionary<string, string> { [ProductCsvMapper.FieldSku] = sku },
                    [],
                    false,
                    null,
                    null),
            ],
            [],
            0,
            0,
            0,
            0);

    /// <summary>An import that is never asked for.</summary>
    private sealed class UnusedImportHandler : ICommandHandler<ImportProductsCommand, Result<ImportSummary>>
    {
        /// <inheritdoc />
        public Task<Result<ImportSummary>> HandleAsync(
            ImportProductsCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<ImportSummary>.Success(new ImportSummary(0, 0, 0)));
    }
}
