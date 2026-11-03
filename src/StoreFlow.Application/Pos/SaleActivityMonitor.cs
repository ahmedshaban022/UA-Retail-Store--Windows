namespace StoreFlow.Application.Pos;

/// <summary>
/// Says whether a sale is being posted right now. The restore refuses to replace the shop's data
/// while a sale is in flight, because a sale that is half written and a restore that lands at the
/// same moment would lose money (PRD §8.5).
/// </summary>
public interface ISaleActivityMonitor
{
    /// <summary>True while a sale is being posted.</summary>
    bool IsSaleInProgress { get; }

    /// <summary>
    /// Marks the start of a sale. The returned scope marks the end of it when it is disposed.
    /// </summary>
    /// <returns>The scope that ends the sale.</returns>
    IDisposable Begin();
}

/// <summary>
/// The default monitor. It counts the sales that are in flight, so two sales being posted at once
/// are both seen, and the count returns to zero when the last of them finishes.
/// </summary>
public sealed class SaleActivityMonitor : ISaleActivityMonitor
{
    private int _inFlight;

    /// <inheritdoc />
    public bool IsSaleInProgress => Volatile.Read(ref _inFlight) > 0;

    /// <summary>
    /// Marks the start of a sale. The returned scope marks the end of it when it is disposed.
    /// </summary>
    /// <returns>The scope that ends the sale.</returns>
    public IDisposable Begin()
    {
        Interlocked.Increment(ref _inFlight);

        return new SaleScope(this);
    }

    private void End() => Interlocked.Decrement(ref _inFlight);

    private sealed class SaleScope(SaleActivityMonitor owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.End();
        }
    }
}
