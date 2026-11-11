using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Application.Abstractions;
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
/// The words the owner reads on the till, in the language the shop is running in.
/// </summary>
/// <remarks>
/// The localisation of the programme is complete, and a handful of sentences had slipped past it:
/// the stamp on a reprint, the message about a missing printer, and the title of the file picker.
/// Each of them is a sentence the owner reads, so each of them belongs in the resources.
/// </remarks>
public sealed class OwnerTextTests
{
    [Fact]
    public void The_copy_stamp_on_a_reprint_is_written_in_the_language_of_the_shop()
    {
        // The stamp is printed on paper, in front of the customer. An English word on an Arabic
        // receipt is English the shop does not speak.
        IStrings arabic = ArabicStrings();

        ReceiptBuilder builder = new(arabic, new TestThemeService());

        PrintDocumentData copy = builder.Build(Receipt(), "Cashier", wide: false, isCopy: true);

        copy.HeaderLines.ShouldContain(line => line.Text == arabic[StringKeys.PosCopyStamp]);
        copy.HeaderLines.ShouldNotContain(line => line.Text == "COPY");
    }

    [Fact]
    public async Task The_receipt_screen_says_there_is_no_printer_in_the_language_of_the_shop()
    {
        IStrings arabic = ArabicStrings();

        var viewModel = new ReceiptViewModel(
            Receipt(),
            isCopy: false,
            new NoPrinters(),
            new ReceiptBuilder(arabic, new TestThemeService()),
            arabic,
            new SignedInCashier());

        // The message appears when the cashier asks for the paper.
        await viewModel.PrintThermalCommand.ExecuteAsync(null);

        viewModel.StatusMessage.ShouldBe(arabic[StringKeys.PosNoPrinter]);
        viewModel.StatusMessage.ShouldNotBe("There is no printer on this machine.");
    }

    [Fact]
    public void The_backup_file_picker_is_titled_in_the_language_of_the_shop()
    {
        IStrings arabic = ArabicStrings();

        var services = new ServiceCollection();

        services.AddSingleton(arabic);

        using ServiceProvider provider = services.BuildServiceProvider();

        var dialogs = new BackupDialogs(provider);

        Microsoft.Win32.OpenFileDialog picker = dialogs.CreateFilePicker();

        picker.Title.ShouldBe(arabic[StringKeys.BackupPickFile]);
        picker.Title.ShouldNotBe("Choose a StoreFlow backup");
    }

    /// <summary>The strings of the application, in Arabic.</summary>
    /// <returns>The strings.</returns>
    private static IStrings ArabicStrings()
    {
        var language = new LanguageService();

        language.SetLanguage("ar");

        return new Strings(language);
    }

    /// <summary>A sale to print.</summary>
    /// <returns>The receipt.</returns>
    private static SaleReceipt Receipt()
    {
        var product = new PosProductFacts(
            1,
            "SKU-001",
            "Rice",
            "أرز",
            null,
            "Piece",
            0,
            new Money(100m),
            1000,
            false,
            true,
            1000L);

        PosCart cart = PosCartBuilder.Build(
            [new SaleLineInput(product.Id, 1m, new Money(100m), DiscountSpec.None)],
            new Dictionary<int, PosProductFacts> { [product.Id] = product },
            DiscountSpec.None);

        PosTotals totals = cart.Totals with { Paid = cart.Totals.Total, Change = Money.Zero };

        var payment = new PaymentSummary(
            PaymentMethod.Cash,
            cart.Totals.Total,
            cart.Totals.Total,
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

    /// <summary>The theme, and the money it writes in.</summary>
    private sealed class TestThemeService : IThemeService
    {
        public ThemeDefinition Current { get; private set; } = new();

        public void Apply(ThemeDefinition theme) => Current = theme;

        public string FormatMoney(Money amount) => amount.Amount.ToString("0.00");
    }

    /// <summary>A machine with no printer on it.</summary>
    private sealed class NoPrinters : IPrintService
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
    private sealed class SignedInCashier : IUserSession
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
}
