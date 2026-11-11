using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>One backup destination as the screen shows it.</summary>
/// <param name="Kind">The destination's identifier.</param>
/// <param name="DisplayName">The name shown to the owner, in the interface language.</param>
/// <param name="StatusMessage">Where the backups go, or why the destination is not ready.</param>
/// <param name="IsConfigured">True when the destination has everything it needs.</param>
public sealed record BackupTargetOption(
    string Kind,
    string DisplayName,
    string StatusMessage,
    bool IsConfigured)
{
    /// <summary>
    /// The name a select shows for this entry.
    /// </summary>
    /// <remarks>
    /// A select shows the selected item's own text. Without this override the closed control
    /// would show the record's generated <c>ToString</c> instead of the name of the choice.
    /// </remarks>
    public override string ToString() => DisplayName;
}

/// <summary>
/// The backup screen: where the shop's copies go, when they are taken, what has been taken, and
/// the way back when something goes wrong (PRD §8.5).
/// </summary>
public sealed partial class BackupViewModel : ObservableObject
{
    private readonly ICommandHandler<CreateBackupCommand, Result<BackupRunResult>> _createBackup;
    private readonly IQueryHandler<ListBackupTargetsQuery, Result<IReadOnlyList<BackupTargetSummary>>> _listTargets;
    private readonly IQueryHandler<TestBackupTargetQuery, Result<BackupTargetTestResult>> _testTarget;
    private readonly IQueryHandler<ListBackupHistoryQuery, Result<IReadOnlyList<BackupHistoryEntry>>> _listHistory;
    private readonly IQueryHandler<GetBackupScheduleQuery, Result<BackupScheduleView>> _getSchedule;
    private readonly ICommandHandler<SaveBackupScheduleCommand, Result> _saveSchedule;
    private readonly IBackupDialogs _dialogs;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    /// <summary>
    /// The read of the log that is on its way. It is cancelled when the owner changes the filter,
    /// so the rows on screen are always the rows the filter asks for.
    /// </summary>
    private CancellationTokenSource? _historyCancellation;

    /// <summary>
    /// The read of the destinations that is on its way. It is cancelled when the screen is read
    /// again, so an older answer cannot land on top of a newer one.
    /// </summary>
    private CancellationTokenSource? _targetsCancellation;

    [ObservableProperty]
    private BackupTargetOption? _selectedTarget;

    [ObservableProperty]
    private BackupTargetOption? _selectedLogTarget;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _outcomeMessage = string.Empty;

    [ObservableProperty]
    private string _lastChecksum = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _dailyEnabled = true;

    [ObservableProperty]
    private bool _weeklyEnabled = true;

    [ObservableProperty]
    private string _dailyTimeText = "23:00";

    [ObservableProperty]
    private string _weeklyTimeText = "23:00";

    [ObservableProperty]
    private int _weeklyDay;

    [ObservableProperty]
    private string _dailyRetentionText = "7";

    [ObservableProperty]
    private string _weeklyRetentionText = "4";

    [ObservableProperty]
    private string _backupDirectory = string.Empty;

    [ObservableProperty]
    private string _filterTarget = string.Empty;

    [ObservableProperty]
    private BackupResultFilterOption? _filterResult;

    [ObservableProperty]
    private string _fromText = string.Empty;

    [ObservableProperty]
    private string _toText = string.Empty;

    /// <summary>Creates the screen.</summary>
    /// <param name="createBackup">Takes a backup.</param>
    /// <param name="listTargets">Lists the destinations.</param>
    /// <param name="testTarget">Asks a destination whether it is reachable.</param>
    /// <param name="listHistory">Reads the backup log.</param>
    /// <param name="getSchedule">Reads the scheduler's settings.</param>
    /// <param name="saveSchedule">Stores the scheduler's settings.</param>
    /// <param name="dialogs">Opens the restore and recovery wizards.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public BackupViewModel(
        ICommandHandler<CreateBackupCommand, Result<BackupRunResult>> createBackup,
        IQueryHandler<ListBackupTargetsQuery, Result<IReadOnlyList<BackupTargetSummary>>> listTargets,
        IQueryHandler<TestBackupTargetQuery, Result<BackupTargetTestResult>> testTarget,
        IQueryHandler<ListBackupHistoryQuery, Result<IReadOnlyList<BackupHistoryEntry>>> listHistory,
        IQueryHandler<GetBackupScheduleQuery, Result<BackupScheduleView>> getSchedule,
        ICommandHandler<SaveBackupScheduleCommand, Result> saveSchedule,
        IBackupDialogs dialogs,
        IStrings strings,
        LanguageService languageService)
    {
        _createBackup = createBackup;
        _listTargets = listTargets;
        _testTarget = testTarget;
        _listHistory = listHistory;
        _getSchedule = getSchedule;
        _saveSchedule = saveSchedule;
        _dialogs = dialogs;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;

        BuildDays();
        BuildResultFilters();
    }

    /// <summary>
    /// The seven days, in the order the week runs.
    /// </summary>
    private void BuildDays()
    {
        Days.Clear();

        for (int day = 0; day < 7; day++)
        {
            Days.Add(new DayOption(day, DescribeDay(day)));
        }
    }

    /// <summary>
    /// The outcomes the log can be narrowed to, with "every outcome" first.
    /// </summary>
    private void BuildResultFilters()
    {
        ResultFilters.Clear();
        ResultFilters.Add(new BackupResultFilterOption(string.Empty, _strings[StringKeys.CommonAll]));
        ResultFilters.Add(new BackupResultFilterOption("Succeeded", _strings[StringKeys.BackupResultSucceeded]));
        ResultFilters.Add(new BackupResultFilterOption("Failed", _strings[StringKeys.BackupResultFailed]));
        ResultFilters.Add(new BackupResultFilterOption("Cancelled", _strings[StringKeys.BackupResultCancelled]));
    }

    /// <summary>The name of a day, in the interface language.</summary>
    /// <param name="day">The day, where 0 is Sunday.</param>
    /// <returns>The name of the day.</returns>
    private string DescribeDay(int day) => day switch
    {
        0 => _strings[StringKeys.BackupDaySunday],
        1 => _strings[StringKeys.BackupDayMonday],
        2 => _strings[StringKeys.BackupDayTuesday],
        3 => _strings[StringKeys.BackupDayWednesday],
        4 => _strings[StringKeys.BackupDayThursday],
        5 => _strings[StringKeys.BackupDayFriday],
        _ => _strings[StringKeys.BackupDaySaturday],
    };

    /// <summary>The destinations the shop can write to.</summary>
    public ObservableCollection<BackupTargetOption> Targets { get; } = [];

    /// <summary>The rows of the backup log.</summary>
    public ObservableCollection<BackupHistoryEntry> History { get; } = [];

    /// <summary>The outcomes the filter can narrow the log to.</summary>
    public ObservableCollection<BackupResultFilterOption> ResultFilters { get; } = [];

    /// <summary>The destinations the log can be narrowed to, with "every destination" first.</summary>
    public ObservableCollection<BackupTargetOption> LogTargetFilters { get; } = [];

    /// <summary>The days the weekly backup can fall on.</summary>
    public ObservableCollection<DayOption> Days { get; } = [];

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.BackupTitle];

    /// <summary>The label of the destinations card.</summary>
    public string TargetsLabel => _strings[StringKeys.BackupTargets];

    /// <summary>The label of the picker that says where a copy goes.</summary>
    public string DestinationLabel => _strings[StringKeys.BackupDestination];

    /// <summary>The label of the "back up now" card.</summary>
    public string BackUpNowLabel => _strings[StringKeys.BackupNow];

    /// <summary>The label of the scheduler card.</summary>
    public string ScheduleLabel => _strings[StringKeys.BackupSchedule];

    /// <summary>The label of the backup log.</summary>
    public string LogLabel => _strings[StringKeys.BackupLog];

    /// <summary>The text shown when the log is empty.</summary>
    public string LogEmptyText => _strings[StringKeys.BackupLogEmpty];

    /// <summary>True when the log holds nothing.</summary>
    public bool IsLogEmpty => !IsBusy && History.Count == 0;

    /// <summary>The label of the daily backup switch.</summary>
    public string DailyEnabledLabel => _strings[StringKeys.BackupDailyEnabled];

    /// <summary>The label of the weekly backup switch.</summary>
    public string WeeklyEnabledLabel => _strings[StringKeys.BackupWeeklyEnabled];

    /// <summary>The label of the daily backup time.</summary>
    public string DailyTimeLabel => _strings[StringKeys.BackupDailyTime];

    /// <summary>The label of the weekly backup day.</summary>
    public string WeeklyDayLabel => _strings[StringKeys.BackupWeeklyDay];

    /// <summary>The label of the weekly backup time.</summary>
    public string WeeklyTimeLabel => _strings[StringKeys.BackupWeeklyTime];

    /// <summary>The label of the daily retention field.</summary>
    public string DailyRetentionLabel => _strings[StringKeys.BackupDailyRetention];

    /// <summary>The label of the weekly retention field.</summary>
    public string WeeklyRetentionLabel => _strings[StringKeys.BackupWeeklyRetention];

    /// <summary>The label of the backup folder field.</summary>
    public string BackupDirectoryLabel => _strings[StringKeys.BackupDirectory];

    /// <summary>The label of the save command.</summary>
    public string SaveLabel => _strings[StringKeys.CommonSave];

    /// <summary>The label of the restore command.</summary>
    public string RestoreLabel => _strings[StringKeys.BackupRestore];

    /// <summary>The label of the recovery command.</summary>
    public string RecoverLabel => _strings[StringKeys.BackupRecover];

    /// <summary>The label of the test command.</summary>
    public string TestLabel => _strings[StringKeys.BackupTest];

    /// <summary>The label of the column that holds the time.</summary>
    public string WhenHeader => _strings[StringKeys.BackupColumnWhen];

    /// <summary>The label of the column that holds the destination.</summary>
    public string TargetHeader => _strings[StringKeys.BackupColumnTarget];

    /// <summary>The label of the column that holds the location.</summary>
    public string PathHeader => _strings[StringKeys.BackupColumnPath];

    /// <summary>The label of the column that holds the checksum.</summary>
    public string ChecksumHeader => _strings[StringKeys.BackupColumnChecksum];

    /// <summary>The label of the column that holds the size.</summary>
    public string SizeHeader => _strings[StringKeys.BackupColumnSize];

    /// <summary>The label of the column that holds the outcome.</summary>
    public string ResultHeader => _strings[StringKeys.BackupColumnResult];

    /// <summary>The label of the column that holds the message.</summary>
    public string MessageHeader => _strings[StringKeys.BackupColumnMessage];

    /// <summary>The label of the target filter.</summary>
    public string FilterTargetLabel => _strings[StringKeys.BackupFilterTarget];

    /// <summary>The label of the outcome filter.</summary>
    public string FilterResultLabel => _strings[StringKeys.BackupFilterResult];

    /// <summary>The label of the first date field.</summary>
    public string FromLabel => _strings[StringKeys.BackupFilterFrom];

    /// <summary>The label of the second date field.</summary>
    public string ToLabel => _strings[StringKeys.BackupFilterTo];

    /// <summary>The label of the checksum of the backup that was just taken.</summary>
    public string ChecksumLabel => _strings[StringKeys.BackupChecksum];

    /// <summary>True when a daily backup is switched on.</summary>
    public bool IsDailyEnabled
    {
        get => DailyEnabled;
        set => DailyEnabled = value;
    }

    /// <summary>Loads the destinations, the log and the schedule.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            await LoadTargetsAsync(cancellationToken);
            await LoadScheduleAsync(cancellationToken);
            await LoadHistoryAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsLogEmpty));
        }
    }

    /// <summary>
    /// Takes a backup now and writes down what happened, including the checksum that proves the
    /// file on the target is the file that was written.
    /// </summary>
    [RelayCommand]
    private async Task BackupNowAsync(CancellationToken cancellationToken)
    {
        if (SelectedTarget is null)
        {
            StatusMessage = _strings[StringKeys.BackupNoTarget];

            return;
        }

        IsBusy = true;
        OutcomeMessage = string.Empty;
        LastChecksum = string.Empty;

        try
        {
            Result<BackupRunResult> result = await _createBackup.HandleAsync(
                new CreateBackupCommand(SelectedTarget.Kind),
                cancellationToken);

            if (result.IsFailure)
            {
                OutcomeMessage = BackupErrorMessages.Resolve(_strings, result.Error.Code);
                StatusMessage = OutcomeMessage;
            }
            else
            {
                OutcomeMessage = _strings.Format(
                    StringKeys.BackupOutcomeWritten,
                    Path.GetFileName(result.Value.ArchivePath));
                LastChecksum = result.Value.Sha256 ?? string.Empty;
                StatusMessage = OutcomeMessage;
            }

            await LoadHistoryAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Asks a destination whether it can be reached.</summary>
    /// <param name="target">The destination to test.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [RelayCommand]
    private async Task TestAsync(BackupTargetOption target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        IsBusy = true;

        try
        {
            Result<BackupTargetTestResult> result = await _testTarget.HandleAsync(
                new TestBackupTargetQuery(target.Kind),
                cancellationToken);

            StatusMessage = result.IsFailure
                ? BackupErrorMessages.Resolve(_strings, result.Error.Code)
                : DescribeState(result.Value.State, result.Value.Detail);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Stores the scheduler's settings.</summary>
    [RelayCommand]
    private async Task SaveScheduleAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result saved = await _saveSchedule.HandleAsync(
                new SaveBackupScheduleCommand(
                    SelectedTarget?.Kind ?? string.Empty,
                    ReadMinutes(DailyTimeText, 23 * 60),
                    WeeklyDay,
                    ReadMinutes(WeeklyTimeText, 23 * 60),
                    DailyEnabled,
                    WeeklyEnabled,
                    ReadNumber(DailyRetentionText, 7),
                    ReadNumber(WeeklyRetentionText, 4),
                    BackupDirectory),
                cancellationToken);

            StatusMessage = saved.IsFailure
                ? ReportFailure("The backup schedule could not be saved.", saved.Error)
                : _strings[StringKeys.BackupScheduleSaved];
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens the restore wizard.</summary>
    [RelayCommand]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        await _dialogs.ShowRestoreWizardAsync(cancellationToken);

        await LoadHistoryAsync(cancellationToken);
    }

    /// <summary>Opens the recovery wizard.</summary>
    [RelayCommand]
    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await _dialogs.ShowRecoveryWizardAsync(cancellationToken);
    }

    private async Task LoadTargetsAsync(CancellationToken cancellationToken)
    {
        // A read that is already on its way is cancelled, because the newest answer is the one the
        // screen means. The old source is not disposed: the read it belongs to may still be
        // handing the token to the database.
        _targetsCancellation?.Cancel();
        _targetsCancellation = new CancellationTokenSource();

        CancellationToken token = _targetsCancellation.Token;

        try
        {
            Result<IReadOnlyList<BackupTargetSummary>> result =
                await _listTargets.HandleAsync(new ListBackupTargetsQuery(), token);

            token.ThrowIfCancellationRequested();

            if (result.IsFailure)
            {
                StatusMessage = ReportFailure("The backup destinations could not be read.", result.Error);

                return;
            }

            string? selected = SelectedTarget?.Kind;

            Targets.Clear();

            foreach (BackupTargetSummary summary in result.Value)
            {
                Targets.Add(new BackupTargetOption(
                    summary.Kind,
                    DescribeKind(summary.Kind, summary.DisplayName),
                    DescribeState(summary.State, summary.Detail),
                    summary.IsConfigured));
            }

            SelectedTarget = Targets.FirstOrDefault(option => option.Kind == selected) ?? Targets.FirstOrDefault();

            BuildLogTargetFilters();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another read of the destinations has taken over; this answer is stale and is dropped.
        }
#pragma warning disable CA1031 // A read that fails must not take the screen down with it.
        catch (Exception ex)
        {
            // A read that fails outright must not lose its exception in the void. The log keeps the
            // detail and the owner is told that the destinations could not be read.
            StartupDiagnostics.Write("The backup destinations could not be read.", ex);

            if (!token.IsCancellationRequested)
            {
                StatusMessage = _strings[StringKeys.ErrorUnexpected];
            }
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// The destinations the log can be narrowed to, with "every destination" first.
    /// </summary>
    private void BuildLogTargetFilters()
    {
        string? selected = SelectedLogTarget?.Kind;

        LogTargetFilters.Clear();
        LogTargetFilters.Add(new BackupTargetOption(string.Empty, _strings[StringKeys.CommonAll], string.Empty, true));

        foreach (BackupTargetOption option in Targets)
        {
            LogTargetFilters.Add(option);
        }

        SelectedLogTarget = LogTargetFilters.FirstOrDefault(option => option.Kind == selected)
            ?? LogTargetFilters[0];
    }

    private async Task LoadScheduleAsync(CancellationToken cancellationToken)
    {
        Result<BackupScheduleView> result =
            await _getSchedule.HandleAsync(new GetBackupScheduleQuery(), cancellationToken);

        if (result.IsFailure)
        {
            StatusMessage = ReportFailure("The backup schedule could not be read.", result.Error);

            return;
        }

        BackupScheduleView view = result.Value;

        DailyEnabled = view.DailyEnabled;
        WeeklyEnabled = view.WeeklyEnabled;
        DailyTimeText = FormatMinutes(view.DailyTimeMinutes);
        WeeklyTimeText = FormatMinutes(view.WeeklyTimeMinutes);
        WeeklyDay = Math.Clamp(view.WeeklyDayOfWeek, 0, 6);
        DailyRetentionText = view.DailyRetention.ToString(CultureInfo.InvariantCulture);
        WeeklyRetentionText = view.WeeklyRetention.ToString(CultureInfo.InvariantCulture);
        BackupDirectory = view.BackupDirectory;

        if (!string.IsNullOrWhiteSpace(view.TargetKind))
        {
            BackupTargetOption? target = Targets.FirstOrDefault(option => option.Kind == view.TargetKind);

            if (target is not null)
            {
                SelectedTarget = target;
            }
        }
    }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken)
    {
        // A read that is already on its way is cancelled, because the newest filter is the one the
        // owner means. The old source is not disposed: the read it belongs to may still be handing
        // the token to the database.
        _historyCancellation?.Cancel();
        _historyCancellation = new CancellationTokenSource();

        CancellationToken token = _historyCancellation.Token;

        try
        {
            Result<IReadOnlyList<BackupHistoryEntry>> result = await _listHistory.HandleAsync(
                new ListBackupHistoryQuery(
                    string.IsNullOrWhiteSpace(FilterTarget) ? null : FilterTarget,
                    ReadResult(FilterResult?.Key),
                    ReadDate(FromText),
                    ReadDate(ToText),
                    500),
                token);

            token.ThrowIfCancellationRequested();

            if (result.IsFailure)
            {
                StatusMessage = ReportFailure("The backup log could not be read.", result.Error);

                return;
            }

            History.Clear();

            foreach (BackupHistoryEntry entry in result.Value)
            {
                History.Add(entry);
            }

            OnPropertyChanged(nameof(IsLogEmpty));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another read of the log has taken over; this answer is stale and is dropped.
        }
#pragma warning disable CA1031 // A read that fails must not take the screen down with it.
        catch (Exception ex)
        {
            // A read that fails outright must not lose its exception in the void. The log keeps the
            // detail and the owner is told that the log could not be read.
            StartupDiagnostics.Write("The backup log could not be read.", ex);

            if (!token.IsCancellationRequested)
            {
                StatusMessage = _strings[StringKeys.ErrorUnexpected];
            }
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Turns a failed operation into the message the owner reads, and keeps the technical detail —
    /// a file path, a reason from the operating system — in the log.
    /// </summary>
    /// <param name="what">What the screen was trying to do.</param>
    /// <param name="error">The failure the operation answered with.</param>
    /// <returns>The message to show the owner.</returns>
    private string ReportFailure(string what, Error error)
    {
        StartupDiagnostics.Write(what + ": " + error.Message);

        return BackupErrorMessages.Resolve(_strings, error.Code);
    }

    /// <summary>
    /// The name of a destination, in the interface language. The destinations this build knows
    /// have names; one added later shows the name its own implementation gives it.
    /// </summary>
    private string DescribeKind(string kind, string fallback) => kind switch
    {
        "LocalFolder" => _strings[StringKeys.BackupTargetLocalFolder],
        "S3" => _strings[StringKeys.BackupTargetS3],
        _ => fallback,
    };

    /// <summary>
    /// The state a destination is in, in the interface language.
    /// </summary>
    /// <param name="state">The state the destination is in.</param>
    /// <param name="detail">Where the copies go, or the error that stopped the destination answering.</param>
    /// <returns>The sentence the owner reads.</returns>
    private string DescribeState(BackupTargetState state, string? detail) => state switch
    {
        BackupTargetState.Ready => string.IsNullOrWhiteSpace(detail)
            ? _strings[StringKeys.BackupTargetReadyPlain]
            : _strings.Format(StringKeys.BackupTargetReady, detail),
        BackupTargetState.NotConfigured => _strings[StringKeys.BackupTargetNotConfigured],
        _ => string.IsNullOrWhiteSpace(detail)
            ? _strings[StringKeys.BackupTargetUnreachablePlain]
            : _strings.Format(StringKeys.BackupTargetUnreachable, detail),
    };

    /// <summary>The outcome a log row carries, in the interface language.</summary>
    /// <param name="result">The outcome.</param>
    /// <returns>The name of the outcome.</returns>
    public string DescribeResult(BackupResult result) => result switch
    {
        BackupResult.Succeeded => _strings[StringKeys.BackupResultSucceeded],
        BackupResult.Failed => _strings[StringKeys.BackupResultFailed],
        _ => _strings[StringKeys.BackupResultCancelled],
    };

    /// <summary>
    /// The outcome a log row carries, in the interface language, as a bindable value.
    /// </summary>
    /// <param name="result">The outcome.</param>
    /// <returns>The name of the outcome.</returns>
    public string DescribeResultText(object? result) =>
        result is BackupResult value ? DescribeResult(value) : string.Empty;

    private static DateOnly? ReadDate(string text) =>
        DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? day
            : null;

    private static BackupResult? ReadResult(string? text) => text switch
    {
        "Succeeded" => BackupResult.Succeeded,
        "Failed" => BackupResult.Failed,
        "Cancelled" => BackupResult.Cancelled,
        _ => null,
    };

    private static int ReadNumber(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static int ReadMinutes(string text, int fallback)
    {
        if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            return fallback;
        }

        int minutes = (parsed.Hours * 60) + parsed.Minutes;

        return Math.Clamp(minutes, 0, (24 * 60) - 1);
    }

    private static string FormatMinutes(int minutes)
    {
        int clamped = Math.Clamp(minutes, 0, (24 * 60) - 1);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{clamped / 60:00}:{clamped % 60:00}");
    }

    partial void OnFilterTargetChanged(string value) => ScheduleHistoryLoad();

    partial void OnSelectedLogTargetChanged(BackupTargetOption? value)
    {
        FilterTarget = value?.Kind ?? string.Empty;
    }

    partial void OnFilterResultChanged(BackupResultFilterOption? value) => ScheduleHistoryLoad();

    partial void OnFromTextChanged(string value) => ScheduleHistoryLoad();

    partial void OnToTextChanged(string value) => ScheduleHistoryLoad();

    /// <summary>
    /// Reads the log again, and forgets the read that was waiting, so the rows on screen are the
    /// rows the filter the owner has just set asks for.
    /// </summary>
    private void ScheduleHistoryLoad() => _ = LoadHistoryAsync(CancellationToken.None);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);

        _ = RefreshAsync(CancellationToken.None);
    }
}

/// <summary>One day of the week, in the interface language.</summary>
/// <param name="Day">The day, where 0 is Sunday.</param>
/// <param name="Name">The name of the day.</param>
public sealed record DayOption(int Day, string Name)
{
    /// <summary>
    /// The name a select shows for this entry.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>One outcome the backup log can be narrowed to.</summary>
/// <param name="Key">The outcome's name, or empty for every outcome.</param>
/// <param name="DisplayName">The name shown to the owner.</param>
public sealed record BackupResultFilterOption(string Key, string DisplayName)
{
    /// <summary>
    /// The name a select shows for this entry.
    /// </summary>
    public override string ToString() => DisplayName;
}
