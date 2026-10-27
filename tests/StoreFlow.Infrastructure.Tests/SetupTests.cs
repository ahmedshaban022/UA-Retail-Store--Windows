using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Application.Identity;
using StoreFlow.Application.Security;
using StoreFlow.Application.Settings;
using StoreFlow.Application.Setup;
using StoreFlow.Infrastructure.Identity;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Services;
using StoreFlow.Infrastructure.Setup;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// Covers the first-run setup and the sign-in that follows it, against a real SQLite file.
/// </summary>
/// <remarks>
/// The first-run wizard is the first thing a shop owner ever sees, and the command behind it runs
/// inside an async view-model command, where an exception would otherwise be swallowed. These
/// tests exercise the handlers directly so a failure is loud.
/// </remarks>
public sealed class SetupTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture = new();

    /// <summary>Creates a fresh database for this test.</summary>
    /// <inheritdoc />
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private CompleteSetupCommandHandler CreateSetupHandler(out TestClock clock)
    {
        clock = new TestClock();
        var session = new InMemoryUserSession();
        IAuditWriter auditWriter = new EfAuditWriter(_fixture.Factory, clock, session);

        return new CompleteSetupCommandHandler(_fixture.Factory, new PasswordHasher(), clock, auditWriter);
    }

    private static CompleteSetupCommand SetupCommand(string userName = "owner") => new(
        userName,
        "StoreFlow!2026",
        "Owner",
        new StoreDetails("Al-Masria Store", "Al-Masria Store", "12 Tahrir Street", "+201000000000", null, null),
        StoreSettings.Defaults);

    [Fact]
    public async Task Completing_the_setup_creates_the_administrator_account()
    {
        CompleteSetupCommandHandler handler = CreateSetupHandler(out _);

        Result result = await handler.HandleAsync(SetupCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        var user = await context.Users.SingleAsync();
        user.Username.ShouldBe("owner");
        user.IsActive.ShouldBeTrue();
        user.PasswordHash.ShouldNotBeNullOrWhiteSpace();
        user.PasswordHash.ShouldNotContain("StoreFlow!2026");
    }

    [Fact]
    public async Task Completing_the_setup_records_the_store_details()
    {
        CompleteSetupCommandHandler handler = CreateSetupHandler(out _);

        await handler.HandleAsync(SetupCommand("owner2"), CancellationToken.None);

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        var setting = await context.Settings
            .SingleAsync(item => item.Key == "store.details");

        setting.Value.ShouldContain("Al-Masria Store");
    }

    [Fact]
    public async Task A_weak_password_is_refused()
    {
        CompleteSetupCommandHandler handler = CreateSetupHandler(out _);

        Result result = await handler.HandleAsync(
            SetupCommand() with { Password = "short" },
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(CompleteSetupCommandHandler.WeakPasswordCode);
    }

    [Fact]
    public async Task The_administrator_can_sign_in_after_the_setup()
    {
        CompleteSetupCommandHandler setupHandler = CreateSetupHandler(out TestClock clock);
        await setupHandler.HandleAsync(SetupCommand("owner3"), CancellationToken.None);

        var session = new InMemoryUserSession();
        IAuditWriter auditWriter = new EfAuditWriter(_fixture.Factory, clock, session);
        var authHandler = new AuthenticateCommandHandler(
            _fixture.Factory,
            new PasswordHasher(),
            clock,
            auditWriter);

        Result<UserSession> result = await authHandler.HandleAsync(
            new AuthenticateCommand("owner3", "StoreFlow!2026"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Username.ShouldBe("owner3");
    }

    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        CompleteSetupCommandHandler setupHandler = CreateSetupHandler(out TestClock clock);
        await setupHandler.HandleAsync(SetupCommand("owner4"), CancellationToken.None);

        var session = new InMemoryUserSession();
        IAuditWriter auditWriter = new EfAuditWriter(_fixture.Factory, clock, session);
        var authHandler = new AuthenticateCommandHandler(
            _fixture.Factory,
            new PasswordHasher(),
            clock,
            auditWriter);

        Result<UserSession> result = await authHandler.HandleAsync(
            new AuthenticateCommand("owner4", "wrong-password"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(AuthenticateCommandHandler.InvalidCredentialsCode);
    }
}
