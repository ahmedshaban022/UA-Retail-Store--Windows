namespace StoreFlow.Desktop.Localization;

/// <summary>
/// Keeps an identifier that sits inside a sentence from disturbing the direction of the words
/// around it.
/// </summary>
/// <remarks>
/// <para>A stock-keeping unit code, a barcode and a telephone number are written left to right even
/// when they stand in an Arabic sentence. The Unicode bidirectional algorithm already keeps a run
/// of Latin letters or digits together, but an identifier that ends in a digit and is followed by
/// an Arabic word can still be read as part of that word. Wrapping the identifier in a
/// left-to-right isolate says plainly where the identifier begins and ends, and the isolate is
/// invisible to the reader.</para>
/// <para>Only identifiers are isolated. A name is the owner's own writing and may be Arabic, and
/// forcing it left to right would be worse than the problem it solves.</para>
/// </remarks>
public static class BidiText
{
    /// <summary>The character that opens a left-to-right isolate.</summary>
    private const char LeftToRightIsolate = '\u2066';

    /// <summary>The character that closes an isolate.</summary>
    private const char PopDirectionalIsolate = '\u2069';

    /// <summary>The character that opens a left-to-right embedding.</summary>
    private const char LeftToRightEmbedding = '\u202A';

    /// <summary>The character that closes an embedding.</summary>
    private const char PopDirectionalFormatting = '\u202C';

    /// <summary>
    /// Wraps an identifier so that it keeps its own direction inside a sentence.
    /// </summary>
    /// <param name="value">The identifier. A missing or empty identifier gives an empty string.</param>
    /// <returns>The identifier, wrapped in a left-to-right isolate.</returns>
    public static string Isolate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return string.Concat(LeftToRightIsolate, value, PopDirectionalIsolate);
    }

    /// <summary>
    /// Wraps a piece of text so that it is written left to right inside an Arabic line.
    /// </summary>
    /// <param name="value">The text. A missing or empty text gives an empty string.</param>
    /// <returns>The text, wrapped in a left-to-right embedding.</returns>
    /// <remarks>
    /// The embedding is the older of the two ways of saying this, and it is the one the text engine
    /// honours: the whole of the wrapped text is written left to right, including the bracket or the
    /// space at its end, which an isolate does not always carry with it.
    /// </remarks>
    public static string Embed(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return string.Concat(LeftToRightEmbedding, value, PopDirectionalFormatting);
    }
}
