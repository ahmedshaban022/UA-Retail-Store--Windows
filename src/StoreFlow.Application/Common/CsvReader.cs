namespace StoreFlow.Application.Common;

using System.Globalization;
using System.Text;

/// <summary>
/// Reads CSV text. Pure and side-effect free, so it can be tested directly and reused by every
/// importer the application grows (catalogue, purchases, opening balances).
/// </summary>
/// <remarks>
/// <para>The parser follows the common CSV convention rather than the letter of RFC 4180: a field may
/// be quoted, a quoted field may contain the delimiter and newlines, and a doubled quote inside a
/// quoted field is one literal quote. This is what Excel, LibreOffice and every accounting package
/// in the region actually produce.</para>
/// <para>Files are decoded as UTF-8 first, which is what the application's own export writes. A file
/// saved in the legacy Windows Arabic code page is decoded as Windows-1256 instead, because a shop
/// owner exporting from an older system should not have to learn what a byte-order mark is.</para>
/// </remarks>
public static class CsvReader
{
    /// <summary>The delimiters recognised when a file's own separator is not declared.</summary>
    private static readonly char[] CandidateDelimiters = [',', ';', '\t', '|'];

    private static bool _codePagesRegistered;

    /// <summary>
    /// Parses CSV text into rows of fields.
    /// </summary>
    /// <param name="text">The CSV text.</param>
    /// <param name="delimiter">The field delimiter.</param>
    /// <returns>The rows, each holding one value per field.</returns>
    public static IReadOnlyList<string[]> Parse(string text, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(text);

        if (delimiter is '"' or '\r' or '\n')
        {
            throw new ArgumentException("A field delimiter cannot be a quote or a line break.", nameof(delimiter));
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();

        bool inQuotes = false;
        bool rowHasContent = false;
        int index = 0;

        while (index < text.Length)
        {
            char current = text[index];

            if (inQuotes)
            {
                if (current == '"')
                {
                    // A doubled quote inside a quoted field is one literal quote.
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                        continue;
                    }

                    inQuotes = false;
                    index++;
                    continue;
                }

                field.Append(current);
                index++;
                continue;
            }

            if (current == '"')
            {
                inQuotes = true;
                rowHasContent = true;
                index++;
                continue;
            }

            if (current == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                rowHasContent = true;
                index++;
                continue;
            }

            if (current is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                rows.Add(fields.ToArray());
                fields.Clear();
                rowHasContent = false;

                index++;

                // A CRLF pair is one line break, not two.
                if (current == '\r' && index < text.Length && text[index] == '\n')
                {
                    index++;
                }

                continue;
            }

            field.Append(current);
            rowHasContent = true;
            index++;
        }

        // A file that does not end with a line break still has a final row.
        if (rowHasContent || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }

        return rows;
    }

    /// <summary>
    /// Reads and parses a CSV file, detecting both the encoding and the delimiter.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="delimiter">The delimiter to use, or null to detect it.</param>
    /// <returns>The rows, each holding one value per field.</returns>
    public static IReadOnlyList<string[]> ParseFile(string path, char? delimiter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string text = ReadTextFile(path);
        char resolved = delimiter ?? DetectDelimiter(text);

        return Parse(text, resolved);
    }

    /// <summary>
    /// Guesses the delimiter of a CSV file by counting the candidates outside quoted fields on the
    /// first line that carries any.
    /// </summary>
    /// <param name="text">The CSV text.</param>
    /// <returns>The delimiter, defaulting to a comma when the file does not make the choice clear.</returns>
    public static char DetectDelimiter(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string firstLine = FirstMeaningfulLine(text);

        if (string.IsNullOrEmpty(firstLine))
        {
            return ',';
        }

        var counts = new Dictionary<char, int>();

        bool inQuotes = false;

        for (int index = 0; index < firstLine.Length; index++)
        {
            char current = firstLine[index];

            if (current == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes || !CandidateDelimiters.Contains(current))
            {
                continue;
            }

            counts[current] = counts.TryGetValue(current, out int count) ? count + 1 : 1;
        }

        if (counts.Count == 0)
        {
            return ',';
        }

        char best = ',';
        int bestCount = 0;

        foreach (char candidate in CandidateDelimiters)
        {
            if (counts.TryGetValue(candidate, out int count) && count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>
    /// Decodes a text file, honouring a byte-order mark and falling back to the legacy Windows Arabic
    /// code page when the bytes are not valid UTF-8.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The decoded text.</returns>
    public static string ReadTextFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DecodeLegacyWindowsArabic(bytes);
        }
    }

    private static string DecodeLegacyWindowsArabic(byte[] bytes)
    {
        RegisterCodePages();

        try
        {
            return Encoding.GetEncoding(1256).GetString(bytes);
        }
        catch (ArgumentException)
        {
            // The code-page provider is unavailable: fall back to the replacement decoder rather than
            // failing the import outright.
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static void RegisterCodePages()
    {
        if (_codePagesRegistered)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _codePagesRegistered = true;
    }

    private static string FirstMeaningfulLine(string text)
    {
        using var reader = new StringReader(text);

        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Parses a decimal written in a CSV file. The separator is always a point, never a comma,
    /// because a comma would already have split the field.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns>True when the text is a number.</returns>
    public static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return decimal.TryParse(
            text.Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);
    }
}
