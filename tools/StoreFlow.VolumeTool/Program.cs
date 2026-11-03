using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Application.Reporting;
using StoreFlow.Application.Security;
using StoreFlow.Application.Sales;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure;
using StoreFlow.Infrastructure.Catalog;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Reporting;
using StoreFlow.Infrastructure.Sales;
using StoreFlow.Infrastructure.Services;

namespace StoreFlow.VolumeTool;

/// <summary>
/// Fills a shop with the volume a busy shop reaches, and measures the screens against it.
/// </summary>
/// <remarks>
/// Two commands:
/// <c>seed</c> builds a shop with the number of products and invoices asked for.
/// <c>measure</c> times the queries the screens run against a shop that has already been filled.
///
/// The measurements are of the application's own work — the same query handlers the screens call,
/// against the same database — rather than of a stand-in that behaves similarly.
/// </remarks>
public static class Program
{
    /// <summary>The number of decimal places the quantities are kept at.</summary>
    private const int QuantityScale = 3;

    /// <summary>The first day of the seeded shop.</summary>
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    /// <summary>The moment the seeded shop starts from.</summary>
    private static readonly DateTimeOffset StartMoment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Runs the tool.</summary>
    /// <param name="args">The command and its arguments.</param>
    /// <returns>Zero when everything asked for was done.</returns>
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            WriteUsage();

            return 1;
        }

        try
        {
            Options options = Options.Parse(args.Skip(1));

            return args[0] switch
            {
                "seed" => await SeedAsync(options),
                "measure" => await MeasureAsync(options),
                _ => Fail("unknown command '" + args[0] + "'"),
            };
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }
        catch (FormatException exception)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        WriteUsage();

        return 1;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("StoreFlow.VolumeTool");
        Console.WriteLine();
        Console.WriteLine("  seed    --home <folder> [--products N] [--invoices N]");
        Console.WriteLine("  measure --home <folder> [--repeat N]");
    }

    /// <summary>
    /// Builds a shop with the asked-for number of products and invoices.
    /// </summary>
    /// <param name="options">Where to build it and how big.</param>
    /// <returns>Zero when the shop was built.</returns>
    private static async Task<int> SeedAsync(Options options)
    {
        AppPaths paths = new(options.Home);
        paths.EnsureCreated();

        IDbContextFactory<StoreFlowDbContext> factory = CreateFactory(paths);

        Stopwatch watch = Stopwatch.StartNew();
        await new DatabaseInitializer(factory, new FixedClock()).InitializeAsync();
        Console.WriteLine($"the shop was set up in {watch.ElapsedMilliseconds} ms");

        await using StoreFlowDbContext context = await factory.CreateDbContextAsync();

        int unitId = await context.Units.Select(u => u.Id).FirstOrDefaultAsync();
        int taxId = await context.TaxCategories.Select(t => t.Id).FirstOrDefaultAsync();
        int roleId = await context.Roles.Where(r => r.Code == "owner").Select(r => r.Id).FirstOrDefaultAsync();

        if (unitId == 0 || taxId == 0 || roleId == 0)
        {
            Console.Error.WriteLine("the shop was not set up as expected");

            return 1;
        }

        // The shop has no category and no user until the first-run wizard has been finished. The
        // harness does not run the wizard, so it writes the two the shop cannot trade without.
        DateTimeOffset now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

        Category category = new()
        {
            Name = "General",
            NameAr = "عام",
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        User owner = new()
        {
            Username = "owner",
            DisplayName = "Owner",
            PasswordHash = new PasswordHasher().Hash("owner"),
            RoleId = roleId,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        context.Users.Add(owner);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        int categoryId = category.Id;
        int userId = owner.Id;

        watch.Restart();
        await SeedProductsAsync(context, options.Products, categoryId, unitId, taxId);
        Console.WriteLine($"{options.Products} products in {watch.ElapsedMilliseconds} ms");

        watch.Restart();
        await SeedSalesAsync(context, options.Invoices, userId);
        Console.WriteLine($"{options.Invoices} invoices in {watch.ElapsedMilliseconds} ms");

        Console.WriteLine($"database: {paths.DatabasePath}");
        Console.WriteLine($"size: {new FileInfo(paths.DatabasePath).Length / 1024 / 1024} MB");

        return 0;
    }

    /// <summary>
    /// Times the queries the screens run, against a shop that has already been filled.
    /// </summary>
    /// <param name="options">Which shop, and how many times each measurement is taken.</param>
    /// <returns>Zero when the measurements were taken.</returns>
    private static async Task<int> MeasureAsync(Options options)
    {
        AppPaths paths = new(options.Home);

        if (!File.Exists(paths.DatabasePath))
        {
            Console.Error.WriteLine("there is no shop at " + options.Home);

            return 1;
        }

        IDbContextFactory<StoreFlowDbContext> factory = CreateFactory(paths);

        IUserSession session = new InMemoryUserSession();
        session.SignIn(new UserSession(1, "owner", "Owner", new HashSet<Permission>(), true));
        IAuthorizationService authorization = new AuthorizationService(session);

        var catalogue = new SearchProductsQueryHandler(factory, authorization);
        var register = new ListSalesRegisterQueryHandler(factory, authorization);
        var reports = new EfReportReader(factory, new FixedClock());

        await using StoreFlowDbContext context = await factory.CreateDbContextAsync();

        Console.WriteLine($"products: {await context.Products.CountAsync()}");
        Console.WriteLine($"invoices: {await context.SalesInvoices.CountAsync()}");
        Console.WriteLine($"invoice lines: {await context.SalesInvoiceLines.CountAsync()}");
        Console.WriteLine($"stock movements: {await context.StockMovements.CountAsync()}");
        Console.WriteLine();

        DateOnly today = new(2026, 11, 3);
        DateOnly from = StartDate;
        DateOnly quarter = today.AddDays(-90);

        List<Measurement> measurements =
        [
            await TimeAsync("the product list, the first page", options.Repeat, async () =>
            {
                Result<ProductPage> result = await catalogue.HandleAsync(
                    new SearchProductsQuery(new ProductFilter(), 0, 50), CancellationToken.None);

                if (result.IsFailure)
                {
                    throw new InvalidOperationException(result.Error.Code);
                }
            }),
            await TimeAsync("the product list, a search", options.Repeat, async () =>
            {
                Result<ProductPage> result = await catalogue.HandleAsync(
                    new SearchProductsQuery(new ProductFilter("4242"), 0, 50), CancellationToken.None);

                if (result.IsFailure)
                {
                    throw new InvalidOperationException(result.Error.Code);
                }
            }),
            await TimeAsync("the product list, a deep page", options.Repeat, async () =>
            {
                Result<ProductPage> result = await catalogue.HandleAsync(
                    new SearchProductsQuery(new ProductFilter(), 4000, 50), CancellationToken.None);

                if (result.IsFailure)
                {
                    throw new InvalidOperationException(result.Error.Code);
                }
            }),
            await TimeAsync("the sales register, everything", options.Repeat, async () =>
            {
                Result<IReadOnlyList<SalesRegisterRow>> result = await register.HandleAsync(
                    new ListSalesRegisterQuery(new SalesFilter(string.Empty, null, null)), CancellationToken.None);

                if (result.IsFailure)
                {
                    throw new InvalidOperationException(result.Error.Code);
                }
            }),
            await TimeAsync("the sales register, a quarter", options.Repeat, async () =>
            {
                Result<IReadOnlyList<SalesRegisterRow>> result = await register.HandleAsync(
                    new ListSalesRegisterQuery(new SalesFilter(string.Empty, quarter, today)), CancellationToken.None);

                if (result.IsFailure)
                {
                    throw new InvalidOperationException(result.Error.Code);
                }
            }),
            await TimeAsync("the daily sales report", options.Repeat, async () =>
            {
                IReadOnlyList<DailySalesRow> rows = await reports.ReadDailySalesAsync(
                    new ReportFilter { From = from, To = today }, CancellationToken.None);

                if (rows.Count == 0)
                {
                    throw new InvalidOperationException("the report was empty");
                }
            }),
            await TimeAsync("the sales by product report", options.Repeat, async () =>
            {
                IReadOnlyList<SalesByProductRow> rows = await reports.ReadSalesByProductAsync(
                    new ReportFilter { From = from, To = today }, CancellationToken.None);

                if (rows.Count == 0)
                {
                    throw new InvalidOperationException("the report was empty");
                }
            }),
            await TimeAsync("the stock valuation report", options.Repeat, async () =>
            {
                IReadOnlyList<StockValuationRow> rows = await reports.ReadStockValuationAsync(
                    new ReportFilter { From = from, To = today }, CancellationToken.None);

                if (rows.Count == 0)
                {
                    throw new InvalidOperationException("the report was empty");
                }
            }),
        ];

        Console.WriteLine("| what | median | slowest |");
        Console.WriteLine("|---|---|---|");
        foreach (Measurement measurement in measurements)
        {
            Console.WriteLine($"| {measurement.Name} | {measurement.Median:F0} ms | {measurement.Slowest:F0} ms |");
        }

        return 0;
    }

    private static async Task<Measurement> TimeAsync(string name, int repeat, Func<Task> action)
    {
        List<double> times = [];

        for (int i = 0; i < repeat; i++)
        {
            Stopwatch watch = Stopwatch.StartNew();
            await action();
            watch.Stop();

            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        times.Sort();

        return new Measurement(name, times[times.Count / 2], times[^1]);
    }

    private static IDbContextFactory<StoreFlowDbContext> CreateFactory(AppPaths paths)
    {
        DbContextOptions<StoreFlowDbContext> options =
            new DbContextOptionsBuilder<StoreFlowDbContext>()
                .UseSqlite($"Data Source={paths.DatabasePath}",
                    sqlite => sqlite.MigrationsAssembly(typeof(StoreFlowDbContext).Assembly.FullName))
                .Options;

        return new FixedFactory(options);
    }

    private static async Task SeedProductsAsync(
        StoreFlowDbContext context,
        int count,
        int categoryId,
        int unitId,
        int taxId)
    {
        DateTimeOffset now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

        for (int start = 0; start < count; start += 2000)
        {
            int end = Math.Min(start + 2000, count);

            List<Product> products = [];
            for (int i = start; i < end; i++)
            {
                products.Add(new Product
                {
                    Sku = "SKU-" + i.ToString("D6", CultureInfo.InvariantCulture),
                    Name = "Product " + i.ToString(CultureInfo.InvariantCulture),
                    NameAr = "منتج " + i.ToString(CultureInfo.InvariantCulture),
                    CategoryId = categoryId,
                    UnitId = unitId,
                    TaxCategoryId = taxId,
                    SalePrice = new Money(1m + (i % 500) * 0.25m),
                    CostPrice = new Money(0.6m + (i % 500) * 0.15m),
                    ReorderLevelScaled = 10 * QuantityScale,
                    Barcode = "200" + i.ToString("D10", CultureInfo.InvariantCulture),
                    IsActive = i % 50 != 0,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                });
            }

            context.Products.AddRange(products);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        List<Product> saved = await context.Products.OrderBy(p => p.Id).ToListAsync();

        context.ProductBarcodes.AddRange(saved.Select(p => new ProductBarcode
        {
            ProductId = p.Id,
            Barcode = p.Barcode ?? string.Empty,
            IsPrimary = true,
        }));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.StockBalances.AddRange(saved.Select(p => new StockBalance
        {
            ProductId = p.Id,
            QuantityOnHandScaled = 500 * QuantityScale,
            DamagedQuantityScaled = 0,
            AverageUnitCost = p.CostPrice,
            QuantityScale = QuantityScale,
            Version = 1,
            UpdatedUtc = now,
        }));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task SeedSalesAsync(StoreFlowDbContext context, int count, int userId)
    {
        List<int> productIds = await context.Products.OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();
        List<Product> products = await context.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        DateTimeOffset moment = StartMoment;

        const int batchSize = 500;
        List<SalesInvoice> batch = [];

        for (int i = 0; i < count; i++)
        {
            Product product = products[i % products.Count];
            int lineCount = 1 + (i % 3);

            SalesInvoice invoice = new()
            {
                Number = "INV-" + (i + 1).ToString("D8", CultureInfo.InvariantCulture),
                Status = DocumentStatus.Posted,
                Subtotal = product.SalePrice * lineCount,
                Discount = Money.Zero,
                Tax = Money.Zero,
                Total = product.SalePrice * lineCount,
                Paid = product.SalePrice * lineCount,
                Change = Money.Zero,
                PaymentMethod = PaymentMethod.Cash,
                UserId = userId,
                BusinessDate = DateOnly.FromDateTime(moment.UtcDateTime),
                PostedUtc = moment,
                ClientOperationId = Guid.NewGuid(),
            };

            context.SalesInvoices.Add(invoice);
            batch.Add(invoice);

            moment = moment.AddMinutes(7);

            if (batch.Count < batchSize)
            {
                continue;
            }

            await WriteSalesBatchAsync(context, batch, products);
        }

        if (batch.Count > 0)
        {
            await WriteSalesBatchAsync(context, batch, products);
        }
    }

    /// <summary>
    /// Writes one batch of invoices, with their lines, their payments and their stock movements.
    /// </summary>
    /// <param name="context">The context to write through.</param>
    /// <param name="batch">The invoices of this batch.</param>
    /// <param name="products">The products the invoices are made of.</param>
    /// <returns>A task that completes when the batch is written.</returns>
    private static async Task WriteSalesBatchAsync(
        StoreFlowDbContext context,
        List<SalesInvoice> batch,
        List<Product> products)
    {
        // The invoices are written first so that the lines have something to point at.
        await context.SaveChangesAsync();

        foreach (SalesInvoice invoice in batch)
        {
            int index = int.Parse(invoice.Number[4..], CultureInfo.InvariantCulture) - 1;
            Product product = products[index % products.Count];
            int lineCount = 1 + (index % 3);

            for (int line = 0; line < lineCount; line++)
            {
                context.SalesInvoiceLines.Add(new SalesInvoiceLine
                {
                    SalesInvoiceId = invoice.Id,
                    ProductId = product.Id,
                    NameSnapshot = product.Name,
                    SkuSnapshot = product.Sku,
                    BarcodeSnapshot = product.Barcode,
                    UnitSnapshot = "piece",
                    QuantityScaled = QuantityScale,
                    QuantityScale = QuantityScale,
                    UnitPrice = product.SalePrice,
                    Discount = Money.Zero,
                    InvoiceDiscountShare = Money.Zero,
                    DiscountPercentScaled = 0,
                    Tax = Money.Zero,
                    Total = product.SalePrice,
                    UnitCostSnapshot = product.CostPrice,
                    TaxRateSnapshotBasisPoints = 0,
                    TaxPriceIncludedSnapshot = false,
                    ReturnedQuantityScaled = 0,
                });
            }

            context.Payments.Add(new Payment
            {
                SalesInvoiceId = invoice.Id,
                IsRefund = false,
                Method = PaymentMethod.Cash,
                Amount = product.SalePrice * lineCount,
                CashReceived = product.SalePrice * lineCount,
                Change = Money.Zero,
            });

            context.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                MovementType = StockMovementType.Sale,
                Disposition = StockDisposition.Sellable,
                DocumentId = invoice.Id,
                QuantityDeltaScaled = -QuantityScale,
                QuantityScale = QuantityScale,
                UnitCost = product.CostPrice,
                RunningBalanceScaled = 499 * QuantityScale,
                Reason = "Sale",
                UserId = invoice.UserId,
                PostedUtc = invoice.PostedUtc,
                BusinessDate = invoice.BusinessDate,
            });
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        batch.Clear();
    }

    private sealed record Measurement(string Name, double Median, double Slowest);

    private sealed class Options
    {
        public string Home { get; private set; } = string.Empty;

        public int Products { get; private set; } = 10000;

        public int Invoices { get; private set; } = 100000;

        public int Repeat { get; private set; } = 3;

        public static Options Parse(IEnumerable<string> args)
        {
            Options options = new();
            string[] all = args.ToArray();

            for (int i = 0; i < all.Length; i++)
            {
                switch (all[i])
                {
                    case "--home":
                        options.Home = all[++i];
                        break;
                    case "--products":
                        options.Products = int.Parse(all[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--invoices":
                        options.Invoices = int.Parse(all[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--repeat":
                        options.Repeat = int.Parse(all[++i], CultureInfo.InvariantCulture);
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(options.Home))
            {
                throw new InvalidOperationException("--home is needed");
            }

            return options;
        }
    }

    private sealed class FixedFactory(DbContextOptions<StoreFlowDbContext> options)
        : IDbContextFactory<StoreFlowDbContext>
    {
        public StoreFlowDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset LocalNow => UtcNow;

        public DateOnly LocalToday => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
