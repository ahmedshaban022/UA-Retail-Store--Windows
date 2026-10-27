using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The categories screen. A category groups products; it is never removed from under a product, so
/// the screen deactivates a category instead of deleting it.
/// </summary>
public sealed partial class CategoriesViewModel : ObservableObject
{
    private readonly IQueryHandler<ListCategoriesQuery, Result<IReadOnlyList<CategorySummary>>> _query;
    private readonly ICommandHandler<SetCategoryActiveCommand, Result> _setActiveHandler;
    private readonly ICatalogDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private CategorySummary? _selectedCategory;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the screen.</summary>
    /// <param name="query">Reads the categories.</param>
    /// <param name="setActiveHandler">Activates and deactivates categories.</param>
    /// <param name="dialogs">Shows the catalogue's dialogs.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public CategoriesViewModel(
        IQueryHandler<ListCategoriesQuery, Result<IReadOnlyList<CategorySummary>>> query,
        ICommandHandler<SetCategoryActiveCommand, Result> setActiveHandler,
        ICatalogDialogs dialogs,
        IStrings strings,
        LanguageService languageService)
    {
        _query = query;
        _setActiveHandler = setActiveHandler;
        _dialogs = dialogs;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The categories on screen.</summary>
    public ObservableCollection<CategorySummary> Categories { get; } = [];

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.CategoriesTitle];

    /// <summary>The label on the new-category command.</summary>
    public string NewText => _strings[StringKeys.CategoriesNew];

    /// <summary>The label on the edit command.</summary>
    public string EditText => _strings[StringKeys.CommonEdit];

    /// <summary>The label on the activate command.</summary>
    public string ActivateText => _strings[StringKeys.CommonActivate];

    /// <summary>The label on the deactivate command.</summary>
    public string DeactivateText => _strings[StringKeys.CommonDeactivate];

    /// <summary>The label on the show-inactive switch.</summary>
    public string ShowInactiveText => _strings[StringKeys.CommonShowInactive];

    /// <summary>The label of the column that holds the name.</summary>
    public string NameHeader => _strings[StringKeys.CategoriesName];

    /// <summary>The label of the column that holds the Arabic name.</summary>
    public string NameArHeader => _strings[StringKeys.CategoriesNameAr];

    /// <summary>The label of the column that counts the products.</summary>
    public string ProductsHeader => _strings[StringKeys.CategoriesProducts];

    /// <summary>The label of the column that holds the state.</summary>
    public string StatusHeader => _strings[StringKeys.CommonStatus];

    /// <summary>The text shown when the shop has no categories yet.</summary>
    public string EmptyText => _strings[StringKeys.CategoriesEmpty];

    /// <summary>The label of the state badge when the category is active.</summary>
    public string ActiveText => _strings[StringKeys.ProductsActive];

    /// <summary>The label of the state badge when the category is inactive.</summary>
    public string InactiveText => _strings[StringKeys.ProductsInactive];

    /// <summary>True when the shop has no categories yet.</summary>
    public bool IsEmpty => !IsBusy && Categories.Count == 0;

    /// <summary>True when a category is selected and can be opened.</summary>
    public bool CanEditSelected => SelectedCategory is not null;

    /// <summary>Loads the categories.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result<IReadOnlyList<CategorySummary>> result = await _query.HandleAsync(
                new ListCategoriesQuery(IncludeInactive),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            Categories.Clear();

            foreach (CategorySummary category in result.Value)
            {
                Categories.Add(category);
            }

            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Creates a new category.</summary>
    [RelayCommand]
    private async Task NewAsync(CancellationToken cancellationToken)
    {
        bool saved = await _dialogs.ShowReferenceEditorAsync(CatalogReferenceKind.Category, null, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Opens the category that is selected.</summary>
    [RelayCommand]
    private async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedCategory is not { } category)
        {
            return;
        }

        bool saved = await _dialogs.ShowReferenceEditorAsync(CatalogReferenceKind.Category, category.Id, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Activates or deactivates the category that is selected.</summary>
    [RelayCommand]
    private async Task ToggleActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedCategory is not { } category)
        {
            return;
        }

        bool target = !category.IsActive;

        Result result = await _setActiveHandler.HandleAsync(
            new SetCategoryActiveCommand(category.Id, target),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        StatusMessage = target
            ? _strings.Format(StringKeys.ProductsActivated, category.Name)
            : _strings.Format(StringKeys.ProductsDeactivated, category.Name);

        await RefreshAsync(cancellationToken);
    }

    partial void OnSelectedCategoryChanged(CategorySummary? value) =>
        OnPropertyChanged(nameof(CanEditSelected));

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(string.Empty);
}
