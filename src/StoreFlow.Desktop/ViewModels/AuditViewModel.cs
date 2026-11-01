using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>One choice in the audit trail's type filter.</summary>
/// <param name="Key">The entity type the filter matches; empty for every type.</param>
/// <param name="DisplayName">The name to show.</param>
public sealed record AuditEntityTypeOption(string Key, string DisplayName)
{
    /// <summary>
    /// The name a select shows for this entry.
    /// </summary>
    /// <remarks>
    /// A select shows the selected item's own text. Without this override the closed control would
    /// show the record's generated <c>ToString</c>, which reads as
    /// <c>AuditEntityTypeOption { Key = , DisplayName = All }</c> instead of the name of the choice.
    /// </remarks>
    public override string ToString() => DisplayName;
}

/// <summary>
/// The audit trail screen. It reads what the application has recorded and nothing else: the trail
/// is append-only, and no screen in the application edits or removes an entry.
/// </summary>
public sealed partial class AuditViewModel : ObservableObject
{
    /// <summary>How long the search waits before it asks the database, in milliseconds.</summary>
    private const int SearchDelayMilliseconds = 250;

    private readonly IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryView>>> _query;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    private CancellationTokenSource? _searchCancellation;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AuditEntityTypeOption? _selectedEntityType;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the screen.</summary>
    /// <param name="query">Reads the audit trail.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public AuditViewModel(
        IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryView>>> query,
        IStrings strings,
        LanguageService languageService)
    {
        _query = query;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;

        BuildEntityTypes();
        SelectedEntityType = EntityTypes[0];
    }

    /// <summary>The entries on screen, newest first.</summary>
    public ObservableCollection<AuditEntryView> Entries { get; } = [];

    /// <summary>The types the trail can be filtered by.</summary>
    public ObservableCollection<AuditEntityTypeOption> EntityTypes { get; } = [];

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.AuditTitle];

    /// <summary>The text shown inside the empty search box.</summary>
    public string SearchPlaceholder => _strings[StringKeys.AuditSearch];

    /// <summary>True when the search box holds nothing, so the placeholder should show.</summary>
    public bool HasNoSearchText => string.IsNullOrEmpty(SearchText);

    /// <summary>The label of the column that holds the time.</summary>
    public string WhenHeader => _strings[StringKeys.AuditWhen];

    /// <summary>The label of the column that holds the user.</summary>
    public string WhoHeader => _strings[StringKeys.AuditWho];

    /// <summary>The label of the column that holds the action.</summary>
    public string ActionHeader => _strings[StringKeys.AuditAction];

    /// <summary>The label of the column that holds the record.</summary>
    public string EntityHeader => _strings[StringKeys.AuditEntityType];

    /// <summary>The label of the column that holds the summary.</summary>
    public string SummaryHeader => _strings[StringKeys.AuditSummary];

    /// <summary>The label of the type filter.</summary>
    public string EntityTypeLabel => _strings[StringKeys.AuditEntityType];

    /// <summary>The text shown when nothing has been recorded yet.</summary>
    public string EmptyText => _strings[StringKeys.AuditEmpty];

    /// <summary>True when the trail holds nothing.</summary>
    public bool IsEmpty => !IsBusy && Entries.Count == 0;

    /// <summary>Loads the trail.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result<IReadOnlyList<AuditEntryView>> result = await _query.HandleAsync(
                new ListAuditEntriesQuery(SelectedEntityType?.Key, SearchText, 500),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
                return;
            }

            Entries.Clear();

            foreach (AuditEntryView entry in result.Value)
            {
                Entries.Add(entry);
            }

            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasNoSearchText));

        ScheduleRefresh();
    }

    partial void OnSelectedEntityTypeChanged(AuditEntityTypeOption? value) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();

        CancellationToken token = _searchCancellation.Token;

        _ = DebounceAsync(token);
    }

    private async Task DebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDelayMilliseconds, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    private void BuildEntityTypes()
    {
        EntityTypes.Clear();
        EntityTypes.Add(new AuditEntityTypeOption(string.Empty, _strings[StringKeys.CommonAll]));
        EntityTypes.Add(new AuditEntityTypeOption("Product", _strings[StringKeys.ProductsTitle]));
        EntityTypes.Add(new AuditEntityTypeOption("Category", _strings[StringKeys.CategoriesTitle]));
        EntityTypes.Add(new AuditEntityTypeOption("Unit", _strings[StringKeys.UnitsTitle]));
        EntityTypes.Add(new AuditEntityTypeOption("Supplier", _strings[StringKeys.SuppliersTitle]));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        BuildEntityTypes();
        SelectedEntityType = EntityTypes[0];
        OnPropertyChanged(string.Empty);
    }
}
