namespace StoreFlow.Application.Abstractions;

/// <summary>How a keyboard-wedge scanner signals the end of a scan.</summary>
public enum ScanTerminator
{
    /// <summary>The Enter key ends the scan. The default, and what most scanners are configured to.</summary>
    Enter = 0,

    /// <summary>The Tab key ends the scan.</summary>
    Tab = 1,
}

/// <summary>How the scan buffer decides that a burst of keystrokes is one barcode.</summary>
/// <param name="Terminator">The key that ends a scan.</param>
/// <param name="MaxInterKeyDelayMs">
/// The longest gap between two characters that still counts as one scan. A scanner types far faster
/// than a person, so a longer gap means the cashier is typing a code manually (PRD §5.6 BAR-02).
/// </param>
/// <param name="MinimumLength">The shortest code accepted; shorter input is ignored.</param>
/// <param name="MaximumLength">The longest code accepted.</param>
/// <param name="AutoSubmitOnTimeout">
/// When true, a burst that stops for longer than <paramref name="MaxInterKeyDelayMs"/> is submitted
/// even without the terminator. This is off by default, because a partial scan must never add an
/// item to the cart (PRD BAR-02); it exists only for scanners configured without a suffix.
/// </param>
public sealed record ScanBufferOptions(
    ScanTerminator Terminator = ScanTerminator.Enter,
    int MaxInterKeyDelayMs = 120,
    int MinimumLength = 3,
    int MaximumLength = 64,
    bool AutoSubmitOnTimeout = false)
{
    /// <summary>Validates the options and throws when they are inconsistent.</summary>
    public void Validate()
    {
        if (MaxInterKeyDelayMs is < 10 or > 2_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxInterKeyDelayMs),
                MaxInterKeyDelayMs,
                "The inter-key delay must be between 10 and 2000 milliseconds.");
        }

        if (MinimumLength < 1 || MaximumLength < MinimumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumLength),
                MinimumLength,
                "The minimum length must be at least 1 and not greater than the maximum length.");
        }
    }
}

/// <summary>Reports a completed scan.</summary>
/// <param name="Code">The scanned text.</param>
/// <param name="ElapsedMs">How long the burst of characters took.</param>
/// <param name="WasTerminated">True when the terminator key ended the burst.</param>
public sealed record ScanCompletedEventArgs(string Code, long ElapsedMs, bool WasTerminated);

/// <summary>
/// Buffers keyboard-wedge input into complete barcode scans. The logic lives in the application
/// layer so it can be tested without a window; the desktop layer only feeds characters into it.
/// </summary>
public interface IScanBuffer
{
    /// <summary>The current options.</summary>
    ScanBufferOptions Options { get; }

    /// <summary>Replaces the options.</summary>
    void Configure(ScanBufferOptions options);

    /// <summary>Raised when a complete scan has been recognised.</summary>
    event EventHandler<ScanCompletedEventArgs>? ScanCompleted;

    /// <summary>Feeds a single character.</summary>
    void Feed(char character, DateTimeOffset timestamp);

    /// <summary>Clears the buffer, for example when the cashier clears the field.</summary>
    void Reset();
}
