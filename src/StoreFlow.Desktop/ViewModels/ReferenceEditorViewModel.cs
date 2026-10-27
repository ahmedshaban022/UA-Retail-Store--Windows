using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Quantity;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>How a field in the reference editor is entered.</summary>
public enum EditorFieldKind
{
    /// <summary>A single line of text.</summary>
    Text = 0,

    /// <summary>Several lines of text.</summary>
    Multiline = 1,

    /// <summary>A choice from a fixed list.</summary>
    Choice = 2,
}

/// <summary>
/// One choice offered by a field of the reference editor.
/// </summary>
/// <param name="Id">The value that is written into the field when the choice is taken.</param>
/// <param name="DisplayName">The name shown in the list.</param>
/// <remarks>
/// The key is text on purpose. WPF converts the bound value to the type of the item's key before
/// it matches the two together, so a field whose value is text must be matched against text keys.
/// Keying the choices by number made an empty value throw while the window was being built, which
/// took the whole editor down with it.
/// </remarks>
public sealed record EditorChoice(string Id, string DisplayName)
{
    /// <inheritdoc />
    public override string ToString() => DisplayName;
}

/// <summary>One field of the reference editor.</summary>
public sealed partial class EditorField : ObservableObject
{
    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    /// <summary>Creates a field.</summary>
    /// <param name="key">The stable key of the field.</param>
    /// <param name="label">The label shown above the field.</param>
    /// <param name="kind">How the field is entered.</param>
    /// <param name="options">The choices, when the field is a choice.</param>
    public EditorField(string key, string label, EditorFieldKind kind, IReadOnlyList<EditorChoice>? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Key = key;
        Label = label;
        Kind = kind;
        Options = options ?? [];
    }

    /// <summary>The stable key of the field.</summary>
    public string Key { get; }

    /// <summary>The label shown above the field.</summary>
    public string Label { get; }

    /// <summary>How the field is entered.</summary>
    public EditorFieldKind Kind { get; }

    /// <summary>The choices, when the field is a choice.</summary>
    public IReadOnlyList<EditorChoice> Options { get; }

    /// <summary>The automation identifier of the field's input, so a test can name it.</summary>
    public string AutomationId => $"ReferenceEditor.{Key}";

    /// <summary>The automation identifier of the field's error message.</summary>
    public string ErrorAutomationId => $"ReferenceEditor.{Key}.Error";
}

/// <summary>
/// The editor for the catalogue's reference records — categories, units and suppliers. The three
/// share one screen because they share one shape: a name, an optional Arabic name, and nothing else
/// that needs explaining.
/// </summary>
public sealed partial class ReferenceEditorViewModel : ObservableObject
{
    private readonly IQueryHandler<ListCategoriesQuery, Result<IReadOnlyList<CategorySummary>>> _categories;
    private readonly IQueryHandler<ListUnitsQuery, Result<IReadOnlyList<UnitSummary>>> _units;
    private readonly IQueryHandler<ListSuppliersQuery, Result<IReadOnlyList<SupplierSummary>>> _suppliers;
    private readonly ICommandHandler<SaveCategoryCommand, Result<int>> _saveCategory;
    private readonly ICommandHandler<SaveUnitCommand, Result<int>> _saveUnit;
    private readonly ICommandHandler<SaveSupplierCommand, Result<int>> _saveSupplier;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    /// <summary>The precision a new unit starts at: whole units.</summary>
    private const string DefaultPrecision = "0";

    private bool _isActive = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _wasSaved;

    /// <summary>Raised when the editor should close. The argument is true when the record was saved.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>Creates the editor.</summary>
    /// <param name="kind">The kind of record being edited.</param>
    /// <param name="categories">Reads the categories.</param>
    /// <param name="units">Reads the units.</param>
    /// <param name="suppliers">Reads the suppliers.</param>
    /// <param name="saveCategory">Saves a category.</param>
    /// <param name="saveUnit">Saves a unit.</param>
    /// <param name="saveSupplier">Saves a supplier.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public ReferenceEditorViewModel(
        CatalogReferenceKind kind,
        IQueryHandler<ListCategoriesQuery, Result<IReadOnlyList<CategorySummary>>> categories,
        IQueryHandler<ListUnitsQuery, Result<IReadOnlyList<UnitSummary>>> units,
        IQueryHandler<ListSuppliersQuery, Result<IReadOnlyList<SupplierSummary>>> suppliers,
        ICommandHandler<SaveCategoryCommand, Result<int>> saveCategory,
        ICommandHandler<SaveUnitCommand, Result<int>> saveUnit,
        ICommandHandler<SaveSupplierCommand, Result<int>> saveSupplier,
        IStrings strings,
        LanguageService languageService)
    {
        Kind = kind;

        _categories = categories;
        _units = units;
        _suppliers = suppliers;
        _saveCategory = saveCategory;
        _saveUnit = saveUnit;
        _saveSupplier = saveSupplier;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The kind of record being edited.</summary>
    public CatalogReferenceKind Kind { get; }

    /// <summary>The identifier of the record being edited, or null for a new one.</summary>
    public int? RecordId { get; private set; }

    /// <summary>The fields the record has.</summary>
    public ObservableCollection<EditorField> Fields { get; } = [];

    /// <summary>The window title.</summary>
    public string Title => Kind switch
    {
        CatalogReferenceKind.Unit => _strings[StringKeys.UnitsNew],
        CatalogReferenceKind.Supplier => _strings[StringKeys.SuppliersNew],
        _ => _strings[StringKeys.CategoriesNew],
    };

    /// <summary>The label on the save command.</summary>
    public string SaveText => _strings[StringKeys.CommonSave];

    /// <summary>The label on the cancel command.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>Loads the record and fills the fields in.</summary>
    /// <param name="recordId">The record to edit, or null to create one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [RelayCommand]
    public async Task LoadAsync(int? recordId, CancellationToken cancellationToken)
    {
        RecordId = recordId;
        BuildFields();

        switch (Kind)
        {
            case CatalogReferenceKind.Category:
                await LoadCategoryAsync(recordId, cancellationToken);
                break;
            case CatalogReferenceKind.Unit:
                await LoadUnitAsync(recordId, cancellationToken);
                break;
            case CatalogReferenceKind.Supplier:
                await LoadSupplierAsync(recordId, cancellationToken);
                break;
        }
    }

    /// <summary>Saves the record.</summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        ClearErrors();

        string name = Value("name");
        string nameAr = Value("nameAr");

        IReadOnlyList<ValidationIssue> issues = Kind switch
        {
            CatalogReferenceKind.Unit => CatalogValidation.ValidateUnit(name, nameAr, Precision()),
            CatalogReferenceKind.Supplier => CatalogValidation.ValidateSupplier(name, nameAr, Value("phone"), Value("notes")),
            _ => CatalogValidation.ValidateCategory(name, nameAr),
        };

        if (issues.Count > 0)
        {
            ShowIssues(issues);
            return;
        }

        Result<int> result;

        switch (Kind)
        {
            case CatalogReferenceKind.Unit:
                result = await _saveUnit.HandleAsync(
                    new SaveUnitCommand(RecordId, name, nameAr, Precision(), _isActive),
                    cancellationToken);
                break;
            case CatalogReferenceKind.Supplier:
                result = await _saveSupplier.HandleAsync(
                    new SaveSupplierCommand(RecordId, name, nameAr, Value("phone"), Value("notes"), _isActive),
                    cancellationToken);
                break;
            default:
                result = await _saveCategory.HandleAsync(
                    new SaveCategoryCommand(RecordId, name, nameAr, _isActive),
                    cancellationToken);
                break;
        }

        if (result.IsFailure)
        {
            StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code, KindName(), name);
            return;
        }

        WasSaved = true;
        CloseRequested?.Invoke(this, true);
    }

    /// <summary>Closes the editor without saving.</summary>
    [RelayCommand]
    private void Cancel()
    {
        WasSaved = false;
        CloseRequested?.Invoke(this, false);
    }

    private void BuildFields()
    {
        Fields.Clear();

        Fields.Add(new EditorField("name", LabelFor("name"), EditorFieldKind.Text));

        switch (Kind)
        {
            case CatalogReferenceKind.Unit:
                Fields.Add(new EditorField("nameAr", LabelFor("nameAr"), EditorFieldKind.Text));
                Fields.Add(UnitPrecisionField());
                break;
            case CatalogReferenceKind.Supplier:
                Fields.Add(new EditorField("nameAr", LabelFor("nameAr"), EditorFieldKind.Text));
                Fields.Add(new EditorField("phone", LabelFor("phone"), EditorFieldKind.Text));
                Fields.Add(new EditorField("notes", LabelFor("notes"), EditorFieldKind.Multiline));
                break;
            default:
                Fields.Add(new EditorField("nameAr", LabelFor("nameAr"), EditorFieldKind.Text));
                break;
        }
    }

    private async Task LoadCategoryAsync(int? recordId, CancellationToken cancellationToken)
    {
        if (recordId is not int id)
        {
            return;
        }

        Result<IReadOnlyList<CategorySummary>> result = await _categories.HandleAsync(
            new ListCategoriesQuery(IncludeInactive: true),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        foreach (CategorySummary category in result.Value)
        {
            if (category.Id != id)
            {
                continue;
            }

            SetValue("name", category.Name);
            SetValue("nameAr", category.NameAr ?? string.Empty);
            _isActive = category.IsActive;
            return;
        }
    }

    private async Task LoadUnitAsync(int? recordId, CancellationToken cancellationToken)
    {
        if (recordId is not int id)
        {
            return;
        }

        Result<IReadOnlyList<UnitSummary>> result = await _units.HandleAsync(
            new ListUnitsQuery(IncludeInactive: true),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        foreach (UnitSummary unit in result.Value)
        {
            if (unit.Id != id)
            {
                continue;
            }

            SetValue("name", unit.Name);
            SetValue("nameAr", unit.NameAr ?? string.Empty);
            SetValue("precision", unit.Precision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _isActive = unit.IsActive;
            return;
        }
    }

    private async Task LoadSupplierAsync(int? recordId, CancellationToken cancellationToken)
    {
        if (recordId is not int id)
        {
            return;
        }

        Result<IReadOnlyList<SupplierSummary>> result = await _suppliers.HandleAsync(
            new ListSuppliersQuery(null, IncludeInactive: true),
            cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = CatalogErrorMessages.Resolve(_strings, result.Error.Code);
            return;
        }

        foreach (SupplierSummary supplier in result.Value)
        {
            if (supplier.Id != id)
            {
                continue;
            }

            SetValue("name", supplier.Name);
            SetValue("nameAr", supplier.NameAr ?? string.Empty);
            SetValue("phone", supplier.Phone ?? string.Empty);
            SetValue("notes", supplier.Notes ?? string.Empty);
            _isActive = supplier.IsActive;
            return;
        }
    }

    private static IReadOnlyList<EditorChoice> PrecisionOptions()
    {
        var options = new List<EditorChoice>();

        for (int precision = 0; precision <= QuantityScale.MaxScale; precision++)
        {
            string text = precision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            options.Add(new EditorChoice(text, text));
        }

        return options;
    }

    /// <summary>
    /// The precision field of a unit, starting at the default of whole units. The field starts with a
    /// value rather than an empty one so the list shows what a new unit will be.
    /// </summary>
    private EditorField UnitPrecisionField()
    {
        var field = new EditorField("precision", LabelFor("precision"), EditorFieldKind.Choice, PrecisionOptions());
        field.Value = DefaultPrecision;
        return field;
    }

    private string Value(string key)
    {
        foreach (EditorField field in Fields)
        {
            if (field.Key == key)
            {
                return field.Value.Trim();
            }
        }

        return string.Empty;
    }

    private void SetValue(string key, string value)
    {
        foreach (EditorField field in Fields)
        {
            if (field.Key == key)
            {
                field.Value = value;
                return;
            }
        }
    }

    private int Precision() =>
        int.TryParse(Value("precision"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int precision)
            ? precision
            : 0;

    private string LabelFor(string key) => Kind switch
    {
        CatalogReferenceKind.Unit => key switch
        {
            "name" => _strings[StringKeys.UnitsName],
            "nameAr" => _strings[StringKeys.UnitsNameAr],
            _ => _strings[StringKeys.UnitsPrecision],
        },
        CatalogReferenceKind.Supplier => key switch
        {
            "name" => _strings[StringKeys.SuppliersName],
            "nameAr" => _strings[StringKeys.SuppliersNameAr],
            "phone" => _strings[StringKeys.SuppliersPhone],
            _ => _strings[StringKeys.SuppliersNotes],
        },
        _ => key switch
        {
            "name" => _strings[StringKeys.CategoriesName],
            _ => _strings[StringKeys.CategoriesNameAr],
        },
    };

    private string KindName() => Kind switch
    {
        CatalogReferenceKind.Unit => _strings[StringKeys.UnitsTitle].ToLowerInvariant(),
        CatalogReferenceKind.Supplier => _strings[StringKeys.SuppliersTitle].ToLowerInvariant(),
        _ => _strings[StringKeys.CategoriesTitle].ToLowerInvariant(),
    };

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues)
    {
        foreach (ValidationIssue issue in issues)
        {
            string message = CatalogErrorMessages.Resolve(_strings, issue.Code, LabelFor(issue.Field));

            foreach (EditorField field in Fields)
            {
                if (field.Key == issue.Field)
                {
                    field.Error = message;
                    break;
                }
            }
        }
    }

    private void ClearErrors()
    {
        foreach (EditorField field in Fields)
        {
            field.Error = string.Empty;
        }

        StatusMessage = string.Empty;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(string.Empty);
}
