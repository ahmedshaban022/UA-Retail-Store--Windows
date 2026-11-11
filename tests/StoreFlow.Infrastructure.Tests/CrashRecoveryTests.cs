using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>A forced shutdown leaves the sale either complete or absent on the next start.</summary>
public sealed class CrashRecoveryTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture = new();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        var now = new TestClock().UtcNow;
        var session = new TestSession();
        var authorization = new AllowAllAuthorization();
        (await authorization.RequireAsync(Permission.SalesPriceOverride)).IsSuccess.ShouldBeTrue();
        var user = new User
        {
            Username = "crash-probe", DisplayName = session.DisplayName, PasswordHash = "test",
            RoleId = (await context.Roles.FirstAsync(role => role.Code == "owner")).Id,
            CreatedUtc = now, UpdatedUtc = now,
        };
        var product = new Product
        {
            Sku = "CRASH-PROBE", Name = "Crash probe product",
            UnitId = (await context.Units.FirstAsync(unit => unit.Precision == 0)).Id,
            TaxCategoryId = (await context.TaxCategories.FirstAsync()).Id,
            SalePrice = new Money(100m), CostPrice = new Money(50m), CreatedUtc = now, UpdatedUtc = now,
        };
        context.Users.Add(user);
        context.Products.Add(product);
        context.StockBalances.Add(new StockBalance
        {
            Product = product, QuantityOnHandScaled = 10, AverageUnitCost = new Money(50m), UpdatedUtc = now,
        });
        await context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>A sale interrupted before commit disappears and the till starts with its stock intact.</summary>
    [Fact]
    public async Task A_sale_killed_before_commit_is_rolled_back()
    {
        await KillProbeAsync("write-and-wait");
        await new DatabaseInitializer(_fixture.Factory, new TestClock()).InitializeAsync();
        await CheckLedgerAsync(0, 10);
    }

    /// <summary>A sale committed before shutdown survives with all its ledger entries.</summary>
    [Fact]
    public async Task A_sale_killed_after_commit_survives()
    {
        await KillProbeAsync("write-and-commit");
        await new DatabaseInitializer(_fixture.Factory, new TestClock()).InitializeAsync();
        await CheckLedgerAsync(1, 9);
    }

    private async Task KillProbeAsync(string mode)
    {
        string probe = Path.Combine(AppContext.BaseDirectory, "StoreFlow.CrashProbe.dll");
        File.Exists(probe).ShouldBeTrue($"Crash probe was not built beside the test assembly: {probe}");
        string marker = Path.Combine(Path.GetDirectoryName(_fixture.DatabasePath) ?? throw new InvalidOperationException(), "ready");
        var start = new ProcessStartInfo
        {
            FileName = DotnetExecutable(),
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "StoreFlow.Infrastructure.Tests.runtimeconfig.json"));
        start.ArgumentList.Add("--depsfile");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "StoreFlow.Infrastructure.Tests.deps.json"));
        start.ArgumentList.Add(probe);
        start.ArgumentList.Add("--db");
        start.ArgumentList.Add(_fixture.DatabasePath);
        start.ArgumentList.Add("--mode");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add("--ready");
        start.ArgumentList.Add(marker);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the crash probe.");
        Task<string> errors = process.StandardError.ReadToEndAsync();
        try
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(marker) && !process.HasExited && timer.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(50);
            }

            if (!File.Exists(marker))
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }

                throw new InvalidOperationException($"Crash probe did not reach the marker. {await errors}");
            }

            process.HasExited.ShouldBeFalse("The probe exited before it could be killed.");
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    /// <summary>
    /// The dotnet that runs this test. The probe is a process of its own, so it needs the muxer
    /// of an installed .NET, and the folder that holds it is asked of the machine rather than
    /// assumed: a path that exists on one computer and nowhere else fails the test for a reason
    /// that has nothing to do with the crash.
    /// </summary>
    private static string DotnetExecutable()
    {
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            string installed = Path.Combine(root, "dotnet.exe");
            if (File.Exists(installed))
            {
                return installed;
            }
        }

        // The runtime that is running this test knows where its own installation is, and the
        // muxer sits at the root of that installation.
        DirectoryInfo? installation = new(RuntimeEnvironment.GetRuntimeDirectory());
        for (int level = 0; level < 3 && installation is not null; level++)
        {
            installation = installation.Parent;
        }

        if (installation is not null)
        {
            string installed = Path.Combine(installation.FullName, "dotnet.exe");
            if (File.Exists(installed))
            {
                return installed;
            }
        }

        throw new InvalidOperationException(
            "The crash probe needs the dotnet muxer of an installed .NET and none was found. "
            + "Set DOTNET_ROOT to the folder that holds dotnet.exe, or install the .NET SDK.");
    }

    private async Task CheckLedgerAsync(int expected, long stock)
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();
        Product product = await context.Products.SingleAsync(item => item.Sku == "CRASH-PROBE");
        (await context.StockBalances.SingleAsync(item => item.ProductId == product.Id)).QuantityOnHandScaled.ShouldBe(stock);
        (await context.SalesInvoices.CountAsync()).ShouldBe(expected);
        (await context.SalesInvoiceLines.CountAsync()).ShouldBe(expected);
        (await context.Payments.CountAsync()).ShouldBe(expected);
        (await context.StockMovements.CountAsync()).ShouldBe(expected);
        if (expected == 1)
        {
            SalesInvoice invoice = await context.SalesInvoices.SingleAsync();
            SalesInvoiceLine line = await context.SalesInvoiceLines.SingleAsync();
            Payment payment = await context.Payments.SingleAsync();
            StockMovement movement = await context.StockMovements.SingleAsync();
            invoice.Status.ShouldBe(DocumentStatus.Posted);
            invoice.Total.ShouldBe(new Money(100m));
            line.SalesInvoiceId.ShouldBe(invoice.Id);
            line.ProductId.ShouldBe(product.Id);
            payment.SalesInvoiceId.ShouldBe(invoice.Id);
            payment.Amount.ShouldBe(new Money(100m));
            movement.DocumentId.ShouldBe(invoice.Id);
            movement.ProductId.ShouldBe(product.Id);
            movement.MovementType.ShouldBe(StockMovementType.Sale);
            movement.QuantityDeltaScaled.ShouldBe(-1);
        }
    }
}

/// <summary>Authorises everything so the test can set up its cashier.</summary>
file sealed class AllowAllAuthorization : IAuthorizationService
{
    public bool IsAllowed(Permission permission) => true;

    public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}

/// <summary>A session with one signed-in user.</summary>
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
