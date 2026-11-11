using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Approvals;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>One approval request, as the manager's screen shows it.</summary>
public sealed class ApprovalRequestView
{
    private readonly ApprovalRequestSummary _summary;

    /// <summary>Wraps a request for the screen.</summary>
    /// <param name="summary">The request.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="theme">The shop's theme, for the money.</param>
    public ApprovalRequestView(ApprovalRequestSummary summary, IStrings strings, IThemeService theme)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(theme);

        _summary = summary;

        Id = summary.Id;
        RequestedBy = summary.RequestedByName;
        DecidedBy = summary.ApprovedByName ?? string.Empty;
        Status = StatusText(summary.Status, strings);

        // A held discount and a held stock adjustment are both waiting for the same decision, so
        // the row says what it is about in the same place either way: the percentage and the money
        // for a discount, the goods and the count for an adjustment.
        Details = string.IsNullOrWhiteSpace(summary.Subject)
            ? $"{summary.Percent.ToString("0.##", CultureInfo.InvariantCulture)}% — {theme.FormatMoney(summary.Amount)}"
            : summary.Subject;

        Value = theme.FormatMoney(summary.CartTotal);
        When = summary.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Reason = summary.DecisionReason ?? string.Empty;
        IsPending = summary.Status == Domain.Enumerations.ApprovalStatus.Pending;
    }

    /// <summary>The request's id.</summary>
    public int Id { get; }

    /// <summary>Who asked for the approval.</summary>
    public string RequestedBy { get; }

    /// <summary>Who decided it, when it has been decided.</summary>
    public string DecidedBy { get; }

    /// <summary>Where the request stands.</summary>
    public string Status { get; }

    /// <summary>What the request is about.</summary>
    public string Details { get; }

    /// <summary>What the request is worth.</summary>
    public string Value { get; }

    /// <summary>When the request was made.</summary>
    public string When { get; }

    /// <summary>Why the request was decided the way it was.</summary>
    public string Reason { get; }

    /// <summary>True while the request is still waiting for a decision.</summary>
    public bool IsPending { get; }

    /// <summary>The request behind the row.</summary>
    public ApprovalRequestSummary Summary => _summary;

    /// <summary>Writes the state of a request in the language the application is running in.</summary>
    private static string StatusText(Domain.Enumerations.ApprovalStatus status, IStrings strings) => status switch
    {
        Domain.Enumerations.ApprovalStatus.Approved => strings[StringKeys.ApprovalsApproved],
        Domain.Enumerations.ApprovalStatus.Rejected => strings[StringKeys.ApprovalsRejected],
        Domain.Enumerations.ApprovalStatus.Cancelled => strings[StringKeys.ApprovalsRejected],
        _ => strings[StringKeys.ApprovalsPending],
    };
}

/// <summary>
/// The approvals screen. A discount that is over the shop's limit waits here for a manager, and no
/// invoice is written until the request has been given (PRD §3, UAT-13).
/// </summary>
public sealed partial class ApprovalsViewModel : ObservableObject
{
    private readonly IQueryHandler<ListApprovalRequestsQuery, Result<IReadOnlyList<ApprovalRequestSummary>>> _list;
    private readonly ICommandHandler<DecideApprovalRequestCommand, Result<ApprovalRequestSummary>> _decide;
    private readonly IStrings _strings;
    private readonly IThemeService _theme;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private ApprovalRequestView? _selectedRequest;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the screen.</summary>
    /// <param name="list">Reads the requests.</param>
    /// <param name="decide">Records the decision.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="theme">The shop's theme, for the money.</param>
    /// <param name="languageService">The application's language.</param>
    public ApprovalsViewModel(
        IQueryHandler<ListApprovalRequestsQuery, Result<IReadOnlyList<ApprovalRequestSummary>>> list,
        ICommandHandler<DecideApprovalRequestCommand, Result<ApprovalRequestSummary>> decide,
        IStrings strings,
        IThemeService theme,
        LanguageService languageService)
    {
        _list = list;
        _decide = decide;
        _strings = strings;
        _theme = theme;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The requests on screen, the newest first.</summary>
    public ObservableCollection<ApprovalRequestView> Requests { get; } = [];

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.ApprovalsTitle];

    /// <summary>The text shown when nothing is waiting.</summary>
    public string EmptyText => _strings[StringKeys.ApprovalsEmpty];

    /// <summary>The label of the approve button.</summary>
    public string ApproveLabel => _strings[StringKeys.ApprovalsApprove];

    /// <summary>The label of the reject button.</summary>
    public string RejectLabel => _strings[StringKeys.ApprovalsReject];

    /// <summary>The label of the status column.</summary>
    public string StatusLabel => _strings[StringKeys.ApprovalsStatus];

    /// <summary>The label of the column that holds who asked.</summary>
    public string RequestedByLabel => _strings[StringKeys.ApprovalsRequestedBy];

    /// <summary>The label of the column that holds what the request is about.</summary>
    public string DetailsLabel => _strings[StringKeys.ApprovalsDetails];

    /// <summary>The label of the column that holds what the request is worth.</summary>
    public string ValueLabel => _strings[StringKeys.ApprovalsValue];

    /// <summary>The label of the column that holds the time.</summary>
    public string WhenLabel => _strings[StringKeys.ApprovalsWhen];

    /// <summary>True when nothing is waiting.</summary>
    public bool IsEmpty => !IsBusy && Requests.Count == 0;

    /// <summary>True when the picked request can still be decided.</summary>
    public bool CanDecide => SelectedRequest is { IsPending: true } && !IsBusy;

    /// <summary>Reloads the requests.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result<IReadOnlyList<ApprovalRequestSummary>> result = await _list.HandleAsync(
                new ListApprovalRequestsQuery(null, 200),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            Requests.Clear();

            foreach (ApprovalRequestSummary summary in result.Value)
            {
                Requests.Add(new ApprovalRequestView(summary, _strings, _theme));
            }

            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanDecide));
        }
    }

    /// <summary>Gives the approval.</summary>
    [RelayCommand]
    private async Task ApproveAsync(CancellationToken cancellationToken) => await DecideAsync(true, cancellationToken);

    /// <summary>Turns the request down.</summary>
    [RelayCommand]
    private async Task RejectAsync(CancellationToken cancellationToken) => await DecideAsync(false, cancellationToken);

    /// <summary>Records the decision on the picked request.</summary>
    private async Task DecideAsync(bool approve, CancellationToken cancellationToken)
    {
        if (SelectedRequest is not ApprovalRequestView request)
        {
            return;
        }

        IsBusy = true;

        try
        {
            Result<ApprovalRequestSummary> result = await _decide.HandleAsync(
                new DecideApprovalRequestCommand(request.Id, approve),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            StatusMessage = approve
                ? _strings[StringKeys.ApprovalsApproved]
                : _strings[StringKeys.ApprovalsRejected];

            await RefreshAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanDecide));
        }
    }

    partial void OnSelectedRequestChanged(ApprovalRequestView? value) => OnPropertyChanged(nameof(CanDecide));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);
    }
}
