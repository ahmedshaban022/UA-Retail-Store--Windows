using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Services;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// The printer behind the till: the paper comes out on a thread of its own, so the window keeps
/// answering the cashier while it does.
/// </summary>
public sealed class PrintingTests
{
    [Fact]
    public async Task The_paper_comes_out_on_a_thread_of_its_own()
    {
        // The spooler takes as long as it takes. A till that waited for it on the thread that
        // draws the screen would be frozen while the paper came out, and the cashier would be
        // standing in front of a window that does not answer.
        int caller = Environment.CurrentManagedThreadId;

        int printer = -1;

        WindowsPrintService service = WindowsPrintService.SendingThrough(
            (_, _, _) => printer = Environment.CurrentManagedThreadId);

        Result result = await service.PrintAsync(
            new PrinterInfo("Test printer", true, null, true),
            PrintDocumentKind.Receipt80,
            Document(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        printer.ShouldNotBe(caller, "the print must not run on the thread that draws the screen");
    }

    [Fact]
    public async Task A_printer_that_is_switched_off_is_reported_rather_than_thrown()
    {
        // The till must stay open when the printer is off. The shop can still sell, and the owner
        // is told the paper did not come out.
        WindowsPrintService service = WindowsPrintService.SendingThrough(
            (_, _, _) => throw new InvalidOperationException("The printer is switched off."));

        Result result = await service.PrintAsync(
            new PrinterInfo("Test printer", true, null, true),
            PrintDocumentKind.Receipt80,
            Document(),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("switched off");
    }

    /// <summary>A document to print.</summary>
    /// <returns>The document.</returns>
    private static PrintDocumentData Document() =>
        new(
            "Receipt",
            [new PrintLine("StoreFlow")],
            [new PrintLine("Total: 10.00")]);
}
