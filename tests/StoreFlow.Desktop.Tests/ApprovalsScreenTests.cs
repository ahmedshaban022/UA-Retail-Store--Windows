using System.Globalization;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Theming;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Drives the approvals screen the way <see cref="PosScreenTests"/> drives the till: a real
/// screen, the real composition root and a real database, so that a decision taken on the screen
/// is proved to reach the ledger rather than assumed to.
/// </summary>
/// <remarks>
/// The suite used to call the approval handler directly, which proved the handler works and said
/// nothing about whether the screen can reach it. An orphaned approval handler leaves the shop
/// waiting for a decision that nothing will ever make.
/// </remarks>
public sealed class ApprovalsScreenTests : IAsyncLifetime
{
    private string _root = string.Empty;
    private ServiceProvider _provider = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "storeflow-desktop-tests", Guid.NewGuid().ToString("N"));

        var paths = new AppPaths(_root);
        paths.EnsureCreated();

        var services = new ServiceCollection();

        services.AddStoreFlowServices(paths, BuiltInThemes.Light);

        // The theme writes into the running WPF application's resources, and a test has no
        // application. The screen only asks the theme to write money, so a stand-in that writes
        // money the same way is enough.
        services.AddSingleton<IThemeService>(new TestThemeService());

        _provider = services.BuildServiceProvider();

        await _provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _provider.Dispose();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A lingering SQLite handle can keep the file locked for a moment.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_adjustment_that_needs_approval_reaches_the_ledger_when_the_screen_approves_it()
    {
        // The cashier is the one who raises the adjustment.
        await SignInAsync("cashier", "Cashier");

        int productId = await CreateProductAsync("RICE-001");

        await RecordOpeningStockAsync(productId, 200);

        ICommandHandler<PostStockAdjustmentCommand, Result<StockAdjustmentOutcome>> adjust =
            _provider.GetRequiredService<ICommandHandler<PostStockAdjustmentCommand, Result<StockAdjustmentOutcome>>>();

        Result<StockAdjustmentOutcome> raised = await adjust.HandleAsync(
            new PostStockAdjustmentCommand(
                productId,
                -150m,
                StockDisposition.Sellable,
                new Money(400m),
                "One hundred and fifty bags were damaged in the shop"),
            CancellationToken.None);

        raised.IsFailure.ShouldBeFalse(raised.Error.Message);
        raised.Value.Posted.ShouldBeFalse("a large adjustment waits for a second pair of eyes");
        raised.Value.ApprovalRequestId.ShouldNotBeNull();

        // Nothing has moved yet: the request is waiting for somebody else.
        await using (StoreFlowDbContext before = await CreateContextAsync())
        {
            (await before.StockMovements.CountAsync()).ShouldBe(1);
            (await before.StockBalances.SingleAsync()).QuantityOnHandScaled.ShouldBe(200);
        }

        // A different person opens the approvals screen. Nobody may decide their own request.
        await SignInAsync("manager", "Manager");

        ApprovalsViewModel screen = _provider.GetRequiredService<ApprovalsViewModel>();

        await screen.RefreshCommand.ExecuteAsync(null);

        screen.Requests.ShouldHaveSingleItem();

        ApprovalRequestView request = screen.Requests.Single();

        request.IsPending.ShouldBeTrue();
        request.Details.ShouldContain("150");

        screen.SelectedRequest = request;
        screen.CanDecide.ShouldBeTrue();

        await screen.ApproveCommand.ExecuteAsync(null);

        // The stock moved, and the movement is in the ledger.
        await using StoreFlowDbContext after = await CreateContextAsync();

        (await after.StockMovements.CountAsync()).ShouldBe(2);
        (await after.StockBalances.SingleAsync()).QuantityOnHandScaled.ShouldBe(50);

        StockMovement movement = await after.StockMovements
            .OrderByDescending(entry => entry.Id)
            .FirstAsync();

        movement.MovementType.ShouldBe(StockMovementType.Adjustment);
        movement.QuantityDeltaScaled.ShouldBe(-150);

        // The screen says the request has been given.
        screen.Requests.Single().IsPending.ShouldBeFalse();
    }

    private async Task RecordOpeningStockAsync(int productId, decimal quantity)
    {
        ICommandHandler<RecordOpeningStockCommand, Result<int>> handler =
            _provider.GetRequiredService<ICommandHandler<RecordOpeningStockCommand, Result<int>>>();

        Result<int> recorded = await handler.HandleAsync(
            new RecordOpeningStockCommand(
                [new OpeningStockLine(productId, quantity, new Money(400m))],
                DateOnly.FromDateTime(DateTime.Today),
                "Opening stock"),
            CancellationToken.None);

        recorded.IsFailure.ShouldBeFalse(recorded.Error.Message);
    }

    private async Task<StoreFlowDbContext> CreateContextAsync()
    {
        IDbContextFactory<StoreFlowDbContext> factory =
            _provider.GetRequiredService<IDbContextFactory<StoreFlowDbContext>>();

        return await factory.CreateDbContextAsync();
    }

    /// <summary>Signs a person in, so the shop has somebody to act.</summary>
    /// <param name="username">The person's name in the shop.</param>
    /// <param name="displayName">The name the screen shows.</param>
    private async Task SignInAsync(string username, string displayName)
    {
        await using StoreFlowDbContext context = await CreateContextAsync();

        Role owner = await context.Roles.FirstAsync(role => role.Code == DatabaseInitializer.OwnerRoleCode);

        var user = new User
        {
            Username = username,
            DisplayName = displayName,
            PasswordHash = "argon2id$test",
            RoleId = owner.Id,
            IsActive = true,
            CreatedUtc = DateTimeOffset.UnixEpoch,
            UpdatedUtc = DateTimeOffset.UnixEpoch,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        IUserSession session = _provider.GetRequiredService<IUserSession>();

        session.SignIn(new UserSession(
            user.Id,
            user.Username,
            user.DisplayName,
            new HashSet<Permission>(Enum.GetValues<Permission>()),
            IsAdministrator: true));
    }

    private async Task<int> CreateProductAsync(string sku)
    {
        ICommandHandler<SaveProductCommand, Result<int>> handler =
            _provider.GetRequiredService<ICommandHandler<SaveProductCommand, Result<int>>>();

        Result<int> saved = await handler.HandleAsync(
            new SaveProductCommand(
                null,
                sku,
                "Rice 1kg",
                null,
                null,
                null,
                1,
                1,
                new Money(450m),
                new Money(400m),
                5m,
                null,
                [],
                null,
                true),
            CancellationToken.None);

        saved.IsFailure.ShouldBeFalse(saved.Error.Message);

        return saved.Value;
    }

    /// <summary>The theme, and the money it writes in.</summary>
    private sealed class TestThemeService : IThemeService
    {
        public ThemeDefinition Current { get; private set; } = new();

        public void Apply(ThemeDefinition theme) => Current = theme;

        public string FormatMoney(Money amount) =>
            amount.Amount.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
