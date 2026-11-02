using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Common;
using StoreFlow.Application.Identity;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>One user account, as the screen shows it.</summary>
public sealed class UserRowView
{
    /// <summary>Wraps an account for the screen.</summary>
    /// <param name="summary">The account.</param>
    public UserRowView(UserSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Id = summary.Id;
        Username = summary.Username;
        DisplayName = summary.DisplayName;
        RoleId = summary.RoleId;
        RoleName = summary.RoleName;
        IsActive = summary.IsActive;
    }

    /// <summary>The account's id.</summary>
    public int Id { get; }

    /// <summary>The login name.</summary>
    public string Username { get; }

    /// <summary>The name shown in the interface.</summary>
    public string DisplayName { get; }

    /// <summary>The role the account carries.</summary>
    public int RoleId { get; }

    /// <summary>The role's name.</summary>
    public string RoleName { get; }

    /// <summary>False when the account has been deactivated.</summary>
    public bool IsActive { get; }

    /// <summary>The account behind the row.</summary>
    public UserSummary Summary { get; } = null!;
}

/// <summary>One role, as the screen shows it.</summary>
public sealed class RoleRowView
{
    /// <summary>Wraps a role for the screen.</summary>
    /// <param name="summary">The role.</param>
    public RoleRowView(RoleSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Id = summary.Id;
        Code = summary.Code;
        Name = summary.Name;
        Description = summary.Description ?? string.Empty;
        Permissions = string.Join(", ", summary.Permissions);
        UserCount = summary.UserCount;
        IsAdministrator = summary.IsAdministrator;
    }

    /// <summary>The role's id.</summary>
    public int Id { get; }

    /// <summary>The role's machine-readable code.</summary>
    public string Code { get; }

    /// <summary>The role's name.</summary>
    public string Name { get; }

    /// <summary>What the role is for.</summary>
    public string Description { get; }

    /// <summary>The permissions the role carries, written out.</summary>
    public string Permissions { get; }

    /// <summary>How many accounts carry the role.</summary>
    public int UserCount { get; }

    /// <summary>True for the role that bypasses grant checks.</summary>
    public bool IsAdministrator { get; }
}

/// <summary>
/// The users and roles screen. A manager can see who works in the shop, add somebody, change what
/// somebody may do, and stop somebody working here without losing the record of what they did
/// (PRD §3, §5.3).
/// </summary>
public sealed partial class UsersViewModel : ObservableObject
{
    private readonly IQueryHandler<ListUsersQuery, Result<IReadOnlyList<UserSummary>>> _listUsers;
    private readonly IQueryHandler<ListRolesQuery, Result<IReadOnlyList<RoleSummary>>> _listRoles;
    private readonly ICommandHandler<CreateUserCommand, Result<UserSummary>> _createUser;
    private readonly ICommandHandler<ChangeUserRoleCommand, Result<UserSummary>> _changeRole;
    private readonly ICommandHandler<SetUserActiveCommand, Result<UserSummary>> _setActive;
    private readonly IStrings _strings;
    private readonly LanguageService _languageService;

    [ObservableProperty]
    private UserRowView? _selectedUser;

    [ObservableProperty]
    private RoleRowView? _selectedRole;

    [ObservableProperty]
    private RoleRowView? _newUserRole;

    [ObservableProperty]
    private string _newUsername = string.Empty;

    [ObservableProperty]
    private string _newDisplayName = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the screen.</summary>
    /// <param name="listUsers">Reads the accounts.</param>
    /// <param name="listRoles">Reads the roles.</param>
    /// <param name="createUser">Adds an account.</param>
    /// <param name="changeRole">Changes an account's role.</param>
    /// <param name="setActive">Deactivates an account or brings it back.</param>
    /// <param name="strings">The application's strings.</param>
    /// <param name="languageService">The application's language.</param>
    public UsersViewModel(
        IQueryHandler<ListUsersQuery, Result<IReadOnlyList<UserSummary>>> listUsers,
        IQueryHandler<ListRolesQuery, Result<IReadOnlyList<RoleSummary>>> listRoles,
        ICommandHandler<CreateUserCommand, Result<UserSummary>> createUser,
        ICommandHandler<ChangeUserRoleCommand, Result<UserSummary>> changeRole,
        ICommandHandler<SetUserActiveCommand, Result<UserSummary>> setActive,
        IStrings strings,
        LanguageService languageService)
    {
        _listUsers = listUsers;
        _listRoles = listRoles;
        _createUser = createUser;
        _changeRole = changeRole;
        _setActive = setActive;
        _strings = strings;
        _languageService = languageService;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The accounts on screen.</summary>
    public ObservableCollection<UserRowView> Users { get; } = [];

    /// <summary>The roles on screen.</summary>
    public ObservableCollection<RoleRowView> Roles { get; } = [];

    /// <summary>The screen title.</summary>
    public string Title => _strings[StringKeys.UsersTitle];

    /// <summary>The heading of the accounts list.</summary>
    public string UsersLabel => _strings[StringKeys.UsersList];

    /// <summary>The heading of the roles list.</summary>
    public string RolesLabel => _strings[StringKeys.UsersRoles];

    /// <summary>The label of the column that holds the login name.</summary>
    public string UsernameLabel => _strings[StringKeys.UsersUsername];

    /// <summary>The label of the column that holds the name.</summary>
    public string DisplayNameLabel => _strings[StringKeys.UsersDisplayName];

    /// <summary>The label of the column that holds the role.</summary>
    public string RoleLabel => _strings[StringKeys.UsersRole];

    /// <summary>The label of the column that holds whether the account is active.</summary>
    public string ActiveLabel => _strings[StringKeys.UsersActive];

    /// <summary>The label of the column that holds the permissions of a role.</summary>
    public string PermissionsLabel => _strings[StringKeys.UsersPermissions];

    /// <summary>The label of the button that adds an account.</summary>
    public string AddLabel => _strings[StringKeys.UsersAdd];

    /// <summary>The label of the button that deactivates an account.</summary>
    public string DeactivateLabel => _strings[StringKeys.UsersDeactivate];

    /// <summary>The label of the button that brings an account back.</summary>
    public string ReactivateLabel => _strings[StringKeys.UsersReactivate];

    /// <summary>The label of the button that changes an account's role.</summary>
    public string ChangeRoleLabel => _strings[StringKeys.UsersChangeRole];

    /// <summary>The label of the field for the login name.</summary>
    public string NewUsernameLabel => _strings[StringKeys.UsersNewUsername];

    /// <summary>The label of the field for the name.</summary>
    public string NewDisplayNameLabel => _strings[StringKeys.UsersNewDisplayName];

    /// <summary>The label of the field for the password.</summary>
    public string NewPasswordLabel => _strings[StringKeys.UsersNewPassword];

    /// <summary>The label of the field for the role.</summary>
    public string NewRoleLabel => _strings[StringKeys.UsersNewRole];

    /// <summary>True when an account is picked and the screen can act on it.</summary>
    public bool HasSelection => SelectedUser is not null && !IsBusy;

    /// <summary>Reloads the accounts and the roles.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Result<IReadOnlyList<RoleSummary>> roles = await _listRoles.HandleAsync(new ListRolesQuery(), cancellationToken);

            if (roles.IsFailure)
            {
                StatusMessage = roles.Error.Message;
                return;
            }

            Roles.Clear();

            foreach (RoleSummary summary in roles.Value)
            {
                Roles.Add(new RoleRowView(summary));
            }

            Result<IReadOnlyList<UserSummary>> users = await _listUsers.HandleAsync(new ListUsersQuery(), cancellationToken);

            if (users.IsFailure)
            {
                StatusMessage = users.Error.Message;
                return;
            }

            Users.Clear();

            foreach (UserSummary summary in users.Value)
            {
                Users.Add(new UserRowView(summary));
            }

            if (NewUserRole is null)
            {
                NewUserRole = Roles.FirstOrDefault();
            }

            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    /// <summary>Adds an account.</summary>
    [RelayCommand]
    private async Task AddUserAsync(CancellationToken cancellationToken)
    {
        if (NewUserRole is null)
        {
            StatusMessage = _strings[StringKeys.UsersNoRole];
            return;
        }

        IsBusy = true;

        try
        {
            Result<UserSummary> result = await _createUser.HandleAsync(
                new CreateUserCommand(NewUsername, NewDisplayName, NewPassword, NewUserRole.Id),
                cancellationToken);

            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            NewUsername = string.Empty;
            NewDisplayName = string.Empty;
            NewPassword = string.Empty;

            StatusMessage = string.Empty;

            await RefreshAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Changes the picked account's role.</summary>
    [RelayCommand]
    private async Task ChangeRoleAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is null || SelectedRole is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            Result<UserSummary> result = await _changeRole.HandleAsync(
                new ChangeUserRoleCommand(SelectedUser.Id, SelectedRole.Id),
                cancellationToken);

            StatusMessage = result.IsFailure ? result.Error.Message : string.Empty;

            await RefreshAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Deactivates the picked account.</summary>
    [RelayCommand]
    private async Task DeactivateAsync(CancellationToken cancellationToken) => await SetActiveAsync(false, cancellationToken);

    /// <summary>Brings the picked account back.</summary>
    [RelayCommand]
    private async Task ReactivateAsync(CancellationToken cancellationToken) => await SetActiveAsync(true, cancellationToken);

    /// <summary>Deactivates an account, or brings it back.</summary>
    private async Task SetActiveAsync(bool isActive, CancellationToken cancellationToken)
    {
        if (SelectedUser is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            Result<UserSummary> result = await _setActive.HandleAsync(
                new SetUserActiveCommand(SelectedUser.Id, isActive),
                cancellationToken);

            StatusMessage = result.IsFailure ? result.Error.Message : string.Empty;

            await RefreshAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedUserChanged(UserRowView? value) => OnPropertyChanged(nameof(HasSelection));

    partial void OnSelectedRoleChanged(RoleRowView? value) => OnPropertyChanged(nameof(HasSelection));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);
    }
}
