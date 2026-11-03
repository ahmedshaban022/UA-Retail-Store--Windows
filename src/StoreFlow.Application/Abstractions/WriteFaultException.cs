namespace StoreFlow.Application.Abstractions;

/// <summary>
/// Thrown when a write is failed on purpose, so that a test can prove a command either committed
/// everything or committed nothing.
/// </summary>
public sealed class WriteFaultException : Exception
{
    /// <summary>Creates the exception for a chosen write point.</summary>
    /// <param name="point">The write that was failed.</param>
    public WriteFaultException(WritePoint point)
        : base($"The write '{point}' was failed on purpose.")
    {
        Point = point;
    }

    /// <summary>The write that was failed.</summary>
    public WritePoint Point { get; }
}
