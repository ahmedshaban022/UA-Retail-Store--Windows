using StoreFlow.Application.Common;
using StoreFlow.Domain.Pricing;

namespace StoreFlow.Application.Pos;

/// <summary>
/// The reads the till needs: the product a barcode names, the products a search finds, and the
/// suspended carts waiting to be picked up.
/// </summary>

/// <summary>Finds the product a scanned barcode belongs to.</summary>
/// <param name="Barcode">The scanned code.</param>
public sealed record LookupProductByBarcodeQuery(string Barcode) : IQuery<Result<PosProductFacts>>;

/// <summary>Finds the products a search term matches, so the cashier can add one by hand.</summary>
/// <param name="Search">Free text matched against the name, the code and the barcode.</param>
/// <param name="Limit">The most rows to return.</param>
public sealed record SearchSellableProductsQuery(string Search, int Limit = 20)
    : IQuery<Result<IReadOnlyList<PosProductFacts>>>;

/// <summary>Lists the suspended carts.</summary>
public sealed record ListHeldCartsQuery() : IQuery<Result<IReadOnlyList<HeldCartSummary>>>;

/// <summary>Reads one suspended cart, so the till can put it back together.</summary>
/// <param name="HeldCartId">The suspended cart to read.</param>
public sealed record GetHeldCartQuery(int HeldCartId) : IQuery<Result<HeldCartDetails>>;

/// <summary>A suspended cart and everything that was in it.</summary>
/// <param name="Summary">The cart's name, its size and what it is worth.</param>
/// <param name="Lines">The lines it holds.</param>
/// <param name="InvoiceDiscount">The discount it was given.</param>
public sealed record HeldCartDetails(
    HeldCartSummary Summary,
    IReadOnlyList<SaleLineInput> Lines,
    DiscountSpec InvoiceDiscount);
