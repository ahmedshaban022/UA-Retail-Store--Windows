namespace StoreFlow.Application.Reporting;

using StoreFlow.Application.Common;
using StoreFlow.Domain.Money;

/// <summary>
/// Records what was counted in the cash drawer, so that the day's cash can be reconciled.
/// </summary>
/// <remarks>
/// A reconciliation needs two numbers the application cannot work out for itself: what the drawer
/// held when the day began, and what it was counted to hold when the day ended. The shopkeeper
/// counts them; the application keeps them and works out the difference.
/// </remarks>
/// <param name="Date">The day the drawer belongs to.</param>
/// <param name="CashierUserId">The cashier whose drawer it is.</param>
/// <param name="Opening">What the drawer held when the day began, when it has been counted.</param>
/// <param name="Counted">What the drawer was counted to hold at the end of the day.</param>
public sealed record RecordDrawerCashCommand(
    DateOnly Date,
    int CashierUserId,
    Money? Opening,
    Money? Counted) : ICommand<Result>;

/// <summary>What was counted in a cash drawer on a day.</summary>
/// <param name="Date">The day.</param>
/// <param name="CashierUserId">The cashier whose drawer it is.</param>
/// <param name="Opening">What the drawer held when the day began.</param>
/// <param name="Counted">What the drawer was counted to hold at the end of the day.</param>
public sealed record DrawerCashSummary(
    DateOnly Date,
    int CashierUserId,
    Money Opening,
    Money Counted);
