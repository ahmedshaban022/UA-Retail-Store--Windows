using Microsoft.EntityFrameworkCore;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.CrashProbe;

/// <summary>Writes one sale and leaves its connection open for a forced shutdown.</summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length != 6 || args[0] != "--db" || args[2] != "--mode" || args[4] != "--ready" ||
            (args[3] != "write-and-wait" && args[3] != "write-and-commit"))
        {
            throw new ArgumentException("Expected --db <path> --mode <write-and-wait|write-and-commit> --ready <path>.");
        }

        DbContextOptions<StoreFlowDbContext> options = new DbContextOptionsBuilder<StoreFlowDbContext>()
            .UseSqlite($"Data Source={args[1]}")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        await using var context = new StoreFlowDbContext(options);
        Product product = await context.Products.Include(item => item.Unit).SingleAsync(item => item.Sku == "CRASH-PROBE");
        User user = await context.Users.SingleAsync(item => item.Username == "crash-probe");
        StockBalance balance = await context.StockBalances.SingleAsync(item => item.ProductId == product.Id);
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 1, 1);
        var amount = new Money(100m);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var invoice = new SalesInvoice
        {
            Number = "INV-CRASH-PROBE", Status = DocumentStatus.Posted, Subtotal = amount,
            Total = amount, Paid = amount, PaymentMethod = PaymentMethod.Cash, UserId = user.Id,
            BusinessDate = date, PostedUtc = now, ClientOperationId = Guid.NewGuid(),
        };
        invoice.Lines.Add(new SalesInvoiceLine
        {
            ProductId = product.Id, NameSnapshot = product.Name, SkuSnapshot = product.Sku,
            UnitSnapshot = product.Unit?.Name ?? string.Empty, QuantityScaled = 1,
            UnitPrice = amount, Total = amount, UnitCostSnapshot = balance.AverageUnitCost,
        });
        invoice.Payments.Add(new Payment { Method = PaymentMethod.Cash, Amount = amount, CashReceived = amount });
        context.SalesInvoices.Add(invoice);
        await context.SaveChangesAsync();
        balance.QuantityOnHandScaled--;
        balance.Version++;
        balance.UpdatedUtc = now;
        context.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id, MovementType = StockMovementType.Sale,
            Disposition = StockDisposition.Sellable, DocumentId = invoice.Id, QuantityDeltaScaled = -1,
            RunningBalanceScaled = balance.QuantityOnHandScaled, UnitCost = balance.AverageUnitCost,
            UserId = user.Id, BusinessDate = date, PostedUtc = now,
        });
        await context.SaveChangesAsync();
        if (args[3] == "write-and-commit")
        {
            await transaction.CommitAsync();
        }

        await File.WriteAllTextAsync(args[5], "ready");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}
