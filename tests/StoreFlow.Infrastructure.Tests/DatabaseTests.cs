using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Infrastructure.Persistence;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

public sealed class DatabaseTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public DatabaseTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_schema_is_created_and_the_essentials_are_seeded()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        (await context.Roles.CountAsync()).ShouldBeGreaterThanOrEqualTo(4);
        (await context.Units.CountAsync()).ShouldBeGreaterThanOrEqualTo(1);
        (await context.TaxCategories.CountAsync()).ShouldBeGreaterThanOrEqualTo(1);
        (await context.Settings.CountAsync()).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task The_owner_role_holds_every_permission()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Role owner = await context.Roles
            .Include(role => role.RolePermissions)
            .FirstAsync(role => role.Code == DatabaseInitializer.OwnerRoleCode);

        owner.IsAdministrator.ShouldBeTrue();
        owner.RolePermissions.Count.ShouldBe(Enum.GetValues<Domain.Authorization.Permission>().Length);
    }

    [Fact]
    public async Task The_cashier_role_cannot_manage_users()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Role cashier = await context.Roles
            .Include(role => role.RolePermissions)
            .FirstAsync(role => role.Code == DatabaseInitializer.CashierRoleCode);

        cashier.RolePermissions.ShouldNotContain(grant => grant.Permission == Domain.Authorization.Permission.UserManage);
        cashier.RolePermissions.ShouldContain(grant => grant.Permission == Domain.Authorization.Permission.SalesPost);
    }

    [Fact]
    public async Task Foreign_keys_are_enforced_on_every_connection()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        await context.Database.OpenConnectionAsync();
        using System.Data.Common.DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";
        object? result = command.ExecuteScalar();

        ((long)result!).ShouldBe(1L);
    }

    [Fact]
    public async Task The_write_ahead_log_is_enabled()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        await context.Database.OpenConnectionAsync();
        using System.Data.Common.DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        object? result = command.ExecuteScalar();

        ((string)result!).ShouldBe("wal");
    }

    [Fact]
    public async Task The_database_passes_its_own_integrity_check()
    {
        var initializer = new DatabaseInitializer(_fixture.Factory, new TestClock());

        IReadOnlyList<string> messages = await initializer.CheckIntegrityAsync();

        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(message => message == "ok");
    }

    [Fact]
    public async Task A_negative_stock_balance_is_refused_by_the_database()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        int productId = await SeedProductAsync(context);

        context.StockBalances.Add(new StockBalance
        {
            ProductId = productId,
            QuantityOnHandScaled = 5,
            DamagedQuantityScaled = 0,
            QuantityScale = 0,
            Version = 1,
            UpdatedUtc = DateTimeOffset.UnixEpoch,
        });

        await context.SaveChangesAsync();

        await using StoreFlowDbContext second = await _fixture.Factory.CreateDbContextAsync();
        StockBalance balance = await second.StockBalances.FirstAsync(entry => entry.ProductId == productId);
        balance.QuantityOnHandScaled = -1;

        await Should.ThrowAsync<DbUpdateException>(async () => await second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_posted_stock_movement_cannot_be_edited()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        int productId = await SeedProductAsync(context);

        var movement = new StockMovement
        {
            ProductId = productId,
            MovementType = StockMovementType.Opening,
            Disposition = StockDisposition.Sellable,
            QuantityDeltaScaled = 10,
            QuantityScale = 0,
            UnitCost = Domain.Money.Money.FromMinorUnits(1_000),
            RunningBalanceScaled = 10,
            PostedUtc = DateTimeOffset.UnixEpoch,
            BusinessDate = new DateOnly(2026, 1, 1),
        };

        context.StockMovements.Add(movement);

        await context.SaveChangesAsync();

        // The database is shared by the whole class, so the movement is fetched by the id it was
        // given. The first movement in the file would be another test's, and this test would
        // then be about a row it never wrote.
        await using StoreFlowDbContext second = await _fixture.Factory.CreateDbContextAsync();
        StockMovement stored = await second.StockMovements.SingleAsync(entry => entry.Id == movement.Id);
        stored.ProductId.ShouldBe(productId);
        stored.QuantityDeltaScaled.ShouldBe(10);

        stored.QuantityDeltaScaled = 999;

        await Should.ThrowAsync<DbUpdateException>(async () => await second.SaveChangesAsync());
    }

    private static async Task<int> SeedProductAsync(StoreFlowDbContext context)
    {
        int unitId = await context.Units.Select(unit => unit.Id).FirstAsync();
        int taxCategoryId = await context.TaxCategories.Select(taxCategory => taxCategory.Id).FirstAsync();

        var product = new Product
        {
            Sku = $"SKU-{Guid.NewGuid():N}",
            Name = "Test product",
            UnitId = unitId,
            TaxCategoryId = taxCategoryId,
            IsActive = true,
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }
}
