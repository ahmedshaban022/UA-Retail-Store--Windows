using System.Globalization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Reads the numbers the owner types into the inventory screens. The text is read with the
/// invariant culture so a quantity typed on an Arabic keyboard still means the same number.
/// </summary>
public static class InventoryInput
{
    /// <summary>
    /// Reads a decimal quantity or amount.
    /// </summary>
    /// <param name="text">The text the owner typed.</param>
    /// <param name="value">The number that was read.</param>
    /// <returns>True when the text is a number.</returns>
    public static bool TryReadDecimal(string? text, out decimal value) =>
        decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);

    /// <summary>
    /// Reads an amount that may not be negative.
    /// </summary>
    /// <param name="text">The text the owner typed.</param>
    /// <param name="value">The amount that was read.</param>
    /// <returns>True when the text is an amount of zero or more.</returns>
    public static bool TryReadMoney(string? text, out decimal value)
    {
        if (!TryReadDecimal(text, out value))
        {
            value = 0m;
            return false;
        }

        return value >= 0m;
    }

    /// <summary>
    /// Reads a date written as year-month-day.
    /// </summary>
    /// <param name="text">The text the owner typed.</param>
    /// <param name="date">The date that was read.</param>
    /// <returns>True when the text is a date.</returns>
    public static bool TryReadDate(string? text, out DateOnly date) =>
        DateOnly.TryParse((text ?? string.Empty).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>
    /// Writes a date the way the owner reads it back.
    /// </summary>
    /// <param name="date">The date.</param>
    /// <returns>The text.</returns>
    public static string WriteDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes a number without a trailing fraction the owner did not type.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string WriteNumber(decimal value) =>
        value.ToString("0.################", CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes an amount the owner can edit back: plain digits and the currency's two decimals.
    /// </summary>
    /// <remarks>
    /// The thousands separator is left out on purpose. This is the text that sits in a field the
    /// owner types over, and a separator in the middle of it turns "1200" into "1,200" the moment
    /// the field is read back, which reads as a different number to somebody who writes a decimal
    /// comma. An amount that is only read - a total, a stock value - is written by the theme's own
    /// money format instead, which groups the digits and carries the currency.
    /// </remarks>
    /// <param name="value">The amount.</param>
    /// <returns>The text.</returns>
    public static string WriteAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
