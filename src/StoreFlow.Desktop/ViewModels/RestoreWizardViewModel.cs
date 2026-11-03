using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The restore wizard: choose a backup, check it, see what it will replace, and then put it back
/// (PRD §8.5).
/// </summary>
public sealed partial class RestoreWizardViewModel : ObservableObject
{
    private readonly IQueryHandler<ListBackupArtifactsQuery, Result<IReadOnlyList<BackupArtifactInfo>>> _listArtifacts;
    private readonly IQueryHandler<InspectBackupQuery, Result<RestorePreview>> _inspect;
    private readonly ICommandHandler<RestoreBackupCommand, Result<RestoreOutcome>> _restore;
    private readonly IBackupDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private BackupArtifactInfo? _selectedArtifact;

    [ObservableProperty]
    private RestorePreview? _preview;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConfirming;

    [ObservableProperty]
    private bool _isRestored;

    [ObservableProperty]
    private string _restorePointText = string.Empty;

    /// <summary>Creates the wizard.</summary>
    /// <param name="listArtifacts">Lists the backups on this computer.</param>
    /// <param name="inspect">Reads a backup file and checks it.</param>
    /// <param name="restore">Puts a backup file back.</param>
    /// <param name="dialogs">Opens the file picker.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public RestoreWizardViewModel(
        IQueryHandler<ListBackupArtifactsQuery, Result<IReadOnlyList<BackupArtifactInfo>>> listArtifacts,
        IQueryHandler<InspectBackupQuery, Result<RestorePreview>> inspect,
        ICommandHandler<RestoreBackupCommand, Result<RestoreOutcome>> restore,
        IBackupDialogs dialogs,
        IStrings strings,
        LanguageService languageService)
    {
        _listArtifacts = listArtifacts;
        _inspect = inspect;
        _restore = restore;
        _dialogs = dialogs;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Raised when the owner closes the wizard.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The backups that are on this computer.</summary>
    public ObservableCollection<BackupArtifactInfo> Artifacts { get; } = [];

    /// <summary>The window title.</summary>
    public string Title => _strings[StringKeys.RestoreTitle];

    /// <summary>The label of the backup list.</summary>
    public string ChooseLabel => _strings[StringKeys.RestoreChoose];

    /// <summary>The label of the browse command.</summary>
    public string BrowseLabel => _strings[StringKeys.CommonBrowse];

    /// <summary>The label of the check command.</summary>
    public string CheckLabel => _strings[StringKeys.RestoreCheck];

    /// <summary>The label of the restore command.</summary>
    public string RestoreLabel => _strings[StringKeys.RestoreNow];

    /// <summary>The label of the confirmation command.</summary>
    public string ConfirmLabel => _strings[StringKeys.RestoreConfirm];

    /// <summary>The label of the cancel command.</summary>
    public string CancelLabel => _strings[StringKeys.CommonCancel];

    /// <summary>The heading of the confirmation block.</summary>
    public string ConfirmHeading => _strings[StringKeys.RestoreConfirmHeading];

    /// <summary>The text of the confirmation block.</summary>
    public string ConfirmBody => _strings[StringKeys.RestoreConfirmBody];

    /// <summary>The label of the "taken" line.</summary>
    public string CreatedLabel => _strings[StringKeys.RestoreCreated];

    /// <summary>The label of the "size" line.</summary>
    public string SizeLabel => _strings[StringKeys.RestoreSize];

    /// <summary>The label of the "checksum" line.</summary>
    public string ChecksumLabel => _strings[StringKeys.RestoreChecksum];

    /// <summary>The label of the "made by" line.</summary>
    public string AppVersionLabel => _strings[StringKeys.RestoreAppVersion];

    /// <summary>The label of the "data shape" line.</summary>
    public string SchemaLabel => _strings[StringKeys.RestoreSchema];

    /// <summary>The label of the "taken on" line.</summary>
    public string MachineLabel => _strings[StringKeys.RestoreMachine];

    /// <summary>The text shown when there is no backup on this computer.</summary>
    public string EmptyText => _strings[StringKeys.RestoreEmpty];

    /// <summary>True when there is no backup to choose from.</summary>
    public bool IsEmpty => !IsBusy && Artifacts.Count == 0;

    /// <summary>True when a backup has been chosen and checked.</summary>
    public bool HasPreview => Preview is not null;

    /// <summary>True when the chosen backup has been checked and may be restored.</summary>
    public bool CanRestore =>
        !IsBusy && Preview is not null && Preview.IsIntegrityOk && Preview.IsSchemaCompatible;

    /// <summary>Loads the backups on this computer.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result<IReadOnlyList<BackupArtifactInfo>> result =
                await _listArtifacts.HandleAsync(new ListBackupArtifactsQuery(), cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = BackupErrorMessages.Resolve(_strings, result.Error.Code);

                return;
            }

            Artifacts.Clear();

            foreach (BackupArtifactInfo artifact in result.Value)
            {
                Artifacts.Add(artifact);
            }

            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanRestore));
        }
    }

    /// <summary>Chooses a backup file from anywhere on this computer.</summary>
    [RelayCommand]
    private async Task BrowseAsync(CancellationToken cancellationToken)
    {
        string? path = await _dialogs.PickBackupFileAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var artifact = new BackupArtifactInfo(
            path,
            Path.GetFileName(path),
            new FileInfo(path).Length,
            File.GetLastWriteTimeUtc(path),
            BackupFileNaming.Classify(Path.GetFileName(path)) ?? BackupScheduleKind.Manual);

        Artifacts.Add(artifact);
        SelectedArtifact = artifact;

        await CheckAsync(cancellationToken);
    }

    /// <summary>Checks the chosen backup and says what it holds.</summary>
    [RelayCommand]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (SelectedArtifact is null)
        {
            StatusMessage = _strings[StringKeys.RestoreNothingChosen];

            return;
        }

        IsBusy = true;

        try
        {
            Result<RestorePreview> result = await _inspect.HandleAsync(
                new InspectBackupQuery(SelectedArtifact.Path),
                cancellationToken);

            if (result.IsFailure)
            {
                Preview = null;
                OnPropertyChanged(nameof(HasPreview));
                StatusMessage = BackupErrorMessages.Resolve(_strings, result.Error.Code);

                return;
            }

            Preview = result.Value;
            OnPropertyChanged(nameof(HasPreview));

            StatusMessage = result.Value.IsIntegrityOk
                ? _strings[StringKeys.RestoreIntegrityOk]
                : result.Value.IntegrityMessage ?? _strings[StringKeys.RestoreIntegrityFailed];
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanRestore));
        }
    }

    /// <summary>
    /// Shows what the restore will replace. Nothing is changed until the owner confirms.
    /// </summary>
    [RelayCommand]
    private void BeginRestore()
    {
        if (!CanRestore)
        {
            return;
        }

        IsConfirming = true;
    }

    /// <summary>Hides the confirmation without changing anything.</summary>
    [RelayCommand]
    private void CancelConfirm()
    {
        IsConfirming = false;
    }

    /// <summary>Puts the checked backup back.</summary>
    [RelayCommand]
    private async Task ConfirmRestoreAsync(CancellationToken cancellationToken)
    {
        if (SelectedArtifact is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            Result<RestoreOutcome> result = await _restore.HandleAsync(
                new RestoreBackupCommand(SelectedArtifact.Path),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = BackupErrorMessages.Resolve(_strings, result.Error.Code);

                return;
            }

            IsRestored = true;
            IsConfirming = false;
            RestorePointText = result.Value.RestorePointPath ?? string.Empty;
            StatusMessage = string.IsNullOrWhiteSpace(result.Value.RestorePointPath)
                ? _strings[StringKeys.BackupRestoreDonePlain]
                : _strings.Format(StringKeys.BackupRestoreDone, result.Value.RestorePointPath);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanRestore));
        }
    }

    partial void OnSelectedArtifactChanged(BackupArtifactInfo? value)
    {
        Preview = null;
        IsConfirming = false;

        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanRestore));
    }

    /// <summary>Closes the wizard.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
