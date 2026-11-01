using System.Text.Json;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;

namespace StoreFlow.Infrastructure.Pos;

/// <summary>
/// How a suspended cart is kept in the database. The lines are stored as plain numbers rather than
/// as the objects the till uses, so that a change to the till's own shapes cannot make a cart that
/// was suspended last week unreadable.
/// </summary>
internal sealed record HeldCartLineStorage(
    int ProductId,
    decimal Quantity,
    decimal UnitPrice,
    int DiscountKind,
    decimal DiscountValue);

/// <summary>The whole of a suspended cart, as it is kept in the database.</summary>
internal sealed record HeldCartStorage(
    IReadOnlyList<HeldCartLineStorage> Lines,
    int InvoiceDiscountKind,
    decimal InvoiceDiscountValue);

/// <summary>Reads and writes the stored shape of a suspended cart.</summary>
internal static class HeldCartStorageCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Writes the cart into the text that goes into the database.</summary>
    /// <param name="lines">The lines of the cart.</param>
    /// <param name="invoiceDiscount">The discount the cart was given.</param>
    /// <returns>The JSON text.</returns>
    public static string Encode(IReadOnlyList<SaleLineInput> lines, DiscountSpec invoiceDiscount)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var stored = new HeldCartStorage(
            lines.Select(line => new HeldCartLineStorage(
                line.ProductId,
                line.Quantity,
                line.UnitPrice.Amount,
                (int)line.LineDiscount.Kind,
                line.LineDiscount.Value)).ToList(),
            (int)invoiceDiscount.Kind,
            invoiceDiscount.Value);

        return JsonSerializer.Serialize(stored, Options);
    }

    /// <summary>Reads the cart back out of the text that came out of the database.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The cart, or an empty one when the text cannot be read.</returns>
    public static HeldCartStorage Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HeldCartStorage([], (int)DiscountKind.Fixed, 0m);
        }

        try
        {
            return JsonSerializer.Deserialize<HeldCartStorage>(json, Options)
                ?? new HeldCartStorage([], (int)DiscountKind.Fixed, 0m);
        }
        catch (JsonException)
        {
            // A cart that cannot be read is a cart that cannot be resumed. It is not a reason to
            // stop the shop from opening the till.
            return new HeldCartStorage([], (int)DiscountKind.Fixed, 0m);
        }
    }

    /// <summary>Turns the stored shape back into the shape the till uses.</summary>
    /// <param name="storage">The stored cart.</param>
    /// <returns>The lines and the discount.</returns>
    public static (IReadOnlyList<SaleLineInput> Lines, DiscountSpec InvoiceDiscount) ToTill(
        HeldCartStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);

        var lines = storage.Lines.Select(line => new SaleLineInput(
            line.ProductId,
            line.Quantity,
            new Money(line.UnitPrice),
            new DiscountSpec((DiscountKind)line.DiscountKind, line.DiscountValue))).ToList();

        return (lines, new DiscountSpec((DiscountKind)storage.InvoiceDiscountKind, storage.InvoiceDiscountValue));
    }
}
