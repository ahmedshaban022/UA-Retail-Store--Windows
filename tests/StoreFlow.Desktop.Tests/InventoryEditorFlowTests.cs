using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Infrastructure;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Theming;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Drives the purchase editor, the stock count window and the stock adjustment window through the
/// real view models, the real composition root and a real database, so a screen that cannot save
/// is caught by the test suite and not only by driving the application by hand.
/// </summary>
public sealed class InventoryEditorFlowTests : IAsyncLifetime
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

        _provider = services.BuildServiceProvider();

        await _provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();

        await SignInAsync();
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
    public async Task The_purchase_editor_saves_and_posts_a_purchase()
    {
        int productId = await CreateProductAsync("RICE-001");

        PurchaseEditorViewModel editor = _provider.GetRequiredService<PurchaseEditorViewModel>();

        await editor.LoadAsync(null, CancellationToken.None);

        editor.AddLineCommand.Execute(null);

        PurchaseLineRow row = editor.Lines.Single();

        row.ProductId = productId;
        row.Quantity = "20";
        row.UnitCost = "380.00";
        row.Tax = "0.00";

        bool closed = false;
        editor.CloseRequested += (_, saved) => closed = saved;

        await editor.SaveAndPostCommand.ExecuteAsync(null);

        closed.ShouldBeTrue("the editor should close once the purchase has been posted");

        await using StoreFlowDbContext context = await CreateContextAsync();

        PurchaseDocument document = await context.PurchaseDocuments.SingleAsync();

        document.Status.ShouldBe(DocumentStatus.Posted);
        document.Number.ShouldStartWith("PUR-");

        (await context.StockMovements.CountAsync()).ShouldBe(1);
        (await context.StockBalances.SingleAsync()).QuantityOnHandScaled.ShouldBe(20);
    }

    [Fact]
    public async Task The_purchase_editor_reports_why_it_could_not_save()
    {
        PurchaseEditorViewModel editor = _provider.GetRequiredService<PurchaseEditorViewModel>();

        await editor.LoadAsync(null, CancellationToken.None);

        editor.AddLineCommand.Execute(null);

        // No product has been chosen on the line, so the document cannot be saved.
        await editor.SaveCommand.ExecuteAsync(null);

        editor.StatusMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_stock_count_window_submits_a_count()
    {
        int productId = await CreateProductAsync("RICE-001");

        StockCountViewModel counter = _provider.GetRequiredService<StockCountViewModel>();

        await counter.LoadAsync(CancellationToken.None);

        counter.AddLineCommand.Execute(null);

        StockCountLineRow row = counter.Lines.Single();

        row.ProductId = productId;
        row.CountedQuantity = "18";

        bool closed = false;
        counter.CloseRequested += (_, saved) => closed = saved;

        await counter.SubmitCommand.ExecuteAsync(null);

        closed.ShouldBeTrue("the count window should close once the count has been submitted");

        await using StoreFlowDbContext context = await CreateContextAsync();

        StockCount count = await context.StockCounts.SingleAsync();

        count.Status.ShouldBe(StockCountStatus.AwaitingApproval);
    }

    [Fact]
    public async Task The_stock_count_window_shows_what_the_books_say_is_on_the_shelf()
    {
        // A stock count is not blind: the counter can see the recorded quantity. The screen has to
        // show it, or the documents that say a count is not blind are describing a screen that
        // does not exist.
        int productId = await CreateProductAsync("RICE-002");
        await RecordOpeningStockAsync(productId, 20);

        StockCountViewModel counter = _provider.GetRequiredService<StockCountViewModel>();

        await counter.LoadAsync(CancellationToken.None);
        counter.AddLineCommand.Execute(null);

        StockCountLineRow row = counter.Lines.Single();

        row.ProductId = productId;

        row.RecordedQuantity.ShouldBe("20");
    }

    [Fact]
    public async Task The_stock_adjustment_window_posts_an_adjustment()
    {
        int productId = await CreateProductAsync("RICE-001");

        await RecordOpeningStockAsync(productId, 20);

        StockAdjustmentViewModel adjuster = _provider.GetRequiredService<StockAdjustmentViewModel>();

        await adjuster.LoadAsync(CancellationToken.None);

        adjuster.ProductId = productId;
        adjuster.QuantityText = "-1";
        adjuster.Reason = "One bag was damaged in the shop";

        bool closed = false;
        adjuster.CloseRequested += (_, saved) => closed = saved;

        await adjuster.SubmitCommand.ExecuteAsync(null);

        closed.ShouldBeTrue("the adjustment window should close once the adjustment has been posted");

        await using StoreFlowDbContext context = await CreateContextAsync();

        (await context.StockMovements.CountAsync()).ShouldBe(2);
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

    private async Task SignInAsync()
    {
        await using StoreFlowDbContext context = await CreateContextAsync();

        Role owner = await context.Roles.FirstAsync(role => role.Code == DatabaseInitializer.OwnerRoleCode);

        var user = new User
        {
            Username = "owner",
            DisplayName = "Owner",
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
}
