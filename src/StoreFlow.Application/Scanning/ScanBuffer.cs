using System.Text;
using StoreFlow.Application.Abstractions;

namespace StoreFlow.Application.Scanning;

/// <summary>
/// The default scan buffer. It accumulates keyboard-wedge characters and emits a scan only when
/// the configured terminator arrives, so an unfinished scan can never add an item to the cart
/// (PRD §5.6 BAR-02).
/// </summary>
public sealed class ScanBuffer : IScanBuffer
{
    private readonly StringBuilder _buffer = new();
    private readonly Lock _gate = new();
    private DateTimeOffset _lastKeyAt;
    private bool _hasCharacters;

    /// <inheritdoc />
    public ScanBufferOptions Options { get; private set; } = new();

    /// <inheritdoc />
    public event EventHandler<ScanCompletedEventArgs>? ScanCompleted;

    /// <inheritdoc />
    public void Configure(ScanBufferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        lock (_gate)
        {
            Options = options;
            _buffer.Clear();
            _hasCharacters = false;
        }
    }

    /// <inheritdoc />
    public void Feed(char character, DateTimeOffset timestamp)
    {
        ScanCompletedEventArgs? completed = null;

        lock (_gate)
        {
            if (IsTerminator(character))
            {
                completed = Complete(timestamp, wasTerminated: true);
            }
            else
            {
                if (_hasCharacters && Options.AutoSubmitOnTimeout && HasTimedOut(timestamp))
                {
                    completed = Complete(timestamp, wasTerminated: false);
                }

                if (completed is null)
                {
                    if (_buffer.Length >= Options.MaximumLength)
                    {
                        // The buffer is full and still not terminated: drop the excess rather than
                        // growing without bound.
                        _buffer.Clear();
                    }

                    _buffer.Append(character);
                    _lastKeyAt = timestamp;
                    _hasCharacters = true;
                }
            }
        }

        if (completed is not null)
        {
            ScanCompleted?.Invoke(this, completed);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_gate)
        {
            _buffer.Clear();
            _hasCharacters = false;
        }
    }

    private bool IsTerminator(char character) => Options.Terminator switch
    {
        ScanTerminator.Enter => character is '\r' or '\n',
        ScanTerminator.Tab => character == '\t',
        _ => false,
    };

    private bool HasTimedOut(DateTimeOffset timestamp) =>
        (timestamp - _lastKeyAt).TotalMilliseconds > Options.MaxInterKeyDelayMs;

    private ScanCompletedEventArgs? Complete(DateTimeOffset timestamp, bool wasTerminated)
    {
        if (!_hasCharacters)
        {
            return null;
        }

        string code = _buffer.ToString();
        long elapsed = (long)(timestamp - _lastKeyAt).TotalMilliseconds;

        _buffer.Clear();
        _hasCharacters = false;

        if (code.Length < Options.MinimumLength || code.Length > Options.MaximumLength)
        {
            return null;
        }

        return new ScanCompletedEventArgs(code, elapsed, wasTerminated);
    }
}
