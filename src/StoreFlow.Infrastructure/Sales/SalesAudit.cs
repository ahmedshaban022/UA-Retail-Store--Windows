namespace StoreFlow.Infrastructure.Sales;

/// <summary>
/// The names the sales register writes into the audit trail.
/// </summary>
internal static class SalesAudit
{
    /// <summary>A return was posted against a posted invoice.</summary>
    public const string ReturnPost = "sale.return";

    /// <summary>A posted invoice was reversed by a linked document.</summary>
    public const string InvoiceReversal = "sale.reversal";
}
