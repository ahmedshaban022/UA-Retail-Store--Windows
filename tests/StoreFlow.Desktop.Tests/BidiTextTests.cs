using Shouldly;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Pins the way an identifier is kept inside a sentence that reads in the other direction.
/// </summary>
public sealed class BidiTextTests
{
    [Fact]
    public void An_identifier_is_wrapped_so_that_it_keeps_its_own_direction()
    {
        BidiText.Isolate("RICE-001").ShouldBe("\u2066RICE-001\u2069");
    }

    [Fact]
    public void A_missing_identifier_gives_nothing()
    {
        BidiText.Isolate(null).ShouldBe(string.Empty);
        BidiText.Isolate(string.Empty).ShouldBe(string.Empty);
    }

    [Fact]
    public void The_identifier_is_the_only_thing_the_isolate_carries()
    {
        // The isolate is invisible to the reader: it opens the identifier and closes it again.
        string isolated = BidiText.Isolate("6221000000017");

        isolated.ShouldStartWith("\u2066");
        isolated.ShouldEndWith("\u2069");
        isolated.Substring(1, isolated.Length - 2).ShouldBe("6221000000017");
    }
}
