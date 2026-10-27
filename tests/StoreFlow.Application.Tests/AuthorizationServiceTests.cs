using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Authorization;
using Shouldly;

namespace StoreFlow.Application.Tests;

public sealed class AuthorizationServiceTests
{
    private readonly InMemorySession _session = new();
    private readonly AuthorizationService _authorization;

    public AuthorizationServiceTests() => _authorization = new AuthorizationService(_session);

    [Fact]
    public async Task No_user_means_no_access()
    {
        Result result = await _authorization.RequireAsync(Permission.DashboardView);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthorizationService.NotAuthenticatedCode);
    }

    [Fact]
    public async Task A_granted_permission_is_allowed()
    {
        _session.SignIn(new UserSession(1, "cashier", "Cashier", new HashSet<Permission> { Permission.SalesPost }, false));

        (await _authorization.RequireAsync(Permission.SalesPost)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_permission_that_was_not_granted_is_refused()
    {
        _session.SignIn(new UserSession(1, "cashier", "Cashier", new HashSet<Permission> { Permission.SalesPost }, false));

        Result result = await _authorization.RequireAsync(Permission.UserManage);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthorizationService.NotAuthorizedCode);
    }

    [Fact]
    public async Task An_administrator_bypasses_the_grant_matrix()
    {
        _session.SignIn(new UserSession(1, "owner", "Owner", new HashSet<Permission>(), true));

        (await _authorization.RequireAsync(Permission.UserManage)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_locked_session_cannot_act()
    {
        _session.SignIn(new UserSession(1, "owner", "Owner", new HashSet<Permission>(), true));
        _session.Lock();

        Result result = await _authorization.RequireAsync(Permission.DashboardView);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthorizationService.NotAuthenticatedCode);
    }

    private sealed class InMemorySession : IUserSession
    {
        public UserSession? Current { get; private set; }

        public bool IsAuthenticated => Current is not null && !IsLocked;

        public bool IsLocked { get; private set; }

        public void SignIn(UserSession session)
        {
            Current = session;
            IsLocked = false;
        }

        public void SignOut() => Current = null;

        public void Lock() => IsLocked = true;

        public void Unlock() => IsLocked = false;
    }
}
