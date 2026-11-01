using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Application.Common;
using StoreFlow.Application.Pos;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// One line of the cart. The line knows what it is and what it is worth; the screen it is shown on
/// asks it for both.
/// </summary>
public sealed partial class PosLineViewModel : ObservableObject
{
    private readonly Func<Money, string> _formatMoney;

    [ObservableProperty]
    private string _quantityText = "1";

    [ObservableProperty]
    private string _unitPriceText = string.Empty;

    [ObservableProperty]
    private string _discountText = string.Empty;

    [ObservableProperty]
    private bool _isScanChipVisible;

    [ObservableProperty]
    private string _scanChipText = string.Empty;

    /// <summary>Creates the line.</summary>
    /// <param name="product">The product the line sells.</param>
    /// <param name="formatMoney">Formats an amount for the screen.</param>
    public PosLineViewModel(PosProductFacts product, Func<Money, string> formatMoney)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(formatMoney);

        Product = product;
        _formatMoney = formatMoney;

        UnitPriceText = product.SalePrice.Amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>The product the line sells.</summary>
    public PosProductFacts Product { get; }

    /// <summary>
    /// Raised when the cashier changed something on the line that changes what the cart is worth.
    /// The cart listens to it and works the money out again.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The name shown on the line, in the language of the screen.</summary>
    public string Name => Product.Name;

    /// <summary>The stock-keeping code shown on the line.</summary>
    public string Sku => Product.Sku;

    /// <summary>The selling unit shown on the line.</summary>
    public string UnitName => Product.UnitName;

    /// <summary>The quantity on the line, as the cashier typed it.</summary>
    public decimal Quantity =>
        InventoryInput.TryReadDecimal(QuantityText, out decimal value) && value > 0m
            ? value
            : 0m;

    /// <summary>The price charged for one unit, as the cashier typed it.</summary>
    public Money UnitPrice =>
        InventoryInput.TryReadDecimal(UnitPriceText, out decimal value) && value >= 0m
            ? new Money(value)
            : Product.SalePrice;

    /// <summary>The discount on the line, as the cashier typed it.</summary>
    public DiscountSpec Discount => ParseDiscount(DiscountText);

    /// <summary>
    /// Reads a discount the cashier typed. A number with a percent sign is a percentage; a plain
    /// number is an amount.
    /// </summary>
    /// <param name="text">The text the cashier typed.</param>
    /// <returns>The discount.</returns>
    public static DiscountSpec ParseDiscount(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return DiscountSpec.None;
        }

        bool isPercent = trimmed.EndsWith('%');

        if (isPercent)
        {
            trimmed = trimmed[..^1].Trim();
        }

        if (!InventoryInput.TryReadDecimal(trimmed, out decimal value) || value <= 0m)
        {
            return DiscountSpec.None;
        }

        if (isPercent)
        {
            return DiscountSpec.Percentage(Math.Min(value, 100m));
        }

        return DiscountSpec.FixedAmount(value);
    }

    /// <summary>The line as the application layer wants it.</summary>
    public SaleLineInput ToInput() => new(Product.Id, Quantity, UnitPrice, Discount);

    /// <summary>The line total, as the cart priced it.</summary>
    public string TotalText => _formatMoney(Total);

    /// <summary>The line total, as the cart priced it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalText))]
    private Money _total;

    /// <summary>The tax on the line, as the cart priced it.</summary>
    public string TaxText => _formatMoney(Tax);

    /// <summary>The tax on the line, as the cart priced it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaxText))]
    private Money _tax;

    partial void OnQuantityTextChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnUnitPriceTextChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnDiscountTextChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
}
