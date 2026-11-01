namespace StoreFlow.Application.Pos;

/// <summary>
/// The stable codes the point of sale answers with, so the interface can localise the message
/// instead of guessing at the wording of an English sentence.
/// </summary>
public static class PosErrorCodes
{
    /// <summary>The cart has no lines, so there is nothing to sell.</summary>
    public const string EmptyCart = "pos.empty_cart";

    /// <summary>The product does not exist.</summary>
    public const string ProductNotFound = "pos.product_not_found";

    /// <summary>The product has been deactivated and cannot be sold again.</summary>
    public const string ProductInactive = "pos.product_inactive";

    /// <summary>The same product appears twice on the cart.</summary>
    public const string DuplicateProduct = "pos.duplicate_product";

    /// <summary>The quantity is not one the product's unit can express.</summary>
    public const string InvalidQuantity = "pos.invalid_quantity";

    /// <summary>The unit price is negative or otherwise unusable.</summary>
    public const string InvalidUnitPrice = "pos.invalid_unit_price";

    /// <summary>There is not enough stock on the shelf for the quantity asked for.</summary>
    public const string InsufficientStock = "pos.insufficient_stock";

    /// <summary>The discounts would take the sale below zero.</summary>
    public const string NegativeTotal = "pos.negative_total";

    /// <summary>The discount is above the limit the store's policy allows without an approver.</summary>
    public const string DiscountOverLimit = "pos.discount_over_limit";

    /// <summary>The price differs from the catalogue and the cashier may not change it.</summary>
    public const string PriceOverrideNotAllowed = "pos.price_override_not_allowed";

    /// <summary>The customer has not paid enough.</summary>
    public const string InsufficientPayment = "pos.insufficient_payment";

    /// <summary>The cash handed over is less than the amount it is paying.</summary>
    public const string InsufficientCash = "pos.insufficient_cash";

    /// <summary>The idempotency key was used before with a different cart.</summary>
    public const string IdempotencyConflict = "pos.idempotency_conflict";

    /// <summary>The suspended cart does not exist, or has already been taken.</summary>
    public const string HeldCartNotFound = "pos.held_cart_not_found";

    /// <summary>The suspended cart has no name.</summary>
    public const string HeldCartLabelRequired = "pos.held_cart_label_required";
}
