using Microsoft.EntityFrameworkCore;
using Shouldly;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Reporting;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Reporting;
using StoreFlow.Infrastructure.Sales;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>The shop's long lists must not turn into one SQL parameter per document or product.</summary>
public sealed class QueryParameterBudgetTests : IAsyncLifetime
{
    private const int Budget = 20;
    private const int Count = 300;
    private readonly DatabaseFixture _fixture = new();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        int roleId = await context.Roles.Where(role => role.Code == "owner").Select(role => role.Id).FirstAsync();
        int unitId = await context.Units.Select(unit => unit.Id).FirstAsync();
        int taxId = await context.TaxCategories.Select(tax => tax.Id).FirstAsync();
        var user = new User { Username = "budget", DisplayName = "Budget", PasswordHash = "hash", RoleId = roleId, CreatedUtc = now, UpdatedUtc = now };
        var category = new Category { Name = "Budget", IsActive = true, CreatedUtc = now, UpdatedUtc = now };
        context.AddRange(user, category);
        await context.SaveChangesAsync();
        for (int i = 0; i < Count; i++)
        {
            var product = new Product
            {
                Sku = $"B-{i:D4}", Name = $"Product {i:D4}", CategoryId = category.Id,
                UnitId = unitId, TaxCategoryId = taxId, SalePrice = new Money(10m), CostPrice = new Money(5m),
                IsActive = true, ReorderLevelScaled = 10, CreatedUtc = now, UpdatedUtc = now,
            };
            context.Products.Add(product);
            context.StockBalances.Add(new StockBalance { Product = product, QuantityOnHandScaled = 1, QuantityScale = 0, AverageUnitCost = new Money(5m), Version = 1, UpdatedUtc = now });
            var invoice = new SalesInvoice
            {
                Number = $"INV-{i:D4}", Status = DocumentStatus.Posted, Subtotal = new Money(10m),
                Total = new Money(10m), Paid = new Money(10m), PaymentMethod = PaymentMethod.Cash,
                UserId = user.Id, BusinessDate = new DateOnly(2026, 1, 1), PostedUtc = now, ClientOperationId = Guid.NewGuid(),
            };
            context.SalesInvoices.Add(invoice);
            context.SalesInvoiceLines.Add(new SalesInvoiceLine
            {
                SalesInvoice = invoice, Product = product, NameSnapshot = product.Name, SkuSnapshot = product.Sku,
                UnitSnapshot = "piece", QuantityScaled = 1, QuantityScale = 0, UnitPrice = new Money(10m),
                Total = new Money(10m), UnitCostSnapshot = new Money(5m),
            });
        }
        await context.SaveChangesAsync();
        _fixture.ResetParameterCount();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>The register reads every invoice without a parameter for each one.</summary>
    [Fact]
    public async Task The_sales_register_keeps_its_parameter_count_bounded()
    {
        var handler = new ListSalesRegisterQueryHandler(_fixture.Factory, new AllowAllAuthorization());
        var result = await handler.HandleAsync(new ListSalesRegisterQuery(new SalesFilter(string.Empty, null, null)), CancellationToken.None);
        result.IsFailure.ShouldBeFalse();
        result.Value.Count.ShouldBe(Count);
        Console.WriteLine($"Largest command parameter count: {_fixture.LargestParameterCount}");
        _fixture.LargestParameterCount.ShouldBeLessThanOrEqualTo(Budget);
    }

    /// <summary>The product report reads every line without a parameter for each invoice.</summary>
    [Fact]
    public async Task The_sales_by_product_report_keeps_its_parameter_count_bounded()
    {
        var reader = new EfReportReader(_fixture.Factory, new TestClock());
        var rows = await reader.ReadSalesByProductAsync(new ReportFilter { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 1) });
        rows.Count.ShouldBe(Count);
        Console.WriteLine($"Largest command parameter count: {_fixture.LargestParameterCount}");
        _fixture.LargestParameterCount.ShouldBeLessThanOrEqualTo(Budget);
    }

    /// <summary>The stock lists read every balance without a parameter for each product.</summary>
    [Fact]
    public async Task The_stock_lists_keep_their_parameter_count_bounded()
    {
        var authorization = new AllowAllAuthorization();
        var balances = await new ListStockBalancesQueryHandler(_fixture.Factory, authorization).HandleAsync(new ListStockBalancesQuery(), CancellationToken.None);
        balances.IsFailure.ShouldBeFalse();
        balances.Value.Count.ShouldBe(Count);
        var low = await new ListLowStockQueryHandler(_fixture.Factory, authorization).HandleAsync(new ListLowStockQuery(), CancellationToken.None);
        low.IsFailure.ShouldBeFalse();
        low.Value.Count.ShouldBe(Count);
        Console.WriteLine($"Largest command parameter count: {_fixture.LargestParameterCount}");
        _fixture.LargestParameterCount.ShouldBeLessThanOrEqualTo(Budget);
    }
}

/// <summary>Authorises everything, so these tests exercise the reads and not the roles.</summary>
file sealed class AllowAllAuthorization : IAuthorizationService
{
    public bool IsAllowed(Permission permission) => true;
    public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
}

/// <summary>A session with one signed-in user, so the audit trail names an actor.</summary>
file sealed class TestSession : IUserSession
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "Tester";
    public bool IsAdministrator { get; set; }
    public UserSession? Current => new(UserId, "tester", DisplayName, new HashSet<Permission>(), IsAdministrator);
    public bool IsAuthenticated => true;
    public bool IsLocked => false;
    public void SignIn(UserSession session) { }
    public void SignOut() { }
    public void Lock() { }
    public void Unlock() { }
}
