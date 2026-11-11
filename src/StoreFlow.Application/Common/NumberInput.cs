using System.Globalization;

namespace StoreFlow.Application.Common;

/// <summary>
/// Reads the numbers the owner types or imports.
/// </summary>
/// <remarks>
/// <para>
/// The number is read the same way in every language, so "12.5" means the same on an Arabic screen
/// and on an English one. Two marks are understood as well: the decimal comma, which is what a
/// keyboard set to a comma locale writes, and the Arabic separators, which are the same marks
/// written differently.
/// </para>
/// <para>
/// The decimal comma is told from the thousands separator by what follows it: one or two digits
/// after the comma and no full stop anywhere means the comma stands for the decimal point, so
/// "12,50" is twelve and a half. Three or more digits after it means the comma is a thousands
/// separator, so "1,200" is twelve hundred. A value written with both — "1.234,56" — is not
/// readable under one rule and is refused rather than guessed at.
/// </para>
/// <para>
/// This is the one place a number is read. Every screen and every import goes through it, so a
/// price typed as "12,50" cannot mean twelve hundred and a half in one place and twelve and a
/// half in another.
/// </para>
/// </remarks>
public static class NumberInput
{
    /// <summary>
    /// The Arabic decimal mark, written as a full stop.
    /// </summary>
    private const char ArabicDecimalMark = '٫';

    /// <summary>
    /// The Arabic thousands mark, written as a comma.
    /// </summary>
    private const char ArabicThousandsMark = '٬';

    /// <summary>
    /// Reads a decimal quantity or amount.
    /// </summary>
    /// <param name="text">The text the owner typed.</param>
    /// <param name="value">The number that was read.</param>
    /// <returns>True when the text is a number.</returns>
    public static bool TryReadDecimal(string? text, out decimal value)
    {
        string trimmed = (text ?? string.Empty).Trim();

        // The Arabic keyboard writes its own decimal and thousands marks.
        trimmed = trimmed.Replace(ArabicDecimalMark, '.').Replace(ArabicThousandsMark, ',');

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
    /// Reads an amount that may not be negative.
    /// </summary>
    /// <param name="text">The text the owner typed.</param>
    /// <param name="value">The amount that was read.</param>
    /// <returns>True when the text is an amount of zero or more.</returns>
    public static bool TryReadMoney(string? text, out decimal value)
    {
        if (!TryReadDecimal(text, out value) || value < 0m)
        {
            // A refused amount leaves no value behind, so a caller that forgets to check the
            // answer cannot act on a number it was told not to use.
            value = 0m;
            return false;
        }

        return true;
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
}
