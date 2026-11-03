using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// The recovery wizard: what to do when the shop's computer has failed, and the one button that
/// puts the shop back (PRD §8.5).
/// </summary>
public sealed partial class RecoveryWizardViewModel : ObservableObject
{
    private readonly ICommandHandler<RecoverStoreCommand, Result<RestoreOutcome>> _recover;
    private readonly IBackupDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private string _chosenPath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRestored;

    /// <summary>Creates the wizard.</summary>
    /// <param name="recover">Puts a backup back when the shop's data cannot be read.</param>
    /// <param name="dialogs">Opens the file picker.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public RecoveryWizardViewModel(
        ICommandHandler<RecoverStoreCommand, Result<RestoreOutcome>> recover,
        IBackupDialogs dialogs,
        IStrings strings,
        LanguageService languageService)
    {
        _recover = recover;
        _dialogs = dialogs;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;

        BuildSteps();
    }

    /// <summary>Raised when the owner closes the wizard.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The steps of the runbook, in the order they are done.</summary>
    public ObservableCollection<string> Steps { get; } = [];

    /// <summary>The window title.</summary>
    public string Title => _strings[StringKeys.RecoveryTitle];

    /// <summary>The heading of the runbook.</summary>
    public string RunbookHeading => _strings[StringKeys.RecoveryRunbookHeading];

    /// <summary>The heading of the restore block.</summary>
    public string RestoreHeading => _strings[StringKeys.RecoveryRestoreHeading];

    /// <summary>The label of the file chooser.</summary>
    public string ChooseLabel => _strings[StringKeys.RecoveryChoose];

    /// <summary>The label of the browse command.</summary>
    public string BrowseLabel => _strings[StringKeys.CommonBrowse];

    /// <summary>The label of the restore command.</summary>
    public string RestoreLabel => _strings[StringKeys.RecoveryRestore];

    /// <summary>The label of the close command.</summary>
    public string CancelLabel => _strings[StringKeys.CommonCancel];

    /// <summary>The text shown when the restore has not been done.</summary>
    public string HelpText => _strings[StringKeys.RecoveryHelp];

    /// <summary>True when a backup file has been chosen.</summary>
    public bool HasChosenFile => !string.IsNullOrWhiteSpace(ChosenPath);

    /// <summary>Chooses a backup file from anywhere on this computer.</summary>
    [RelayCommand]
    private async Task BrowseAsync(CancellationToken cancellationToken)
    {
        string? path = await _dialogs.PickBackupFileAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        ChosenPath = path;
        StatusMessage = string.Empty;
    }

    /// <summary>Puts the chosen backup back.</summary>
    [RelayCommand]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!HasChosenFile)
        {
            StatusMessage = _strings[StringKeys.RestoreNothingChosen];

            return;
        }

        IsBusy = true;

        try
        {
            Result<RestoreOutcome> result = await _recover.HandleAsync(
                new RecoverStoreCommand(ChosenPath),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = BackupErrorMessages.Resolve(_strings, result.Error.Code);

                return;
            }

            IsRestored = true;
            StatusMessage = result.Value.RestorePointPath is null or ""
                ? _strings[StringKeys.BackupRestoreDonePlain]
                : _strings.Format(StringKeys.BackupRestoreDone, result.Value.RestorePointPath);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The runbook, in the order it is followed.
    /// </summary>
    private void BuildSteps()
    {
        Steps.Clear();

        Steps.Add(_strings[StringKeys.RecoveryStep1]);
        Steps.Add(_strings[StringKeys.RecoveryStep2]);
        Steps.Add(_strings[StringKeys.RecoveryStep3]);
        Steps.Add(_strings[StringKeys.RecoveryStep4]);
        Steps.Add(_strings[StringKeys.RecoveryStep5]);
    }

    partial void OnChosenPathChanged(string value) => OnPropertyChanged(nameof(HasChosenFile));

    /// <summary>Closes the wizard.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        BuildSteps();
        OnPropertyChanged(string.Empty);
    }
}
