using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The selling units screen. A unit carries its own precision, because a kilogram and a piece are
/// counted differently and the ledger scales a quantity by the unit it is expressed in.
/// </summary>
public sealed partial class UnitsViewModel : ObservableObject
{
    private readonly IQueryHandler<ListUnitsQuery, Result<IReadOnlyList<UnitSummary>>> _query;
    private readonly ICommandHandler<SetUnitActiveCommand, Result> _setActiveHandler;
    private readonly ICatalogDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private UnitSummary? _selectedUnit;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the screen.</summary>
    /// <param name="query">Reads the units.</param>
    /// <param name="setActiveHandler">Activates and deactivates units.</param>
    /// <param name="dialogs">Shows the catalogue's dialogs.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public UnitsViewModel(
        IQueryHandler<ListUnitsQuery, Result<IReadOnlyList<UnitSummary>>> query,
        ICommandHandler<SetUnitActiveCommand, Result> setActiveHandler,
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

    /// <summary>The units on screen.</summary>
    public ObservableCollection<UnitSummary> Units { get; } = [];

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.UnitsTitle];

    /// <summary>The label on the new-unit command.</summary>
    public string NewText => _strings[StringKeys.UnitsNew];

    /// <summary>The label on the edit command.</summary>
    public string EditText => _strings[StringKeys.CommonEdit];

    /// <summary>The label on the activate command.</summary>
    public string ActivateText => _strings[StringKeys.CommonActivate];

    /// <summary>The label on the deactivate command.</summary>
    public string DeactivateText => _strings[StringKeys.CommonDeactivate];

    /// <summary>The label on the show-inactive switch.</summary>
    public string ShowInactiveText => _strings[StringKeys.CommonShowInactive];

    /// <summary>The label of the column that holds the name.</summary>
    public string NameHeader => _strings[StringKeys.UnitsName];

    /// <summary>The label of the column that holds the Arabic name.</summary>
    public string NameArHeader => _strings[StringKeys.UnitsNameAr];

    /// <summary>The label of the column that holds the precision.</summary>
    public string PrecisionHeader => _strings[StringKeys.UnitsPrecision];

    /// <summary>The label of the column that counts the products.</summary>
    public string ProductsHeader => _strings[StringKeys.UnitsProducts];

    /// <summary>The label of the column that holds the state.</summary>
    public string StatusHeader => _strings[StringKeys.CommonStatus];

    /// <summary>The text shown when the shop has no units yet.</summary>
    public string EmptyText => _strings[StringKeys.UnitsEmpty];

    /// <summary>The label of the state badge when the unit is active.</summary>
    public string ActiveText => _strings[StringKeys.ProductsActive];

    /// <summary>The label of the state badge when the unit is inactive.</summary>
    public string InactiveText => _strings[StringKeys.ProductsInactive];

    /// <summary>True when the shop has no units yet.</summary>
    public bool IsEmpty => !IsBusy && Units.Count == 0;

    /// <summary>True when a unit is selected and can be opened.</summary>
    public bool CanEditSelected => SelectedUnit is not null;

    /// <summary>Loads the units.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result<IReadOnlyList<UnitSummary>> result = await _query.HandleAsync(
                new ListUnitsQuery(IncludeInactive),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            Units.Clear();

            foreach (UnitSummary unit in result.Value)
            {
                Units.Add(unit);
            }

            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Creates a new unit.</summary>
    [RelayCommand]
    private async Task NewAsync(CancellationToken cancellationToken)
    {
        bool saved = await _dialogs.ShowReferenceEditorAsync(CatalogReferenceKind.Unit, null, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Opens the unit that is selected.</summary>
    [RelayCommand]
    private async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedUnit is not { } unit)
        {
            return;
        }

        bool saved = await _dialogs.ShowReferenceEditorAsync(CatalogReferenceKind.Unit, unit.Id, cancellationToken);

        if (saved)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>Activates or deactivates the unit that is selected.</summary>
    [RelayCommand]
    private async Task ToggleActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedUnit is not { } unit)
        {
            return;
        }

        bool target = !unit.IsActive;

        Result result = await _setActiveHandler.HandleAsync(
            new SetUnitActiveCommand(unit.Id, target),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        StatusMessage = target
            ? _strings.Format(StringKeys.ProductsActivated, unit.Name)
            : _strings.Format(StringKeys.ProductsDeactivated, unit.Name);

        await RefreshAsync(cancellationToken);
    }

    partial void OnSelectedUnitChanged(UnitSummary? value) =>
        OnPropertyChanged(nameof(CanEditSelected));

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(string.Empty);
}
