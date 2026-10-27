using System.Security.Cryptography;
using System.Text;

namespace StoreFlow.Infrastructure.Catalog;

/// <summary>
/// Builds the audit entries the catalogue writes. The trail records what changed and who changed it;
/// it never records a secret, and it records the previous state as a hash rather than as a copy, so
/// a change can be detected without the trail becoming a second copy of the catalogue.
/// </summary>
internal static class CatalogAudit
{
    /// <summary>The action recorded when a product is created.</summary>
    public const string ProductCreate = "product.create";

    /// <summary>The action recorded when a product is changed.</summary>
    public const string ProductUpdate = "product.update";

    /// <summary>The action recorded when a product is activated or deactivated.</summary>
    public const string ProductActiveChanged = "product.active_changed";

    /// <summary>The action recorded when a category is created.</summary>
    public const string CategoryCreate = "category.create";

    /// <summary>The action recorded when a category is changed.</summary>
    public const string CategoryUpdate = "category.update";

    /// <summary>The action recorded when a category is activated or deactivated.</summary>
    public const string CategoryActiveChanged = "category.active_changed";

    /// <summary>The action recorded when a unit is created.</summary>
    public const string UnitCreate = "unit.create";

    /// <summary>The action recorded when a unit is changed.</summary>
    public const string UnitUpdate = "unit.update";

    /// <summary>The action recorded when a unit is activated or deactivated.</summary>
    public const string UnitActiveChanged = "unit.active_changed";

    /// <summary>The action recorded when a supplier is created.</summary>
    public const string SupplierCreate = "supplier.create";

    /// <summary>The action recorded when a supplier is changed.</summary>
    public const string SupplierUpdate = "supplier.update";

    /// <summary>The action recorded when a supplier is activated or deactivated.</summary>
    public const string SupplierActiveChanged = "supplier.active_changed";

    /// <summary>The action recorded when products are imported.</summary>
    public const string CatalogImport = "catalog.import";

    /// <summary>The action recorded when products are exported.</summary>
    public const string CatalogExport = "catalog.export";

    /// <summary>The separator placed between the parts of a hash, so two different pairs of values
    /// cannot produce the same text.</summary>
    private const char Separator = '\u001F';

    /// <summary>
    /// Hashes the parts of a record that matter, so two versions of the same record can be told apart
    /// without the trail holding either of them.
    /// </summary>
    /// <param name="parts">The values that make up the state.</param>
    /// <returns>The hash, as lower-case hexadecimal.</returns>
    public static string Hash(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var builder = new StringBuilder();

        foreach (string? part in parts)
        {
            builder.Append(part ?? string.Empty);
            builder.Append(Separator);
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
