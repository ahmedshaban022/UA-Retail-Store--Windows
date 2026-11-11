using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Catalog;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Infrastructure;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Theming;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Drives the product editor through the real view model, the real composition root and a real
/// database, so a price written with a decimal comma is proved to reach the shop's books as the
/// number the owner meant, not only as a number the parser happens to like.
/// </summary>
public sealed class ProductEditorNumberInputTests : IAsyncLifetime
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
    public async Task A_price_written_with_a_decimal_comma_is_saved_as_that_price()
    {
        ProductEditorViewModel editor = _provider.GetRequiredService<ProductEditorViewModel>();

        await editor.LoadAsync(null, CancellationToken.None);

        editor.Sku = "RICE-001";
        editor.Name = "Rice 1kg";
        editor.SalePrice = "12,50";
        editor.CostPrice = "10,00";
        editor.ReorderLevel = "5";

        bool closed = false;
        editor.CloseRequested += (_, saved) => closed = saved;

        await editor.SaveCommand.ExecuteAsync(null);

        closed.ShouldBeTrue("the editor should close once the product has been saved");
        editor.SalePriceError.ShouldBeEmpty();
        editor.CostPriceError.ShouldBeEmpty();

        await using StoreFlowDbContext context = await CreateContextAsync();

        Product product = await context.Products.SingleAsync();

        product.SalePrice.Amount.ShouldBe(12.50m);
        product.CostPrice.Amount.ShouldBe(10.00m);
    }

    [Fact]
    public async Task A_price_written_with_a_thousands_separator_is_saved_as_that_price()
    {
        ProductEditorViewModel editor = _provider.GetRequiredService<ProductEditorViewModel>();

        await editor.LoadAsync(null, CancellationToken.None);

        editor.Sku = "RICE-002";
        editor.Name = "Rice 5kg";
        editor.SalePrice = "1,200";
        editor.CostPrice = "1,000.00";
        editor.ReorderLevel = "5";

        bool closed = false;
        editor.CloseRequested += (_, saved) => closed = saved;

        await editor.SaveCommand.ExecuteAsync(null);

        closed.ShouldBeTrue("the editor should close once the product has been saved");
        editor.SalePriceError.ShouldBeEmpty();

        await using StoreFlowDbContext context = await CreateContextAsync();

        Product product = await context.Products.SingleAsync();

        product.SalePrice.Amount.ShouldBe(1200m);
        product.CostPrice.Amount.ShouldBe(1000.00m);
    }

    [Fact]
    public async Task A_price_that_cannot_be_read_is_refused_and_the_product_is_not_saved()
    {
        ProductEditorViewModel editor = _provider.GetRequiredService<ProductEditorViewModel>();

        await editor.LoadAsync(null, CancellationToken.None);

        editor.Sku = "RICE-003";
        editor.Name = "Rice 1kg";
        editor.SalePrice = "1.234,56";
        editor.CostPrice = "10.00";

        bool closed = false;
        editor.CloseRequested += (_, saved) => closed = saved;

        await editor.SaveCommand.ExecuteAsync(null);

        closed.ShouldBeFalse("the editor should not close when the price cannot be read");
        editor.SalePriceError.ShouldNotBeNullOrWhiteSpace();

        await using StoreFlowDbContext context = await CreateContextAsync();

        (await context.Products.CountAsync()).ShouldBe(0);
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
}
