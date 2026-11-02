namespace StoreFlow.Application.Approvals;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;

/// <summary>
/// What is written into an approval request, so that the request can be tied to the exact cart it
/// was asked for.
/// </summary>
/// <param name="Fingerprint">The fingerprint of the cart the discount applies to.</param>
/// <param name="DiscountPercent">The discount, as a percentage.</param>
/// <param name="DiscountMinor">The money the discount gives away, in minor units.</param>
/// <param name="CartTotalMinor">The total of the cart, in minor units.</param>
/// <param name="LineCount">How many lines the cart has.</param>
public sealed record DiscountApprovalPayload(
    string Fingerprint,
    decimal DiscountPercent,
    long DiscountMinor,
    long CartTotalMinor,
    int LineCount);

/// <summary>
/// The rules that tie an approval to the cart it was asked for.
/// </summary>
/// <remarks>
/// An approval is not a blank cheque. It says that <i>this</i> discount, on <i>this</i> cart, was
/// agreed. The fingerprint is what makes that true: if the cashier changes a line, a price or the
/// discount after the request was made, the fingerprint no longer matches and the approval does not
/// apply, so the request has to be made again.
/// </remarks>
public static class DiscountApproval
{
    /// <summary>
    /// A fingerprint of what the cashier asked for: the lines, their prices and their discounts,
    /// and the discount on the whole sale.
    /// </summary>
    /// <param name="lines">The lines of the cart.</param>
    /// <param name="invoiceDiscount">The discount granted on the whole sale.</param>
    public static string CartFingerprint(IReadOnlyList<SaleLineInput> lines, DiscountSpec invoiceDiscount)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var builder = new StringBuilder();

        builder.Append(invoiceDiscount.Kind).Append(':')
            .Append(invoiceDiscount.Value.ToString("0.####", CultureInfo.InvariantCulture));

        foreach (SaleLineInput line in lines.OrderBy(line => line.ProductId))
        {
            builder.Append('|').Append(line.ProductId).Append(':')
                .Append(line.Quantity.ToString("0.####", CultureInfo.InvariantCulture)).Append(':')
                .Append(line.UnitPrice.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(':')
                .Append(line.LineDiscount.Kind).Append(':')
                .Append(line.LineDiscount.Value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexString(hash);
    }

    /// <summary>Writes the payload of an approval request.</summary>
    public static string WritePayload(DiscountApprovalPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(payload, PayloadJson);
    }

    /// <summary>Reads the payload of an approval request.</summary>
    /// <param name="json">The payload as it was written.</param>
    public static DiscountApprovalPayload? ReadPayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DiscountApprovalPayload>(json, PayloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
