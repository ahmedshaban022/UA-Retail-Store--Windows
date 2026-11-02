using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Identity;
using StoreFlow.Application.Security;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Identity;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Services;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The manager's side of the user administration: adding somebody, changing what they may do, and
/// stopping somebody working here without losing the record of what they did (PRD §3, §5.3).
/// </summary>
public sealed class UserAdminTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture = new();

    private AllowAllAuthorization _authorization = null!;
    private EfAuditWriter _auditWriter = null!;
    private TestSession _session = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        _authorization = new AllowAllAuthorization();
        _session = new TestSession();
        _auditWriter = new EfAuditWriter(_fixture.Factory, new TestClock(), _session);

        _session.UserId = await CreateUserAsync("admin", "Shop owner");
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task An_account_is_added_with_the_role_it_was_given()
    {
        int roleId = await RoleIdAsync("cashier");

        Result<UserSummary> created = await CreateAsync("newcomer", "New cashier", "correct-horse", roleId);

        created.IsFailure.ShouldBeFalse(created.Error.Message);
        created.Value.Username.ShouldBe("newcomer");
        created.Value.DisplayName.ShouldBe("New cashier");
        created.Value.RoleCode.ShouldBe("cashier");
        created.Value.IsActive.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        User? saved = await context.Users.FirstOrDefaultAsync(user => user.Username == "newcomer");

        saved.ShouldNotBeNull();

        // The password is never written down in any form a person could read it in.
        saved!.PasswordHash.ShouldNotContain("correct-horse");
        saved.PasswordHash.ShouldStartWith("argon2id$");
    }

    [Fact]
    public async Task A_user_name_that_is_already_taken_is_refused()
    {
        int roleId = await RoleIdAsync("cashier");

        Result<UserSummary> first = await CreateAsync("twice", "First", "correct-horse", roleId);

        first.IsFailure.ShouldBeFalse(first.Error.Message);

        Result<UserSummary> second = await CreateAsync("twice", "Second", "correct-horse", roleId);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(UserAdminErrorCodes.DuplicateUsername);
    }

    [Fact]
    public async Task The_last_administrator_cannot_be_deactivated()
    {
        // The shop would then have nobody who could bring the accounts back. The manager doing it is
        // somebody else, so that the rule about one's own account does not hide this one.
        int ownerId = await RoleIdAsync("owner");
        int cashierId = await RoleIdAsync("cashier");

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        User admin = await context.Users.SingleAsync(user => user.Username == "admin");
        admin.RoleId = ownerId;

        await context.SaveChangesAsync();

        _session.UserId = await CreateUserWithRoleAsync("somebody-else", "Somebody else", cashierId);

        Result<UserSummary> deactivated = await SetActiveAsync(admin.Id, false);

        deactivated.IsFailure.ShouldBeTrue();
        deactivated.Error.Code.ShouldBe(UserAdminErrorCodes.LastAdministrator);
    }

    [Fact]
    public async Task A_signed_in_user_cannot_deactivate_their_own_account()
    {
        // Otherwise the last manager could lock the whole shop out with one click.
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        User admin = await context.Users.SingleAsync(user => user.Username == "admin");

        Result<UserSummary> deactivated = await SetActiveAsync(admin.Id, false);

        deactivated.IsFailure.ShouldBeTrue();
        deactivated.Error.Code.ShouldBe(UserAdminErrorCodes.CannotDeactivateSelf);
    }

    [Fact]
    public async Task An_account_can_be_deactivated_and_brought_back()
    {
        int roleId = await RoleIdAsync("cashier");

        Result<UserSummary> created = await CreateAsync("leaver", "Leaving", "correct-horse", roleId);

        created.IsFailure.ShouldBeFalse(created.Error.Message);

        Result<UserSummary> deactivated = await SetActiveAsync(created.Value.Id, false);

        deactivated.IsFailure.ShouldBeFalse(deactivated.Error.Message);
        deactivated.Value.IsActive.ShouldBeFalse();

        Result<UserSummary> back = await SetActiveAsync(created.Value.Id, true);

        back.IsFailure.ShouldBeFalse(back.Error.Message);
        back.Value.IsActive.ShouldBeTrue();

        // The account is never removed: the trail names the people who did what they did.
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        int accounts = await context.Users.CountAsync(user => user.Username == "leaver");

        accounts.ShouldBe(1);
    }

    [Fact]
    public async Task The_roles_carry_the_permissions_they_were_given()
    {
        Result<IReadOnlyList<RoleSummary>> roles = await new ListRolesQueryHandler(
            _fixture.Factory, _authorization)
            .HandleAsync(new ListRolesQuery(), CancellationToken.None);

        roles.IsFailure.ShouldBeFalse(roles.Error.Message);
        roles.Value.ShouldNotBeEmpty();

        RoleSummary owner = roles.Value.Single(role => role.Code == "owner");

        owner.IsAdministrator.ShouldBeTrue();
        owner.Permissions.ShouldNotBeEmpty();
        owner.UserCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_accounts_are_listed_with_the_role_each_one_carries()
    {
        int roleId = await RoleIdAsync("cashier");

        await CreateAsync("listed", "Listed", "correct-horse", roleId);

        Result<IReadOnlyList<UserSummary>> users = await new ListUsersQueryHandler(
            _fixture.Factory, _authorization)
            .HandleAsync(new ListUsersQuery(), CancellationToken.None);

        users.IsFailure.ShouldBeFalse(users.Error.Message);

        UserSummary listed = users.Value.Single(user => user.Username == "listed");

        listed.RoleCode.ShouldBe("cashier");
        listed.RoleName.ShouldBe("Cashier");
    }

    /// <summary>Adds an account.</summary>
    private async Task<Result<UserSummary>> CreateAsync(
        string username,
        string displayName,
        string password,
        int roleId) =>
        await new CreateUserCommandHandler(
            _fixture.Factory,
            _authorization,
            new PasswordHasher(),
            new TestClock(),
            _auditWriter,
            _session)
            .HandleAsync(
                new CreateUserCommand(username, displayName, password, roleId),
                CancellationToken.None);

    /// <summary>Deactivates an account, or brings it back.</summary>
    private async Task<Result<UserSummary>> SetActiveAsync(int userId, bool isActive) =>
        await new SetUserActiveCommandHandler(
            _fixture.Factory,
            _authorization,
            new TestClock(),
            _auditWriter,
            _session)
            .HandleAsync(new SetUserActiveCommand(userId, isActive), CancellationToken.None);

    /// <summary>Finds a role by its code.</summary>
    private async Task<int> RoleIdAsync(string code)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Role role = await context.Roles.FirstAsync(candidate => candidate.Code == code);

        return role.Id;
    }

    /// <summary>Creates a user.</summary>
    private async Task<int> CreateUserAsync(string username, string displayName) =>
        await CreateUserWithRoleAsync(username, displayName, await RoleIdAsync("owner"));

    /// <summary>Creates a user who carries a given role.</summary>
    private async Task<int> CreateUserWithRoleAsync(string username, string displayName, int roleId)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        var user = new User
        {
            Username = username,
            DisplayName = displayName,
            PasswordHash = "pbkdf2$sha256$1$c2FsdA$aGFzaA==",
            RoleId = roleId,
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        context.Users.Add(user);

        await context.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>Authorises everything, so these tests exercise the rules and not the roles.</summary>
    private sealed class AllowAllAuthorization : IAuthorizationService
    {
        public bool IsAllowed(Permission permission) => true;

        public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>A session with one signed-in user.</summary>
    private sealed class TestSession : IUserSession
    {
        public int UserId { get; set; }

        public string DisplayName { get; set; } = "Tester";

        public bool IsAdministrator { get; set; }

        public UserSession? Current => new(
            UserId,
            "tester",
            DisplayName,
            new HashSet<Permission>(),
            IsAdministrator);

        public bool IsAuthenticated => true;

        public bool IsLocked => false;

        public void SignIn(UserSession session)
        {
        }

        public void SignOut()
        {
        }

        public void Lock()
        {
        }

        public void Unlock()
        {
        }
    }
}
