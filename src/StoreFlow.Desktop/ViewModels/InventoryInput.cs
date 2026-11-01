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
    /// <remarks>
    /// The number is read the same way in every language, so "12.5" means the same on an Arabic
    /// screen and on an English one. Two marks are understood as well: the decimal comma, which is
    /// what a keyboard set to a comma locale writes, and the Arabic separators, which are the same
    /// marks written differently. A comma with three digits behind it is still a thousands
    /// separator, so "1,200" is twelve hundred and not one and a bit.
    /// </remarks>
    /// <param name="text">The text the owner typed.</param>
    /// <param name="value">The number that was read.</param>
    /// <returns>True when the text is a number.</returns>
    public static bool TryReadDecimal(string? text, out decimal value)
    {
        string trimmed = (text ?? string.Empty).Trim();

        // The Arabic keyboard writes its own decimal and thousands marks.
        trimmed = trimmed.Replace('٫', '.').Replace('٬', ',');

        if (HasDecimalComma(trimmed))
        {
            return decimal.TryParse(
                trimmed.Replace(",", "."),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value);
        }

        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// True when the comma in the text is a decimal comma rather than a thousands separator.
    /// </summary>
    /// <param name="text">The text the owner typed.</param>
    /// <returns>True when the comma stands for a decimal point.</returns>
    private static bool HasDecimalComma(string text)
    {
        if (text.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        int comma = text.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0 || text.IndexOf(',', comma + 1) >= 0)
        {
            return false;
        }

        string behind = text[(comma + 1)..];

        return behind.Length is 1 or 2 && behind.All(char.IsAsciiDigit);
    }

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
