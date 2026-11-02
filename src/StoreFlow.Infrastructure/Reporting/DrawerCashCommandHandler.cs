using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Reporting;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.Infrastructure.Reporting;

/// <summary>
/// Keeps what was counted in a cash drawer.
/// </summary>
/// <remarks>
/// The counts are kept in the shop's settings rather than in a table of their own: they are two
/// numbers per cashier per day, they are written once, and the shop's settings table already holds
/// exactly this kind of small piece of shop state.
/// </remarks>
internal static class DrawerCashStore
{
    /// <summary>The prefix of the key a drawer's counts are kept under.</summary>
    private const string KeyPrefix = "cash.drawer.";

    /// <summary>How a drawer's counts are read back: the names are matched whatever their case.</summary>
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>How a drawer's counts are written: the same shape the settings table uses elsewhere.</summary>
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads what was counted in a drawer.</summary>
    /// <param name="context">The database.</param>
    /// <param name="date">The day.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    internal static async Task<DrawerCashSummary> ReadAsync(
        StoreFlowDbContext context,
        DateOnly date,
        int cashierUserId,
        CancellationToken cancellationToken)
    {
        string key = Key(date, cashierUserId);

        Setting? setting = await context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        if (setting is null)
        {
            return new DrawerCashSummary(date, cashierUserId, Money.Zero, Money.Zero);
        }

        try
        {
            // The counts are read back whatever case they were written in: a count written by an
            // older build of the app must not silently read back as nothing counted.
            DrawerCashValues? values = JsonSerializer.Deserialize<DrawerCashValues>(setting.Value, ReadOptions);

            if (values is null)
            {
                return new DrawerCashSummary(date, cashierUserId, Money.Zero, Money.Zero);
            }

            return new DrawerCashSummary(
                date,
                cashierUserId,
                Money.FromMinorUnits(values.Opening),
                Money.FromMinorUnits(values.Counted));
        }
        catch (JsonException)
        {
            return new DrawerCashSummary(date, cashierUserId, Money.Zero, Money.Zero);
        }
    }

    /// <summary>Writes what was counted in a drawer.</summary>
    /// <param name="context">The database.</param>
    /// <param name="date">The day.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="opening">What the drawer held when the day began.</param>
    /// <param name="counted">What the drawer was counted to hold at the end of the day.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    internal static async Task WriteAsync(
        StoreFlowDbContext context,
        DateOnly date,
        int cashierUserId,
        Money? opening,
        Money? counted,
        IClock clock,
        CancellationToken cancellationToken)
    {
        string key = Key(date, cashierUserId);

        DrawerCashSummary existing = await ReadAsync(context, date, cashierUserId, cancellationToken);

        long openingMinor = opening?.ToMinorUnits() ?? existing.Opening.ToMinorUnits();
        long countedMinor = counted?.ToMinorUnits() ?? existing.Counted.ToMinorUnits();

        string value = JsonSerializer.Serialize(new DrawerCashValues(openingMinor, countedMinor), WriteOptions);

        Setting? setting = await context.Settings
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        if (setting is null)
        {
            context.Settings.Add(new Setting { Key = key, Value = value, UpdatedUtc = clock.UtcNow });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedUtc = clock.UtcNow;
        }
    }

    /// <summary>The key a drawer's counts are kept under.</summary>
    private static string Key(DateOnly date, int cashierUserId) =>
        $"{KeyPrefix}{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.{cashierUserId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>What is kept for one drawer.</summary>
    private sealed record DrawerCashValues(long Opening, long Counted);
}

/// <summary>
/// Records what was counted in a cash drawer, so that the day's cash can be reconciled.
/// </summary>
public sealed class RecordDrawerCashCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IClock clock,
    IAuditWriter auditWriter,
    IUserSession session)
    : ICommandHandler<RecordDrawerCashCommand, Result>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(RecordDrawerCashCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.SalesView, cancellationToken);

        if (permitted.IsFailure)
        {
            return permitted;
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        bool cashierExists = await context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == command.CashierUserId, cancellationToken);

        if (!cashierExists)
        {
            return Result.Failure(
                Application.Identity.UserAdminErrorCodes.NotFound,
                "That account does not exist.");
        }

        await DrawerCashStore.WriteAsync(
            context,
            command.Date,
            command.CashierUserId,
            command.Opening,
            command.Counted,
            clock,
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                "cash.drawer_counted",
                nameof(Setting),
                command.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                null,
                $"cashier {command.CashierUserId.ToString(CultureInfo.InvariantCulture)}",
                session.Current?.UserId),
            cancellationToken);

        return Result.Success();
    }
}
