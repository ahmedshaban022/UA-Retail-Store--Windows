using Microsoft.EntityFrameworkCore;
using StoreFlow.Desktop.Localization;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// What the programme does when a screen throws. The shop's programme must not end because of a
/// fault in one of its screens: the fault is recorded, the owner is told, and the shop carries on
/// trading.
/// </summary>
public sealed class DispatcherFailureTests
{
    [Fact]
    public void A_fault_in_a_screen_does_not_end_the_programme()
    {
        FailureDecision decision = DispatcherFailure.Decide(
            new InvalidOperationException("A screen fell over."),
            new TestStrings());

        decision.KeepRunning.ShouldBeTrue();
        decision.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Stock_that_somebody_else_moved_is_told_in_its_own_words()
    {
        var strings = new TestStrings();

        FailureDecision decision = DispatcherFailure.Decide(
            new DbUpdateConcurrencyException("The balance was moved."),
            strings);

        decision.KeepRunning.ShouldBeTrue();
        decision.Message.ShouldBe(strings[StringKeys.ErrorStockChanged]);
    }

    [Fact]
    public void Any_other_fault_is_told_in_the_ordinary_words()
    {
        var strings = new TestStrings();

        FailureDecision decision = DispatcherFailure.Decide(
            new InvalidOperationException("A screen fell over."),
            strings);

        decision.Message.ShouldBe(strings[StringKeys.ErrorUnexpected]);
    }

    [Fact]
    public void A_fault_before_the_host_is_up_still_keeps_the_programme_running()
    {
        FailureDecision decision = DispatcherFailure.Decide(
            new InvalidOperationException("A screen fell over."),
            strings: null);

        decision.KeepRunning.ShouldBeTrue();
        decision.Message.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A stand-in for the application's strings, so the test does not need a resource manager.
    /// </summary>
    private sealed class TestStrings : IStrings
    {
        private static readonly Dictionary<string, string> Messages = new()
        {
            [StringKeys.ErrorUnexpected] = "Something went wrong. Close this message and try again.",
            [StringKeys.ErrorStockChanged] = "Somebody else changed this stock while you were working.",
        };

        public string this[string key] => Messages.TryGetValue(key, out string? message) ? message : key;

        public string Format(string key, params object?[] arguments) => string.Format(this[key], arguments);
    }
}
