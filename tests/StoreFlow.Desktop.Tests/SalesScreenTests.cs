using System.IO;
using StoreFlow.Application.Sales;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Domain.Money;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// The sales register's own helpers: the error codes the application layer answers with become the
/// localised message the owner reads, and the refund the dialog offers is the refund the ledger
/// will post.
/// </summary>
public sealed class SalesScreenTests
{
    [Fact]
    public void Every_sales_error_code_becomes_a_message_of_its_own()
    {
        var strings = new TestStrings();

        string[] codes =
        [
            SalesErrorCodes.InvoiceNotFound,
            SalesErrorCodes.InvoiceNotPosted,
            SalesErrorCodes.InvoiceAlreadyReversed,
            SalesErrorCodes.InvoiceIsReversal,
            SalesErrorCodes.NoLines,
            SalesErrorCodes.LineNotOnInvoice,
            SalesErrorCodes.InvalidQuantity,
            SalesErrorCodes.QuantityExceedsSold,
            SalesErrorCodes.QuantityExceedsRemaining,
            SalesErrorCodes.ReasonRequired,
            SalesErrorCodes.NothingToReturn,
            SalesErrorCodes.ReversalNotAllowed,
            SalesErrorCodes.RefundTooLarge,
            SalesErrorCodes.StockNotRestored,
        ];

        foreach (string code in codes)
        {
            string message = SalesErrorMessages.Resolve(strings, code);

            message.ShouldNotBe("Something went wrong. Try again.");
            message.ShouldNotBe(code);
        }
    }

    [Fact]
    public void An_unknown_error_code_falls_back_to_the_generic_message()
    {
        var strings = new TestStrings();

        SalesErrorMessages.Resolve(strings, "sales.something_new")
            .ShouldBe("Something went wrong. Try again.");
    }

    [Fact]
    public void The_dialog_refunds_the_quantity_that_was_typed()
    {
        ReturnLineViewModel line = Line(quantityScaled: 3, returnedScaled: 0, total: new Money(135m));

        line.QuantityText = "1";

        line.TryGetQuantity(out decimal quantity).ShouldBeTrue();
        quantity.ShouldBe(1m);

        // A third of 135.00 is the price of one of the three units.
        line.Refund.ShouldBe(new Money(45m));
    }

    [Fact]
    public void The_dialog_refunds_nothing_when_nothing_was_typed()
    {
        ReturnLineViewModel line = Line(quantityScaled: 3, returnedScaled: 0, total: new Money(135m));

        line.QuantityText = "0";

        line.TryGetQuantity(out decimal quantity).ShouldBeTrue();
        quantity.ShouldBe(0m);
        line.Refund.ShouldBe(Money.Zero);
    }

    [Fact]
    public void The_dialog_refuses_more_than_is_left_to_return()
    {
        ReturnLineViewModel line = Line(quantityScaled: 3, returnedScaled: 1, total: new Money(135m));

        line.RemainingQuantity.ShouldBe(2m);

        line.QuantityText = "3";

        line.TryGetQuantity(out _).ShouldBeFalse();
        line.ErrorMessage.ShouldBe("More is being returned than is left to return on that line.");
    }

    [Fact]
    public void The_dialog_refuses_a_quantity_the_unit_cannot_express()
    {
        ReturnLineViewModel line = Line(quantityScaled: 30, returnedScaled: 0, total: new Money(135m), scale: 1);

        line.QuantityText = "2.55";

        line.TryGetQuantity(out _).ShouldBeFalse();
        line.ErrorMessage.ShouldBe("A quantity that the selling unit cannot express cannot be returned.");
    }

    [Fact]
    public void The_register_shows_a_sale_as_positive_and_a_correction_as_negative()
    {
        SalesRules.SignedAmount(SalesDocumentKind.Invoice, new Money(135m)).ShouldBe(new Money(135m));
        SalesRules.SignedAmount(SalesDocumentKind.Return, new Money(45m)).ShouldBe(new Money(-45m));
        SalesRules.SignedAmount(SalesDocumentKind.Reversal, new Money(135m)).ShouldBe(new Money(-135m));
    }

    [Fact]
    public void The_sales_screen_carries_the_identifiers_the_smoke_script_looks_for()
    {
        string? root = FindRepositoryRoot();

        if (root is null)
        {
            return;
        }

        string view = File.ReadAllText(Path.Combine(root, "src", "StoreFlow.Desktop", "Views", "SalesView.xaml"));
        string returnDialog = File.ReadAllText(Path.Combine(root, "src", "StoreFlow.Desktop", "Views", "ReturnDialogWindow.xaml"));
        string reverseDialog = File.ReadAllText(Path.Combine(root, "src", "StoreFlow.Desktop", "Views", "ReverseDialogWindow.xaml"));

        foreach (string id in new[]
        {
            "SalesView", "SalesSearch", "SalesFrom", "SalesTo", "SalesCashier", "SalesRefresh",
            "SalesRegister", "SalesDetail", "SalesStatus", "SalesReturn", "SalesReverse",
        })
        {
            view.ShouldContain($"AutomationProperties.AutomationId=\"{id}\"");
        }

        foreach (string id in new[]
        {
            "ReturnDialog", "ReturnDialogLines", "ReturnDialogQuantity", "ReturnDialogReason",
            "ReturnDialogMethod", "ReturnDialogTotal", "ReturnDialogPost", "ReturnDialogCancel",
        })
        {
            returnDialog.ShouldContain($"AutomationProperties.AutomationId=\"{id}\"");
        }

        foreach (string id in new[]
        {
            "ReverseDialog", "ReverseDialogReason", "ReverseDialogMethod", "ReverseDialogPost", "ReverseDialogCancel",
        })
        {
            reverseDialog.ShouldContain($"AutomationProperties.AutomationId=\"{id}\"");
        }
    }

    /// <summary>Builds a row of the return dialog over a line of an invoice.</summary>
    /// <param name="quantityScaled">The quantity that was sold.</param>
    /// <param name="returnedScaled">The quantity that has already come back.</param>
    /// <param name="total">What the customer paid for the line.</param>
    /// <param name="scale">The precision of the selling unit.</param>
    /// <returns>The row.</returns>
    private static ReturnLineViewModel Line(long quantityScaled, long returnedScaled, Money total, int scale = 0)
    {
        var line = new SalesLineDetails(
            1,
            1,
            "Rice 1kg",
            "RICE-001",
            "Piece",
            SalesRules.Unscale(quantityScaled, scale),
            quantityScaled,
            scale,
            returnedScaled,
            new Money(45m),
            Money.Zero,
            Money.Zero,
            total,
            new Money(38m));

        long remaining = SalesRules.RemainingQuantity(quantityScaled, returnedScaled);

        return new ReturnLineViewModel(line, remaining, "Resellable", "Damaged", new TestStrings());
    }

    /// <summary>Finds the repository, so the test can read the views.</summary>
    /// <returns>The root of the repository, or null when it cannot be found.</returns>
    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreFlow.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// A stand-in for the application's strings, so the test does not need a resource manager.
    /// </summary>
    private sealed class TestStrings : IStrings
    {
        private static readonly Dictionary<string, string> Messages = new()
        {
            [StringKeys.ErrorSalesInvalidQuantity] = "A quantity that the selling unit cannot express cannot be returned.",
            [StringKeys.ErrorSalesQuantityExceedsRemaining] = "More is being returned than is left to return on that line.",
            [StringKeys.ErrorSalesQuantityExceedsSold] = "More is being returned than was sold on that line.",
            [StringKeys.ErrorSalesReasonRequired] = "A return must say why the goods came back.",
            [StringKeys.ErrorSalesNoLines] = "A return must have at least one line.",
            [StringKeys.ErrorSalesInvoiceNotFound] = "This invoice no longer exists.",
            [StringKeys.ErrorSalesInvoiceNotPosted] = "Only a posted invoice can be returned against.",
            [StringKeys.ErrorSalesInvoiceAlreadyReversed] = "This invoice has already been reversed.",
            [StringKeys.ErrorSalesInvoiceIsReversal] = "A reversal cannot be reversed again.",
            [StringKeys.ErrorSalesLineNotOnInvoice] = "One of the lines is not on this invoice.",
            [StringKeys.ErrorSalesNothingToReturn] = "There is nothing left to return on this invoice.",
            [StringKeys.ErrorSalesReversalNotAllowed] = "This invoice cannot be reversed.",
            [StringKeys.ErrorSalesRefundTooLarge] = "The refund cannot be larger than what was paid.",
            [StringKeys.ErrorSalesStockNotRestored] = "The stock could not be put back on the shelf.",
            [StringKeys.ErrorUnexpected] = "Something went wrong. Try again.",
        };

        public string this[string key] => Messages.TryGetValue(key, out string? message) ? message : key;

        public string Format(string key, params object?[] arguments) => this[key];
    }
}
