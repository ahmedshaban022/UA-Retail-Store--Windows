namespace StoreFlow.Infrastructure.Pos;

/// <summary>
/// The names the point of sale writes into the audit trail.
/// </summary>
internal static class PosAudit
{
    /// <summary>A sale was posted.</summary>
    public const string SalePost = "sale.post";

    /// <summary>A cart was suspended.</summary>
    public const string CartHold = "pos.cart_hold";

    /// <summary>A suspended cart was taken back onto the till.</summary>
    public const string CartResume = "pos.cart_resume";

    /// <summary>A suspended cart was discarded.</summary>
    public const string CartClear = "pos.cart_clear";
}
