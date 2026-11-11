using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>One row of the import preview, as the owner sees it.</summary>
/// <param name="Line">The line in the file.</param>
/// <param name="Action">What the row will do: create, update or refused.</param>
/// <param name="Sku">The stock-keeping unit code.</param>
/// <param name="Name">The product name.</param>
/// <param name="Category">The category the row names.</param>
/// <param name="Unit">The unit the row names.</param>
/// <param name="SalePrice">The selling price the row carries.</param>
/// <param name="Problems">The problems found in the row.</param>
public sealed record ImportRowItem(
    int Line,
    string Action,
    string Sku,
    string Name,
    string Category,
    string Unit,
    string SalePrice,
    string Problems);

/// <summary>
/// The CSV import window. The file is read and checked before anything is written, so the owner sees
/// exactly what an import would do, row by row (PRD PRD-06).
/// </summary>
public sealed partial class CsvImportViewModel : ObservableObject
{
    private readonly IQueryHandler<PreviewProductImportQuery, Result<ProductImportPreview>> _previewHandler;
    private readonly ICommandHandler<ImportProductsCommand, Result<ImportSummary>> _importHandler;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    /// <summary>
    /// The read of the file that is on its way. It is cancelled when the file is read again, so
    /// the rows on screen are always the rows of the file the owner has chosen.
    /// </summary>
    private CancellationTokenSource? _previewCancellation;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _updateExisting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _wasImported;

    /// <summary>Raised when the window should close. The argument is true when something was imported.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>Creates the window's view model.</summary>
    /// <param name="previewHandler">Reads and checks the file.</param>
    /// <param name="importHandler">Writes the products.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public CsvImportViewModel(
        IQueryHandler<PreviewProductImportQuery, Result<ProductImportPreview>> previewHandler,
        ICommandHandler<ImportProductsCommand, Result<ImportSummary>> importHandler,
        IStrings strings,
        LanguageService languageService)
    {
        _previewHandler = previewHandler;
        _importHandler = importHandler;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The rows of the file, as they were read.</summary>
    public ObservableCollection<ImportRowItem> Rows { get; } = [];

    /// <summary>The window title.</summary>
    public string Title => _strings[StringKeys.CsvTitle];

    /// <summary>The label on the choose-file command.</summary>
    public string ChooseFileText => _strings[StringKeys.CsvChooseFile];

    /// <summary>The label of the file field.</summary>
    public string FileLabel => _strings[StringKeys.CsvFile];

    /// <summary>The label of the mode question.</summary>
    public string ModeLabel => _strings[StringKeys.CsvMode];

    /// <summary>The label of the create-only choice.</summary>
    public string ModeCreateOnlyText => _strings[StringKeys.CsvModeCreateOnly];

    /// <summary>The label of the update choice.</summary>
    public string ModeUpsertText => _strings[StringKeys.CsvModeUpsert];

    /// <summary>The label on the import command.</summary>
    public string ImportText => _strings[StringKeys.CommonImport];

    /// <summary>The label on the cancel command.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>The label of the column that holds the line number.</summary>
    public string LineHeader => _strings[StringKeys.CommonLine];

    /// <summary>The label of the column that holds the action.</summary>
    public string ActionHeader => _strings[StringKeys.CsvAction];

    /// <summary>The label of the column that holds the code.</summary>
    public string SkuHeader => _strings[StringKeys.CsvSku];

    /// <summary>The label of the column that holds the name.</summary>
    public string NameHeader => _strings[StringKeys.CsvName];

    /// <summary>The label of the column that holds the category.</summary>
    public string CategoryHeader => _strings[StringKeys.CsvCategory];

    /// <summary>The label of the column that holds the unit.</summary>
    public string UnitHeader => _strings[StringKeys.CsvUnit];

    /// <summary>The label of the column that holds the selling price.</summary>
    public string SalePriceHeader => _strings[StringKeys.CsvSalePrice];

    /// <summary>The label of the column that holds the problems.</summary>
    public string ProblemsHeader => _strings[StringKeys.CommonProblems];

    /// <summary>The label of the action that creates a product.</summary>
    public string ActionCreateText => _strings[StringKeys.CsvActionCreate];

    /// <summary>The label of the action that updates a product.</summary>
    public string ActionUpdateText => _strings[StringKeys.CsvActionUpdate];

    /// <summary>The label of the action that refuses a row.</summary>
    public string ActionInvalidText => _strings[StringKeys.CsvActionInvalid];

    /// <summary>The text shown when the file has no rows at all.</summary>
    public string EmptyText => _strings[StringKeys.CsvEmpty];

    /// <summary>True when a file has been read.</summary>
    public bool HasPreview => Rows.Count > 0;

    /// <summary>True when there is something worth importing.</summary>
    public bool CanImport => Rows.Any(row => row.Action == ActionCreateText || row.Action == ActionUpdateText);

    /// <summary>The line that says how many rows are ready and how many have problems.</summary>
    public string SummaryText
    {
        get
        {
            int ready = Rows.Count(row => row.Action == ActionCreateText || row.Action == ActionUpdateText);
            int problems = Rows.Count - ready;

            return _strings.Format(StringKeys.CsvReady, ready, problems);
        }
    }

    /// <summary>Chooses the file to import.</summary>
    [RelayCommand]
    private async Task ChooseFileAsync(CancellationToken cancellationToken)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        FilePath = dialog.FileName;

        await RefreshPreviewAsync(cancellationToken);
    }

    /// <summary>Reads the file again, for instance after the mode changed.</summary>
    [RelayCommand]
    public async Task RefreshPreviewAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        // A read that is already on its way is cancelled, because the newest mode is the one the
        // owner means. The old source is not disposed: the read it belongs to may still be handing
        // the token to the database.
        _previewCancellation?.Cancel();
        _previewCancellation = new CancellationTokenSource();

        CancellationToken token = _previewCancellation.Token;

        IsBusy = true;

        try
        {
            Result<ProductImportPreview> result = await _previewHandler.HandleAsync(
                new PreviewProductImportQuery(
                    FilePath,
                    UpdateExisting ? ProductImportMode.UpsertBySku : ProductImportMode.CreateOnly,
                    CatalogHeaders.Build(_strings)),
                token);

            token.ThrowIfCancellationRequested();

            if (result.IsFailure)
            {
                StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
                Rows.Clear();
                return;
            }

            ApplyPreview(result.Value);
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another read of the file has taken over; this answer is stale and is dropped.
        }
#pragma warning disable CA1031 // A read that fails must not take the screen down with it.
        catch (Exception ex)
        {
            // A read that fails outright must not lose its exception in the void. The log keeps the
            // detail and the owner is told that the file could not be read.
            StartupDiagnostics.Write("The import file could not be read.", ex);

            if (!token.IsCancellationRequested)
            {
                StatusMessage = _strings[StringKeys.ErrorUnexpected];
            }
        }
#pragma warning restore CA1031
        finally
        {
            // A read that has been overtaken leaves the busy flag to the read that took over.
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }

            RaiseSummaryChanged();
        }
    }

    /// <summary>Imports the rows that are ready.</summary>
    [RelayCommand]
    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        IsBusy = true;

        try
        {
            Result<ImportSummary> result = await _importHandler.HandleAsync(
                new ImportProductsCommand(
                    FilePath,
                    UpdateExisting ? ProductImportMode.UpsertBySku : ProductImportMode.CreateOnly,
                    CatalogHeaders.Build(_strings)),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            StatusMessage = _strings.Format(
                StringKeys.ProductsExported,
                result.Value.Created + result.Value.Updated);

            WasImported = true;
            CloseRequested?.Invoke(this, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Closes the window without importing.</summary>
    [RelayCommand]
    private void Cancel()
    {
        WasImported = false;
        CloseRequested?.Invoke(this, false);
    }

    private void ApplyPreview(ProductImportPreview preview)
    {
        Rows.Clear();

        foreach (ProductImportRow row in preview.Rows)
        {
            string action = !row.IsValid
                ? ActionInvalidText
                : row.IsUpdate ? ActionUpdateText : ActionCreateText;

            string problems = string.Join(
                " ",
                row.Issues.Select(Describe));

            Rows.Add(new ImportRowItem(
                row.LineNumber,
                action,
                row.Raw.TryGetValue(ProductCsvMapper.FieldSku, out string? sku) ? sku : string.Empty,
                row.Raw.TryGetValue(ProductCsvMapper.FieldName, out string? name) ? name : string.Empty,
                row.Raw.TryGetValue(ProductCsvMapper.FieldCategory, out string? category) ? category : string.Empty,
                row.Raw.TryGetValue(ProductCsvMapper.FieldUnit, out string? unit) ? unit : string.Empty,
                row.Raw.TryGetValue(ProductCsvMapper.FieldSalePrice, out string? price) ? price : string.Empty,
                problems));
        }

        RaiseSummaryChanged();
    }

    /// <summary>
    /// Builds the message for one problem in the row, in the language the interface is in.
    /// </summary>
    /// <param name="issue">The problem the mapper found.</param>
    /// <returns>The message the owner reads.</returns>
    /// <remarks>
    /// The mapper answers with a code and the value that caused the problem. The interface builds
    /// its own sentence from the code, so an Arabic owner reads an Arabic sentence and sees the
    /// offending value rather than a description of it.
    /// </remarks>
    private string Describe(ImportIssue issue) => issue.Code switch
    {
        ProductCsvMapper.UnknownCategoryCode
            or ProductCsvMapper.UnknownUnitCode
            or ProductCsvMapper.UnknownTaxCategoryCode
            => CatalogErrorMessages.Resolve(_strings, issue.Code, issue.Value ?? string.Empty),

        ProductCsvMapper.SkuExistsCode
            or ProductCsvMapper.BarcodeInUseCode
            => CatalogErrorMessages.Resolve(_strings, issue.Code, Identifier(issue.Value)),

        ProductCsvMapper.DuplicateSkuInFileCode
            or ProductCsvMapper.DuplicateBarcodeInFileCode
            => CatalogErrorMessages.Resolve(_strings, issue.Code, Identifier(issue.Value), issue.Line),

        _ => CatalogErrorMessages.Resolve(_strings, issue.Code, FieldName(issue.Field)),
    };

    /// <summary>
    /// The name of the field the problem is in, in the language the interface is in.
    /// </summary>
    /// <param name="field">The field's technical name.</param>
    /// <returns>The name the owner reads.</returns>
    private string FieldName(string field) => field switch
    {
        ProductCsvMapper.FieldSku => _strings[StringKeys.CsvSku],
        ProductCsvMapper.FieldName => _strings[StringKeys.CsvName],
        ProductCsvMapper.FieldCategory => _strings[StringKeys.CsvCategory],
        ProductCsvMapper.FieldUnit => _strings[StringKeys.CsvUnit],
        ProductCsvMapper.FieldTaxCategory => _strings[StringKeys.ProductEditorTaxCategory],
        ProductCsvMapper.FieldSalePrice => _strings[StringKeys.CsvSalePrice],
        ProductCsvMapper.FieldCostPrice => _strings[StringKeys.ProductEditorCostPrice],
        ProductCsvMapper.FieldReorderLevel => _strings[StringKeys.ProductEditorReorderLevel],
        ProductCsvMapper.FieldBarcode => _strings[StringKeys.ProductEditorBarcode],
        ProductCsvMapper.FieldAdditionalBarcodes => _strings[StringKeys.ProductEditorAdditionalBarcodes],
        ProductCsvMapper.FieldIsActive => _strings[StringKeys.CommonStatus],
        _ => field,
    };

    /// <summary>
    /// An identifier, kept from disturbing the direction of the words around it.
    /// </summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The identifier, isolated when the interface is in Arabic.</returns>
    private string Identifier(string? value) =>
        _languageService.CurrentLanguage.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            ? BidiText.Isolate(value)
            : value ?? string.Empty;

    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(SummaryText));
    }

    partial void OnUpdateExistingChanged(bool value) => SchedulePreviewLoad();

    /// <summary>
    /// Reads the file again, and forgets the read that was waiting, so the rows on screen are the
    /// rows of the mode the owner has just chosen.
    /// </summary>
    private void SchedulePreviewLoad() => _ = RefreshPreviewAsync(CancellationToken.None);

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(string.Empty);
}
