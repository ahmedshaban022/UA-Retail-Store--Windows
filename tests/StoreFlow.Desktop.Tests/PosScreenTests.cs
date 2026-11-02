using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Approvals;
using StoreFlow.Application.Common;
using StoreFlow.Application.Pos;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.Printing;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Pricing;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// The point of sale, seen from the till: the money on the screen is the money the cart priced,
/// the change comes from the cash the cashier typed, every code the till answers with has a
/// message of its own, and the receipt carries the same numbers as the screen.
/// </summary>
public sealed class PosScreenTests
{
    /// <summary>The language of the screen the tests pretend to be in.</summary>
    private static readonly CultureInfo _english = new("en-US");

    [Fact]
    public void The_money_on_the_till_is_the_money_the_cart_priced()
    {
        PosProductFacts product = Product();

        RunTill(
            till =>
            {
                PosViewModel viewModel = till.ViewModel;

                viewModel.AddFromSearchCommand.Execute(product);
                viewModel.Lines[0].QuantityText = "2";

                viewModel.Lines.ShouldHaveSingleItem();
                viewModel.Totals.Subtotal.Amount.ShouldBe(200m);
                viewModel.Totals.Tax.Amount.ShouldBe(20m);
                viewModel.Totals.Total.Amount.ShouldBe(220m);

                viewModel.SubtotalText.ShouldBe("200.00");
                viewModel.TaxText.ShouldBe("20.00");
                viewModel.TaxableText.ShouldBe("200.00");
                viewModel.TotalText.ShouldBe("220.00");

                // The till has no second opinion about the arithmetic: it shows what the cart priced.
                PosCart cart = PosCartBuilder.Build(
                    viewModel.Lines.Select(line => line.ToInput()).ToList(),
                    Catalogue(product),
                    DiscountSpec.None);

                cart.Totals.ShouldBe(viewModel.Totals);
                viewModel.Lines[0].Total.ShouldBe(cart.Lines[0].Total);
                viewModel.Lines[0].Tax.ShouldBe(cart.Lines[0].Tax);
            },
            product);
    }

    [Fact]
    public void The_taxable_amount_and_the_tax_add_up_to_the_total()
    {
        PosProductFacts product = Product();

        RunTill(
            till =>
            {
                PosViewModel viewModel = till.ViewModel;

                viewModel.AddFromSearchCommand.Execute(product);
                viewModel.Lines[0].QuantityText = "2";

                decimal taxable = decimal.Parse(viewModel.TaxableText, CultureInfo.InvariantCulture);
                decimal tax = decimal.Parse(viewModel.TaxText, CultureInfo.InvariantCulture);
                decimal total = decimal.Parse(viewModel.TotalText, CultureInfo.InvariantCulture);

                // What the tax is charged on, plus the tax, is what the customer owes.
                taxable.ShouldBe(viewModel.Totals.Total.Amount - viewModel.Totals.Tax.Amount);
                (taxable + tax).ShouldBe(total);
            },
            product);
    }

    [Fact]
    public void The_change_is_worked_out_from_the_cash_the_cashier_typed()
    {
        PosProductFacts product = Product();

        RunTill(
            till =>
            {
                PosViewModel viewModel = till.ViewModel;

                viewModel.AddFromSearchCommand.Execute(product);
                viewModel.Lines[0].QuantityText = "2";

                viewModel.Totals.Total.Amount.ShouldBe(220m);

                viewModel.CashReceivedText = "300";
                viewModel.ChangeText.ShouldBe("80.00");

                // Cash that does not cover the sale is not change.
                viewModel.CashReceivedText = "100";
                viewModel.ChangeText.ShouldBe("0.00");

                viewModel.CashReceivedText = "220";
                viewModel.ChangeText.ShouldBe("0.00");
            },
            product);
    }

    [Fact]
    public void The_cash_the_cashier_typed_is_read_in_the_language_of_the_screen()
    {
        PosProductFacts product = Product();

        RunTill(
            till =>
            {
                PosViewModel viewModel = till.ViewModel;

                viewModel.AddFromSearchCommand.Execute(product);
                viewModel.Lines[0].QuantityText = "2";

                // The cashier typed the cash the way the screen writes it, so it is read the same
                // way. Read as a fixed number instead, it would be a hundred times too much.
                viewModel.CashReceivedText = "300,50";
                viewModel.ChangeText.ShouldBe("80.50");
            },
            product,
            culture: CommaCulture());
    }

    [Fact]
    public void A_discount_typed_as_a_percentage_and_one_typed_as_an_amount_are_read_differently()
    {
        DiscountSpec percentage = PosLineViewModel.ParseDiscount("10%");

        percentage.Kind.ShouldBe(DiscountKind.Percent);
        percentage.Value.ShouldBe(10m);

        DiscountSpec amount = PosLineViewModel.ParseDiscount("10");

        amount.Kind.ShouldBe(DiscountKind.Fixed);
        amount.Value.ShouldBe(10m);

        // A percentage is capped, so a cashier cannot give away more than the whole sale.
        PosLineViewModel.ParseDiscount("150%").Value.ShouldBe(100m);
    }

    [Fact]
    public void Rubbish_in_the_discount_field_is_no_discount_at_all()
    {
        PosLineViewModel.ParseDiscount(string.Empty).ShouldBe(DiscountSpec.None);
        PosLineViewModel.ParseDiscount(null).ShouldBe(DiscountSpec.None);
        PosLineViewModel.ParseDiscount("   ").ShouldBe(DiscountSpec.None);
        PosLineViewModel.ParseDiscount("ten percent").ShouldBe(DiscountSpec.None);
        PosLineViewModel.ParseDiscount("0").ShouldBe(DiscountSpec.None);
        PosLineViewModel.ParseDiscount("-5").ShouldBe(DiscountSpec.None);
    }

    [Fact]
    public void A_quantity_the_unit_cannot_express_is_refused()
    {
        // The product is sold by the piece, so two and a half pieces is not a quantity of anything.
        PosProductFacts byPiece = Product(unitScale: 0);

        PosRules.FitsScale(2.6m, 0).ShouldBeFalse();

        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(byPiece.Id, 2.6m, new Money(100m), DiscountSpec.None)],
            Catalogue(byPiece),
            DiscountSpec.None);

        cart.IsValid.ShouldBeFalse();
        cart.Issues.ShouldContain(issue => issue.Code == PosErrorCodes.InvalidQuantity);
        cart.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void A_quantity_the_unit_can_express_is_accepted()
    {
        // The same product, sold by the kilogram, takes the quantity exactly.
        PosProductFacts byKilo = Product(unitScale: 2);

        PosRules.FitsScale(2.6m, 2).ShouldBeTrue();

        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(byKilo.Id, 2.6m, new Money(100m), DiscountSpec.None)],
            Catalogue(byKilo),
            DiscountSpec.None);

        cart.IsValid.ShouldBeTrue();
        cart.Lines.ShouldHaveSingleItem();
        cart.Lines[0].Quantity.ShouldBe(2.6m);
        cart.Lines[0].QuantityScaled.ShouldBe(260L);
    }

    [Fact]
    public void Every_error_code_the_till_shows_has_a_message_of_its_own()
    {
        HashSet<string> resourceKeys = ReadResourceKeys();
        IStrings strings = CreateStrings();

        FieldInfo[] codes = typeof(PosErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static);

        codes.ShouldNotBeEmpty();

        foreach (FieldInfo code in codes)
        {
            string value = (string)code.GetValue(null)!;

            string key = PosErrorMessages.KeyFor(value);

            // A code with no case of its own falls through to the generic message, which tells the
            // cashier nothing about what actually went wrong.
            key.ShouldNotBe(StringKeys.ErrorUnexpected);
            resourceKeys.ShouldContain(key);

            PosErrorMessages.Resolve(strings, value).ShouldNotBe(strings[StringKeys.ErrorUnexpected]);
        }
    }

    [Fact]
    public void The_receipt_carries_the_money_of_the_sale()
    {
        SaleReceipt receipt = PostedSale(paid: new Money(200m), change: new Money(2m));

        ReceiptBuilder builder = new(CreateStrings(), new TestThemeService());

        PrintDocumentData document = builder.Build(receipt, "Cashier", wide: false, isCopy: false);

        document.Lines.Count.ShouldBe(8);

        document.Lines[0].Text.ShouldBe("Subtotal: 200.00");
        document.Lines[1].Text.ShouldBe("Discount: 20.00");
        document.Lines[2].Text.ShouldBe("Taxable amount: 180.00");
        document.Lines[3].Text.ShouldBe("Tax: 18.00");
        document.Lines[4].Text.ShouldBe("Total: 198.00");
        document.Lines[5].Text.ShouldBe("Paid: 198.00");
        document.Lines[6].Text.ShouldBe("Cash received: 200.00");
        document.Lines[7].Text.ShouldBe("Change: 2.00");

        // The arithmetic of the printed receipt reconciles: what is taxed plus the tax is what is
        // owed.
        decimal printedTaxable = AmountOn(document.Lines[2]);
        decimal printedTax = AmountOn(document.Lines[3]);
        decimal printedTotal = AmountOn(document.Lines[4]);

        (printedTaxable + printedTax).ShouldBe(printedTotal);
    }

    [Fact]
    public void A_reprint_is_marked_as_a_copy_and_the_first_print_is_not()
    {
        SaleReceipt receipt = PostedSale();

        ReceiptBuilder builder = new(CreateStrings(), new TestThemeService());

        PrintDocumentData first = builder.Build(receipt, "Cashier", wide: false, isCopy: false);
        PrintDocumentData reprint = builder.Build(receipt, "Cashier", wide: false, isCopy: true);

        first.IsCopy.ShouldBeFalse();
        first.HeaderLines.ShouldNotContain(line => line.Text == "COPY");

        reprint.IsCopy.ShouldBeTrue();
        reprint.HeaderLines.ShouldContain(line => line.Text == "COPY");
    }

    [Fact]
    public void The_receipt_and_the_invoice_are_different_papers_with_the_same_money()
    {
        SaleReceipt receipt = PostedSale();

        ReceiptBuilder builder = new(CreateStrings(), new TestThemeService());

        PrintDocumentData thermal = builder.Build(receipt, "Cashier", wide: false, isCopy: false);
        PrintDocumentData a4 = builder.Build(receipt, "Cashier", wide: true, isCopy: false);

        ReceiptBuilder.ThermalContentWidth.ShouldNotBe(ReceiptBuilder.A4ContentWidth);

        PrintTable? thermalTable = thermal.Table;
        PrintTable? a4Table = a4.Table;

        thermalTable.ShouldNotBeNull();
        a4Table.ShouldNotBeNull();

        // The roll has one column for the item and one for its price; the sheet has one for each.
        thermalTable!.Columns.Count.ShouldBe(2);
        a4Table!.Columns.Count.ShouldBe(4);

        thermalTable.Rows.ShouldHaveSingleItem();
        a4Table.Rows.ShouldHaveSingleItem();

        // Both papers carry the same money, so the customer is never quoted two different totals.
        thermal.Lines.Select(line => line.Text)
            .ShouldBe(a4.Lines.Select(line => line.Text));

        // The window shows the paper the cashier chose.
        var viewModel = new ReceiptViewModel(
            receipt,
            isCopy: false,
            new FakePrintService(),
            builder,
            CreateStrings(),
            new FakeUserSession());

        viewModel.CurrentContentWidth.ShouldBe(ReceiptBuilder.ThermalContentWidth);

        viewModel.Paper = ReceiptPaper.A4;

        viewModel.CurrentContentWidth.ShouldBe(ReceiptBuilder.A4ContentWidth);
    }

    [Fact]
    public void The_till_prints_the_receipt_again_as_a_copy()
    {
        PosProductFacts product = Product();
        SaleReceipt receipt = PostedSale(quantity: 1m, invoiceDiscount: 0m);

        RunTill(
            till =>
            {
                PosViewModel viewModel = till.ViewModel;

                viewModel.AddFromSearchCommand.Execute(product);

                viewModel.ConfirmSaleCommand.ExecuteAsync(null).GetAwaiter().GetResult();

                // The sale that was posted is the sale the till priced.
                till.PostSale.Command.ShouldNotBeNull();
                till.PostSale.Command!.Lines.ShouldHaveSingleItem();
                till.PostSale.Command.Lines[0].Quantity.ShouldBe(1m);

                till.Dialogs.Shown.Count.ShouldBe(1);
                till.Dialogs.Shown[0].IsCopy.ShouldBeFalse();

                viewModel.PrintLastReceiptCommand.ExecuteAsync(null).GetAwaiter().GetResult();

                till.Dialogs.Shown.Count.ShouldBe(2);
                till.Dialogs.Shown[1].IsCopy.ShouldBeTrue();
                till.Dialogs.Shown[1].Receipt.Number.ShouldBe(receipt.Number);
            },
            product,
            receipt);
    }

    /// <summary>
    /// Runs a scenario on a till, on a thread WPF is happy with and in the language of the screen.
    /// </summary>
    /// <param name="scenario">What the cashier does at the till.</param>
    /// <param name="product">The product the catalogue knows, when the scenario needs one.</param>
    /// <param name="postedSale">The sale the till posts, when the scenario confirms one.</param>
    /// <param name="culture">The culture of the screen, when it is not the usual one.</param>
    private static void RunTill(
        Action<TestTill> scenario,
        PosProductFacts? product = null,
        SaleReceipt? postedSale = null,
        CultureInfo? culture = null)
    {
        TestHost.RunOnStaThread(() =>
        {
            var till = new TestTill(product, postedSale);

            // The application runs in Arabic, whose decimal separator is not a dot, so the test
            // pins the language of the screen it means to be in.
            CultureInfo.CurrentCulture = culture ?? _english;
            CultureInfo.CurrentUICulture = culture ?? _english;

            scenario(till);
        });
    }

    /// <summary>
    /// A culture that writes its decimals with a comma, so a number typed the way the screen
    /// writes it can only be read with the culture of the screen.
    /// </summary>
    /// <returns>The culture.</returns>
    private static CultureInfo CommaCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = "\u00a0";

        return culture;
    }

    /// <summary>The strings of the application, in the language of the screen.</summary>
    /// <returns>The strings.</returns>
    private static IStrings CreateStrings()
    {
        var language = new LanguageService();

        language.SetLanguage("en");

        return new Strings(language);
    }

    /// <summary>A product the shop sells.</summary>
    /// <param name="id">The product id.</param>
    /// <param name="salePrice">The catalogue price of one unit.</param>
    /// <param name="taxRateBasisPoints">The tax rate in basis points.</param>
    /// <param name="unitScale">The precision of the selling unit.</param>
    /// <returns>The product.</returns>
    private static PosProductFacts Product(
        int id = 1,
        decimal salePrice = 100m,
        int taxRateBasisPoints = 1000,
        int unitScale = 0) =>
        new(
            id,
            $"SKU-{id:D3}",
            $"Product {id}",
            $"Product {id} AR",
            null,
            "Piece",
            unitScale,
            new Money(salePrice),
            taxRateBasisPoints,
            false,
            true,
            1000L);

    /// <summary>The catalogue the till prices against.</summary>
    /// <param name="products">The products it knows.</param>
    /// <returns>The catalogue.</returns>
    private static IReadOnlyDictionary<int, PosProductFacts> Catalogue(params PosProductFacts[] products)
    {
        var catalogue = new Dictionary<int, PosProductFacts>();

        foreach (PosProductFacts product in products)
        {
            catalogue[product.Id] = product;
        }

        return catalogue;
    }

    /// <summary>
    /// A sale that has been posted, priced by the same cart builder the till uses, so the receipt
    /// carries the numbers of a real sale.
    /// </summary>
    /// <param name="quantity">How much of the product was sold.</param>
    /// <param name="invoiceDiscount">The discount granted on the whole sale.</param>
    /// <param name="paid">The cash the customer handed over.</param>
    /// <param name="change">The change that was given back.</param>
    /// <returns>The sale.</returns>
    private static SaleReceipt PostedSale(
        decimal quantity = 2m,
        decimal invoiceDiscount = 20m,
        Money? paid = null,
        Money? change = null)
    {
        PosProductFacts product = Product();

        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(product.Id, quantity, new Money(100m), DiscountSpec.None)],
            Catalogue(product),
            invoiceDiscount > 0m ? DiscountSpec.FixedAmount(invoiceDiscount) : DiscountSpec.None);

        PosTotals totals = cart.Totals with
        {
            Paid = cart.Totals.Total,
            Change = change ?? Money.Zero,
        };

        var payment = new PaymentSummary(
            PaymentMethod.Cash,
            cart.Totals.Total,
            paid ?? cart.Totals.Total,
            totals.Change,
            null);

        return new SaleReceipt(
            42,
            "INV-2026-0001",
            new DateOnly(2026, 3, 4),
            new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.Zero),
            totals,
            cart.Lines,
            [payment],
            WasReplay: false);
    }

    /// <summary>The amount printed on a line of a receipt.</summary>
    /// <param name="line">The printed line.</param>
    /// <returns>The amount.</returns>
    private static decimal AmountOn(PrintLine line)
    {
        line.Text.ShouldContain(":");

        string amount = line.Text[(line.Text.LastIndexOf(':') + 1)..].Trim();

        return decimal.Parse(amount, CultureInfo.InvariantCulture);
    }

    /// <summary>The keys the application's English resource set holds.</summary>
    /// <returns>The keys.</returns>
    private static HashSet<string> ReadResourceKeys()
    {
        string root = FindRepositoryRoot();

        string path = Path.Combine(root, "src", "StoreFlow.Desktop", "Resources", "Strings.resx");

        File.Exists(path).ShouldBeTrue($"the resource file of the application must be readable: {path}");

        XDocument document = XDocument.Load(path);

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (XElement data in document.Root!.Elements("data"))
        {
            string? name = data.Attribute("name")?.Value;

            if (name is not null)
            {
                keys.Add(name);
            }
        }

        keys.ShouldNotBeEmpty();

        return keys;
    }

    /// <summary>Finds the folder that holds the solution, walking up from the test.</summary>
    /// <returns>The folder, or an empty string when the test is not run from the repository.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreFlow.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }

    /// <summary>A till, with the fakes behind it.</summary>
    private sealed class TestTill
    {
        public TestTill(PosProductFacts? product, SaleReceipt? postedSale)
        {
            Language = new LanguageService();
            Language.SetLanguage("en");

            Dialogs = new RecordingDialogs();
            PostSale = new RecordingPostSale(postedSale);

            IReadOnlyList<PosProductFacts> products = product is null
                ? Array.Empty<PosProductFacts>()
                : new[] { product };

            ViewModel = new PosViewModel(
                new FakeLookupHandler(product),
                new FakeSearchHandler(products),
                new FakeHeldCartsHandler(),
                PostSale,
                new FakeApprovalHandler(),
                new FakeHoldCartHandler(),
                new FakeResumeCartHandler(),
                new FakeClearCartHandler(),
                Dialogs,
                new Strings(Language),
                new TestThemeService(),
                Language);
        }

        public PosViewModel ViewModel { get; }

        public RecordingDialogs Dialogs { get; }

        public RecordingPostSale PostSale { get; }

        public LanguageService Language { get; }
    }

    /// <summary>The product a scanned barcode belongs to.</summary>
    private sealed class FakeLookupHandler(PosProductFacts? product)
        : IQueryHandler<LookupProductByBarcodeQuery, Result<PosProductFacts>>
    {
        public Task<Result<PosProductFacts>> HandleAsync(
            LookupProductByBarcodeQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(product is null
                ? Result<PosProductFacts>.Failure(
                    PosErrorCodes.ProductNotFound,
                    "That product is not in the catalogue.")
                : Result<PosProductFacts>.Success(product));
    }

    /// <summary>The products a search finds.</summary>
    private sealed class FakeSearchHandler(IReadOnlyList<PosProductFacts> products)
        : IQueryHandler<SearchSellableProductsQuery, Result<IReadOnlyList<PosProductFacts>>>
    {
        public Task<Result<IReadOnlyList<PosProductFacts>>> HandleAsync(
            SearchSellableProductsQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<PosProductFacts>>.Success(products));
    }

    /// <summary>The carts waiting to be picked up.</summary>
    private sealed class FakeHeldCartsHandler
        : IQueryHandler<ListHeldCartsQuery, Result<IReadOnlyList<HeldCartSummary>>>
    {
        public Task<Result<IReadOnlyList<HeldCartSummary>>> HandleAsync(
            ListHeldCartsQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<HeldCartSummary>>.Success(
                Array.Empty<HeldCartSummary>()));
    }

    /// <summary>The sale the till posts.</summary>
    private sealed class RecordingPostSale(SaleReceipt? receipt)
        : ICommandHandler<PostSaleCommand, Result<SaleReceipt>>
    {
        public PostSaleCommand? Command { get; private set; }

        public Task<Result<SaleReceipt>> HandleAsync(
            PostSaleCommand command,
            CancellationToken cancellationToken)
        {
            Command = command;

            return Task.FromResult(receipt is null
                ? Result<SaleReceipt>.Failure(PosErrorCodes.EmptyCart, "There is nothing to sell.")
                : Result<SaleReceipt>.Success(receipt));
        }
    }

    /// <summary>The approval a held discount waits for.</summary>
    private sealed class FakeApprovalHandler
        : ICommandHandler<RequestDiscountApprovalCommand, Result<ApprovalRequestSummary>>
    {
        public Task<Result<ApprovalRequestSummary>> HandleAsync(
            RequestDiscountApprovalCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<ApprovalRequestSummary>.Success(
                new ApprovalRequestSummary(
                    1,
                    ApprovalKind.DiscountOverLimit,
                    ApprovalStatus.Pending,
                    1,
                    "Cashier",
                    null,
                    null,
                    DateTimeOffset.UnixEpoch,
                    null,
                    null,
                    new Money(50m),
                    25m,
                    new Money(200m))));
    }

    /// <summary>The cart that is put aside.</summary>
    private sealed class FakeHoldCartHandler : ICommandHandler<HoldCartCommand, Result<HeldCartSummary>>    {
        public Task<Result<HeldCartSummary>> HandleAsync(
            HoldCartCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<HeldCartSummary>.Success(
                new HeldCartSummary(1, "Held", 1, 1m, new Money(100m), DateTimeOffset.UnixEpoch)));
    }

    /// <summary>The cart that is taken back.</summary>
    private sealed class FakeResumeCartHandler
        : ICommandHandler<ResumeHeldCartCommand, Result<HeldCartDetails>>
    {
        public Task<Result<HeldCartDetails>> HandleAsync(
            ResumeHeldCartCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<HeldCartDetails>.Success(
                new HeldCartDetails(
                    new HeldCartSummary(1, "Held", 0, 0m, Money.Zero, DateTimeOffset.UnixEpoch),
                    Array.Empty<SaleLineInput>(),
                    DiscountSpec.None)));
    }

    /// <summary>The cart that is thrown away.</summary>
    private sealed class FakeClearCartHandler : ICommandHandler<ClearHeldCartCommand, Result>
    {
        public Task<Result> HandleAsync(ClearHeldCartCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>The dialogs the till opens, remembered.</summary>
    private sealed class RecordingDialogs : IPosDialogs
    {
        public List<(SaleReceipt Receipt, bool IsCopy)> Shown { get; } = [];

        public Task ShowReceiptAsync(
            SaleReceipt receipt,
            bool isCopy,
            CancellationToken cancellationToken = default)
        {
            Shown.Add((receipt, isCopy));

            return Task.CompletedTask;
        }

        public Task<string?> AskForLabelAsync(
            string title,
            string label,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    /// <summary>The printers of the machine.</summary>
    private sealed class FakePrintService : IPrintService
    {
        public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrinterInfo>>(Array.Empty<PrinterInfo>());

        public Task<Result> PrintAsync(
            PrinterInfo printer,
            PrintDocumentKind kind,
            PrintDocumentData data,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> TestPrintAsync(PrinterInfo printer, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>The cashier at the till.</summary>
    private sealed class FakeUserSession : IUserSession
    {
        public UserSession? Current { get; private set; } = new(
            1,
            "cashier",
            "Cashier",
            new HashSet<Permission>(),
            false);

        public bool IsAuthenticated => Current is not null;

        public bool IsLocked => false;

        public void SignIn(UserSession session) => Current = session;

        public void SignOut() => Current = null;

        public void Lock()
        {
        }

        public void Unlock()
        {
        }
    }

    /// <summary>The theme, and the money it writes in.</summary>
    private sealed class TestThemeService : IThemeService
    {
        public ThemeDefinition Current { get; private set; } = new();

        public void Apply(ThemeDefinition theme) => Current = theme;

        public string FormatMoney(Money amount) =>
            amount.Amount.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
