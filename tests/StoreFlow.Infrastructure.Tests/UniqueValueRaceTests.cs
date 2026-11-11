using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Catalog;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Services;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The shop with two clerks in it: the case where two people save the same unique value at the
/// same moment.
/// </summary>
/// <remarks>
/// <para>
/// A code, a barcode, a purchase number and a count number are all guarded by a unique index, and
/// each one is checked before it is written. The check and the write are not one act, though: two
/// clerks can pass the check together, and the index then refuses the second write.
/// </para>
/// <para>
/// That refusal must be an answer the shop understands — the value is taken, save again — and not
/// an exception that escapes the handler and takes the programme down with it.
/// </para>
/// </remarks>
public sealed class UniqueValueRaceTests : IAsyncLifetime
{
    private string _directory = string.Empty;
    private RaceContextFactory _factory = null!;
    private RacingInjector _races = null!;
    private AllowAllAuthorization _authorization = null!;
    private EfAuditWriter _auditWriter = null!;
    private TestImageStore _imageStore = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "storeflow-race-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _races = new RacingInjector();

        DbContextOptions<StoreFlowDbContext> options = new DbContextOptionsBuilder<StoreFlowDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "storeflow.db")}")
            .AddInterceptors(new SqlitePragmaInterceptor(), _races)
            .Options;

        _factory = new RaceContextFactory(options);

        var initializer = new DatabaseInitializer(_factory, new TestClock());
        await initializer.InitializeAsync();

        _authorization = new AllowAllAuthorization();
        _auditWriter = new EfAuditWriter(_factory, new TestClock(), new NoUserSession());
        _imageStore = new TestImageStore();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A lingering SQLite handle can keep the file locked for a moment.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_code_another_clerk_took_at_the_same_moment_is_refused_rather_than_thrown()
    {
        // The other clerk saves the same code in the moment this clerk is reading the shop's books
        // and before their write. Both checks passed, and the index refuses the second write.
        _races.Arm("Products", () =>
        {
            using StoreFlowDbContext other = _factory.CreateDbContext();

            other.Products.Add(new Product
            {
                Sku = "RICE-001",
                Name = "Rice, from the other till",
                UnitId = DefaultUnitId(),
                TaxCategoryId = DefaultTaxCategoryId(),
                SalePrice = new Money(450m),
                CostPrice = new Money(400m),
                ReorderLevelScaled = 0,
                IsActive = true,
                CreatedUtc = DateTimeOffset.UnixEpoch,
                UpdatedUtc = DateTimeOffset.UnixEpoch,
            });

            other.SaveChanges();
        });

        Result<int> saved = await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        // The shop is told which value is taken, rather than the programme being taken down with
        // the exception.
        saved.IsFailure.ShouldBeTrue();
        saved.Error.Code.ShouldBe(SaveProductCommandHandler.DuplicateSkuCode);

        await using StoreFlowDbContext context = _factory.CreateDbContext();

        (await context.Products.CountAsync(product => product.Sku == "RICE-001")).ShouldBe(1);
    }

    [Fact]
    public async Task A_purchase_number_another_clerk_took_at_the_same_moment_is_refused_rather_than_thrown()
    {
        Result<int> product = await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        int productId = product.Value;

        // The other clerk saves a purchase at the same moment, and the number the shop works out
        // for this one has already been taken.
        _races.Arm("PurchaseDocuments", () =>
        {
            using StoreFlowDbContext other = _factory.CreateDbContext();

            other.PurchaseDocuments.Add(new PurchaseDocument
            {
                Number = "PUR-2026-0001",
                Status = DocumentStatus.Draft,
                BusinessDate = new DateOnly(2026, 1, 1),
                Subtotal = Money.Zero,
                Discount = Money.Zero,
                Tax = Money.Zero,
                Total = Money.Zero,
            });

            other.SaveChanges();
        });

        var handler = new SavePurchaseCommandHandler(
            _factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            new NoUserSession());

        Result<int> saved = await handler.HandleAsync(
            new SavePurchaseCommand(
                null,
                null,
                new DateOnly(2026, 1, 1),
                [new PurchaseLineInput(productId, 10m, new Money(400m), Money.Zero)],
                Money.Zero,
                null),
            CancellationToken.None);

        // The document was not written, and the shop is asked to save it again: the next number is
        // then free, and the document the clerk wrote is still on their screen.
        saved.IsFailure.ShouldBeTrue();
        saved.Error.Code.ShouldBe(InventoryErrorCodes.NumberClash);

        await using StoreFlowDbContext context = _factory.CreateDbContext();

        (await context.PurchaseDocuments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_count_number_another_clerk_took_at_the_same_moment_is_refused_rather_than_thrown()
    {
        Result<int> product = await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        int productId = product.Value;

        // The other clerk saves a count at the same moment, and the number the shop works out for
        // this one has already been taken.
        _races.Arm("StockCounts", () =>
        {
            using StoreFlowDbContext other = _factory.CreateDbContext();

            other.StockCounts.Add(new StockCount
            {
                Number = "CNT-2026-0001",
                Status = StockCountStatus.Draft,
                BusinessDate = new DateOnly(2026, 1, 1),
                CreatedUtc = DateTimeOffset.UnixEpoch,
            });

            other.SaveChanges();
        });

        var handler = new SaveStockCountCommandHandler(
            _factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            new NoUserSession());

        Result<int> saved = await handler.HandleAsync(
            new SaveStockCountCommand(
                null,
                new DateOnly(2026, 1, 1),
                [new StockCountLineInput(productId, 10m)]),
            CancellationToken.None);

        saved.IsFailure.ShouldBeTrue();
        saved.Error.Code.ShouldBe(InventoryErrorCodes.NumberClash);

        await using StoreFlowDbContext context = _factory.CreateDbContext();

        (await context.StockCounts.CountAsync()).ShouldBe(1);
    }

    private async Task<Result<int>> SaveProductAsync(string sku, string name, string? barcode)
    {
        var handler = new SaveProductCommandHandler(
            _factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _imageStore);

        return await handler.HandleAsync(
            new SaveProductCommand(
                null,
                sku,
                name,
                null,
                null,
                null,
                await DefaultUnitIdAsync(),
                await DefaultTaxCategoryIdAsync(),
                new Money(450m),
                new Money(400m),
                0m,
                barcode,
                [],
                null,
                true),
            CancellationToken.None);
    }

    private int DefaultUnitId()
    {
        using StoreFlowDbContext context = _factory.CreateDbContext();

        return context.Units.Where(unit => unit.Name == "Piece").Select(unit => unit.Id).First();
    }

    private int DefaultTaxCategoryId()
    {
        using StoreFlowDbContext context = _factory.CreateDbContext();

        return context.TaxCategories.Select(tax => tax.Id).First();
    }

    private async Task<int> DefaultUnitIdAsync()
    {
        await using StoreFlowDbContext context = _factory.CreateDbContext();

        return await context.Units.Where(unit => unit.Name == "Piece").Select(unit => unit.Id).FirstAsync();
    }

    private async Task<int> DefaultTaxCategoryIdAsync()
    {
        await using StoreFlowDbContext context = _factory.CreateDbContext();

        return await context.TaxCategories.Select(tax => tax.Id).FirstAsync();
    }

    /// <summary>
    /// Takes the part of the second clerk: it saves something in the moment the first clerk is
    /// reading the shop's books, so the unique index refuses the first clerk's write.
    /// </summary>
    /// <remarks>
    /// The second clerk acts after the first clerk's read has run but before their transaction has
    /// opened. The write lock is not held yet, so the second clerk's row lands, and the first
    /// clerk's write is the one the index refuses.
    /// </remarks>
    private sealed class RacingInjector : DbCommandInterceptor
    {
        private string _table = string.Empty;
        private Action? _act;
        private bool _acted;

        /// <summary>Arms the interceptor to act at the first read of one table.</summary>
        /// <param name="table">The table whose first read the second clerk answers.</param>
        /// <param name="act">What the second clerk does.</param>
        public void Arm(string table, Action act)
        {
            _table = table;
            _act = act;
            _acted = false;
        }

        /// <inheritdoc />
        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result)
        {
            Act(command);

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            // The asynchronous path does not fall through to the synchronous one, so the second
            // clerk would never arrive.
            Act(command);

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override object? ScalarExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result)
        {
            Act(command);

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<object?> ScalarExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result,
            CancellationToken cancellationToken = default)
        {
            Act(command);

            return ValueTask.FromResult(result);
        }

        private void Act(DbCommand command)
        {
            if (_acted || _act is null || !command.CommandText.Contains(_table, StringComparison.Ordinal))
            {
                return;
            }

            _acted = true;

            _act();
        }
    }

    /// <summary>A factory whose contexts carry the racing interceptor.</summary>
    private sealed class RaceContextFactory(DbContextOptions<StoreFlowDbContext> options)
        : IDbContextFactory<StoreFlowDbContext>
    {
        public StoreFlowDbContext CreateDbContext() => new(options);
    }

    /// <summary>Authorises everything, so these tests exercise the race and not the roles.</summary>
    private sealed class AllowAllAuthorization : IAuthorizationService
    {
        public bool IsAllowed(Permission permission) => true;

        public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>A session with nobody signed in, so the audit trail records system actions.</summary>
    private sealed class NoUserSession : IUserSession
    {
        public UserSession? Current => null;

        public bool IsAuthenticated => false;

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

    /// <summary>A picture store that keeps the picture in memory, so no file is written by a test.</summary>
    private sealed class TestImageStore : IProductImageStore
    {
        public Task<Result<string>> SaveAsync(string sourceFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string>.Success("products/test.png"));

        public Task<Result> DeleteAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public string ResolvePath(string relativePath) => relativePath;

        public bool Exists(string relativePath) => true;
    }
}
