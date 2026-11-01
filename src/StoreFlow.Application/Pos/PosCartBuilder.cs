using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Domain.Quantity;

namespace StoreFlow.Application.Pos;

/// <summary>
/// Prices a cart, applies the discounts, works out the tax and finds what is wrong with it.
/// </summary>
/// <remarks>
/// <para>This is the heart of the till and it is a pure function: it reads no database and writes
/// none. The handler loads the products, hands them over, and gets back a cart that is either
/// valid or a list of problems with stable codes. The receipt, the invoice and the ledger all
/// carry the numbers this produces, so they cannot disagree.</para>
/// <para>The order of the money is the order the till shows it: subtotal, discount, tax, total,
/// paid, change. The invoice discount is spread across the lines in proportion to what each line
/// is worth, so the tax of every line is computed on the amount that line actually paid, and the
/// shares always add up to the discount exactly.</para>
/// </remarks>
public static class PosCartBuilder
{
    /// <summary>
    /// Prices the cart and works out its money.
    /// </summary>
    /// <param name="lines">The lines the cashier asked for.</param>
    /// <param name="products">What is known about each product, by id.</param>
    /// <param name="invoiceDiscount">The discount granted on the whole sale.</param>
    /// <returns>The priced cart, with its money and any problems.</returns>
    public static PosCart Build(
        IReadOnlyList<SaleLineInput> lines,
        IReadOnlyDictionary<int, PosProductFacts> products,
        DiscountSpec invoiceDiscount)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(products);

        var issues = new List<PosIssue>();

        if (lines.Count == 0)
        {
            issues.Add(new PosIssue(PosErrorCodes.EmptyCart, "The cart is empty."));
            return new PosCart([], EmptyTotals(), issues);
        }

        var seen = new HashSet<int>();
        var drafts = new List<LineDraft>(lines.Count);

        foreach (SaleLineInput line in lines)
        {
            // The catalogue is asked first, so that a line for a product the shop does not sell is
            // reported as that, rather than as a duplicate of a line that was never priced.
            if (!products.TryGetValue(line.ProductId, out PosProductFacts? product))
            {
                issues.Add(new PosIssue(
                    PosErrorCodes.ProductNotFound,
                    "That product is not in the catalogue.",
                    line.ProductId));
                continue;
            }

            if (!seen.Add(line.ProductId))
            {
                issues.Add(new PosIssue(
                    PosErrorCodes.DuplicateProduct,
                    "The same product is on the cart twice.",
                    line.ProductId));
                continue;
            }

            if (!product.IsActive)
            {
                issues.Add(new PosIssue(
                    PosErrorCodes.ProductInactive,
                    "That product has been deactivated and cannot be sold.",
                    line.ProductId));
                continue;
            }

            if (line.UnitPrice.IsNegative)
            {
                issues.Add(new PosIssue(
                    PosErrorCodes.InvalidUnitPrice,
                    "The price of a line cannot be negative.",
                    line.ProductId));
                continue;
            }

            if (line.Quantity <= 0m)
            {
                issues.Add(new PosIssue(
                    PosErrorCodes.InvalidQuantity,
                    "The quantity of a line must be more than zero.",
                    line.ProductId));
                continue;
            }

            if (!PosRules.FitsScale(line.Quantity, product.UnitScale))
            {
                issues.Add(new PosIssue(
                    PosErrorCodes.InvalidQuantity,
                    "The quantity cannot be written in this product's unit.",
                    line.ProductId));
                continue;
            }

            var quantity = new UnitQuantity(line.Quantity, product.UnitScale);

            if (quantity.Value <= 0m)
            {
                issues.Add(new PosIssue(
                    PosErrorCodes.InvalidQuantity,
                    "The quantity of a line must be more than zero.",
                    line.ProductId));
                continue;
            }

            long scaled = quantity.ToScaledLong();

            if (scaled > product.OnHandScaled)
            {
                issues.Add(new PosIssue(
                    PosErrorCodes.InsufficientStock,
                    "There is not enough stock on the shelf for that quantity.",
                    line.ProductId));
                continue;
            }

            decimal grossAmount = MoneyMath.Multiply(line.UnitPrice.Amount, quantity.Value);
            var gross = new Money(grossAmount);

            decimal discountAmount = DiscountCalculator.DiscountAmount(grossAmount, line.LineDiscount);
            var discount = new Money(discountAmount);

            drafts.Add(new LineDraft(product, line, quantity, scaled, gross, discount));
        }

        if (drafts.Count == 0)
        {
            return new PosCart([], EmptyTotals(), issues);
        }

        decimal netTotalAmount = drafts.Sum(draft => (draft.Gross - draft.Discount).Amount);
        var netTotal = new Money(netTotalAmount);

        decimal invoiceDiscountAmount = DiscountCalculator.DiscountAmount(netTotalAmount, invoiceDiscount);
        var invoiceDiscountTotal = new Money(invoiceDiscountAmount);

        IReadOnlyList<Money> shares = Spread(invoiceDiscountTotal, drafts);

        var cartLines = new List<CartLine>(drafts.Count);
        var subtotal = Money.Zero;
        var lineDiscountTotal = Money.Zero;
        var taxTotal = Money.Zero;
        var total = Money.Zero;

        for (int index = 0; index < drafts.Count; index++)
        {
            LineDraft draft = drafts[index];
            PosProductFacts product = draft.Product;

            var net = draft.Gross - draft.Discount;
            var share = shares[index];
            var baseAmount = net - share;

            var taxableBase = PosRules.TaxableBaseOn(
                baseAmount, product.TaxRateBasisPoints, product.PriceIncludesTax);

            var tax = PosRules.TaxOn(baseAmount, product.TaxRateBasisPoints, product.PriceIncludesTax);

            var lineTotal = product.PriceIncludesTax
                ? baseAmount
                : baseAmount + tax;

            cartLines.Add(new CartLine(
                product.Id,
                product.Sku,
                product.Name,
                product.NameAr,
                product.Barcode,
                product.UnitName,
                product.UnitScale,
                draft.Quantity.Value,
                draft.QuantityScaled,
                draft.UnitPrice,
                draft.DiscountSpec,
                draft.Discount,
                draft.Gross,
                net,
                share,
                taxableBase,
                tax,
                lineTotal));

            subtotal += draft.Gross;
            lineDiscountTotal += draft.Discount;
            taxTotal += tax;
            total += lineTotal;
        }

        var totals = new PosTotals(
            subtotal,
            lineDiscountTotal,
            invoiceDiscountTotal,
            lineDiscountTotal + invoiceDiscountTotal,
            taxTotal,
            total,
            Money.Zero,
            Money.Zero);

        if (totals.Total.IsNegative)
        {
            issues.Add(new PosIssue(
                PosErrorCodes.NegativeTotal,
                "The discounts would take the sale below zero."));
        }

        return new PosCart(cartLines, totals, issues);
    }

    /// <summary>
    /// The percentage the whole sale is discounted by, measured against the subtotal. This is the
    /// number the store's discount policy is written against.
    /// </summary>
    /// <param name="subtotal">The subtotal before any discount.</param>
    /// <param name="discount">Every discount given, line and invoice together.</param>
    /// <returns>The effective discount percentage.</returns>
    public static decimal EffectiveDiscountPercent(Money subtotal, Money discount)
    {
        if (subtotal.Amount <= 0m || discount.IsNegative)
        {
            return 0m;
        }

        return MoneyMath.Round(discount.Amount * 100m / subtotal.Amount);
    }

    /// <summary>
    /// True when the cashier is charging something other than the catalogue price.
    /// </summary>
    /// <param name="product">The product being sold.</param>
    /// <param name="unitPrice">The price the cashier is charging.</param>
    /// <returns>True when the price differs from the catalogue price.</returns>
    public static bool IsPriceOverride(PosProductFacts product, Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(product);

        return unitPrice.Amount != product.SalePrice.Amount;
    }

    /// <summary>
    /// Splits an invoice discount across the lines in proportion to what each line is worth. The
    /// shares always add up to the discount exactly, so the invoice reconciles to the piastre.
    /// </summary>
    private static IReadOnlyList<Money> Spread(Money invoiceDiscount, IReadOnlyList<LineDraft> drafts)
    {
        var shares = new Money[drafts.Count];

        decimal netTotal = drafts.Sum(draft => (draft.Gross - draft.Discount).Amount);

        if (invoiceDiscount.IsZero || netTotal <= 0m)
        {
            return shares;
        }

        var allocated = Money.Zero;

        for (int index = 0; index < drafts.Count; index++)
        {
            LineDraft draft = drafts[index];
            decimal net = (draft.Gross - draft.Discount).Amount;

            var share = new Money(MoneyMath.Round(invoiceDiscount.Amount * net / netTotal));

            shares[index] = share;
            allocated += share;
        }

        // The rounding leaves a remainder of a fraction of a piastre. It goes on the dearest line,
        // so the shares still add up to the discount and the line order does not matter.
        Money remainder = invoiceDiscount - allocated;

        if (remainder.IsZero)
        {
            return shares;
        }

        int dearest = 0;

        for (int index = 1; index < drafts.Count; index++)
        {
            if ((drafts[index].Gross - drafts[index].Discount) > (drafts[dearest].Gross - drafts[dearest].Discount))
            {
                dearest = index;
            }
        }

        shares[dearest] += remainder;

        return shares;
    }

    private static PosTotals EmptyTotals() => new(
        Money.Zero,
        Money.Zero,
        Money.Zero,
        Money.Zero,
        Money.Zero,
        Money.Zero,
        Money.Zero,
        Money.Zero);

    /// <summary>A line while it is being priced.</summary>
    private sealed record LineDraft(
        PosProductFacts Product,
        SaleLineInput Input,
        UnitQuantity Quantity,
        long QuantityScaled,
        Money Gross,
        Money Discount)
    {
        public Money UnitPrice => Input.UnitPrice;

        public DiscountSpec DiscountSpec => Input.LineDiscount;
    }
}
