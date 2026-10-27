using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Scanning;
using Shouldly;

namespace StoreFlow.Application.Tests;

public sealed class ScanBufferTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_scan_terminated_by_enter_is_reported()
    {
        var buffer = new ScanBuffer();
        var completed = new List<ScanCompletedEventArgs>();
        buffer.ScanCompleted += (_, args) => completed.Add(args);

        Feed(buffer, "6221052775557", '\r');

        completed.Count.ShouldBe(1);
        completed[0].Code.ShouldBe("6221052775557");
        completed[0].WasTerminated.ShouldBeTrue();
    }

    [Fact]
    public void An_unterminated_burst_never_adds_an_item()
    {
        var buffer = new ScanBuffer();
        var completed = new List<ScanCompletedEventArgs>();
        buffer.ScanCompleted += (_, args) => completed.Add(args);

        Feed(buffer, "6221052775557", '\0');

        completed.ShouldBeEmpty();
    }

    [Fact]
    public void A_code_shorter_than_the_minimum_is_ignored()
    {
        var buffer = new ScanBuffer();
        var completed = new List<ScanCompletedEventArgs>();
        buffer.ScanCompleted += (_, args) => completed.Add(args);

        Feed(buffer, "12", '\r');

        completed.ShouldBeEmpty();
    }

    [Fact]
    public void A_tab_can_be_the_terminator()
    {
        var buffer = new ScanBuffer();
        buffer.Configure(new ScanBufferOptions(Terminator: ScanTerminator.Tab));

        var completed = new List<ScanCompletedEventArgs>();
        buffer.ScanCompleted += (_, args) => completed.Add(args);

        Feed(buffer, "12345", '\t');

        completed.Count.ShouldBe(1);
        completed[0].WasTerminated.ShouldBeTrue();
    }

    [Fact]
    public void A_slow_typist_can_opt_into_a_timeout_submission()
    {
        var buffer = new ScanBuffer();
        buffer.Configure(new ScanBufferOptions(MaxInterKeyDelayMs: 50, AutoSubmitOnTimeout: true));

        var completed = new List<ScanCompletedEventArgs>();
        buffer.ScanCompleted += (_, args) => completed.Add(args);

        // Three fast characters, then a pause longer than the inter-key delay.
        buffer.Feed('1', Start);
        buffer.Feed('2', Start.AddMilliseconds(10));
        buffer.Feed('3', Start.AddMilliseconds(20));
        buffer.Feed('4', Start.AddMilliseconds(300));

        completed.Count.ShouldBe(1);
        completed[0].Code.ShouldBe("123");
        completed[0].WasTerminated.ShouldBeFalse();
    }

    [Fact]
    public void The_buffer_can_be_cleared()
    {
        var buffer = new ScanBuffer();
        var completed = new List<ScanCompletedEventArgs>();
        buffer.ScanCompleted += (_, args) => completed.Add(args);

        Feed(buffer, "12345", '\0');
        buffer.Reset();
        buffer.Feed('\r', Start.AddMilliseconds(20));

        completed.ShouldBeEmpty();
    }

    private static void Feed(ScanBuffer buffer, string code, char terminator)
    {
        DateTimeOffset timestamp = Start;

        foreach (char character in code)
        {
            buffer.Feed(character, timestamp);
            timestamp = timestamp.AddMilliseconds(10);
        }

        if (terminator != '\0')
        {
            buffer.Feed(terminator, timestamp);
        }
    }
}
