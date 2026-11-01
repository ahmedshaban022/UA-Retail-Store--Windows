using System.Globalization;
using System.Windows.Data;
using StoreFlow.Desktop.Localization;
using StoreFlow.Domain.Enumerations;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Writes the inventory enumerations in the language the application is running in: the kind of a
/// stock movement, the state of a purchase document and the state of a stock count.
/// </summary>
public sealed class InventoryLabelConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        IStrings? strings = App.Host?.Services.GetService(typeof(IStrings)) as IStrings;

        return value switch
        {
            StockMovementType movement => strings is null
                ? movement.ToString()
                : strings[KeyFor(movement)],
            DocumentStatus status => strings is null
                ? status.ToString()
                : strings[KeyFor(status)],
            StockCountStatus status => strings is null
                ? status.ToString()
                : strings[KeyFor(status)],
            bool low => low && strings is not null
                ? strings[StringKeys.InventoryBelowReorder]
                : string.Empty,
            _ => string.Empty,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter only converts in one direction.");

    /// <summary>
    /// The resource key that carries the label of a stock movement kind.
    /// </summary>
    /// <param name="movement">The kind of movement.</param>
    /// <returns>The resource key.</returns>
    public static string KeyFor(StockMovementType movement) => movement switch
    {
        StockMovementType.Opening => StringKeys.InventoryMovementOpening,
        StockMovementType.PurchaseReceipt => StringKeys.InventoryMovementPurchaseReceipt,
        StockMovementType.Sale => StringKeys.InventoryMovementSale,
        StockMovementType.ReturnRestock => StringKeys.InventoryMovementReturnRestock,
        StockMovementType.ReturnDamaged => StringKeys.InventoryMovementReturnDamaged,
        StockMovementType.Adjustment => StringKeys.InventoryMovementAdjustment,
        StockMovementType.CountCorrection => StringKeys.InventoryMovementCountCorrection,
        StockMovementType.Reversal => StringKeys.InventoryMovementReversal,
        _ => StringKeys.InventoryMovementAdjustment,
    };

    /// <summary>
    /// The resource key that carries the label of a purchase document state.
    /// </summary>
    /// <param name="status">The state.</param>
    /// <returns>The resource key.</returns>
    public static string KeyFor(DocumentStatus status) => status switch
    {
        DocumentStatus.Draft => StringKeys.InventoryStatusDraft,
        DocumentStatus.Posted => StringKeys.InventoryStatusPosted,
        DocumentStatus.Reversed => StringKeys.InventoryStatusReversed,
        DocumentStatus.Cancelled => StringKeys.InventoryStatusCancelled,
        _ => StringKeys.InventoryStatusDraft,
    };

    /// <summary>
    /// The resource key that carries the label of a stock count state.
    /// </summary>
    /// <param name="status">The state.</param>
    /// <returns>The resource key.</returns>
    public static string KeyFor(StockCountStatus status) => status switch
    {
        StockCountStatus.Draft => StringKeys.InventoryStatusDraft,
        StockCountStatus.AwaitingApproval => StringKeys.InventoryStatusAwaitingApproval,
        StockCountStatus.Approved => StringKeys.InventoryStatusApproved,
        StockCountStatus.Posted => StringKeys.InventoryStatusPosted,
        StockCountStatus.Cancelled => StringKeys.InventoryStatusCancelled,
        _ => StringKeys.InventoryStatusDraft,
    };
}
