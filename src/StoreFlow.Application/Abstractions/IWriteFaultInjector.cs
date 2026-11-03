namespace StoreFlow.Application.Abstractions;

/// <summary>
/// Lets a test fail one write on purpose, so that a command can be shown to have committed
/// everything or to have committed nothing (PRD NFR-05).
///
/// The production implementation does nothing. The seam is here so that a failure can be placed
/// exactly where a write happens, instead of being approximated by a stub that never touches the
/// database and therefore proves nothing about the database.
/// </summary>
public interface IWriteFaultInjector
{
    /// <summary>
    /// Called at the point where a command's side effects are written. Throwing here means the
    /// side effects never reach the database.
    /// </summary>
    /// <param name="point">Which write is being attempted.</param>
    /// <param name="cancellationToken">The token the write was asked to honour.</param>
    Task BeforeWriteAsync(WritePoint point, CancellationToken cancellationToken);

    /// <summary>
    /// Called at the point where the transaction holding those side effects is committed.
    /// Throwing here means the work was done and then thrown away.
    /// </summary>
    /// <param name="point">Which write is being committed.</param>
    /// <param name="cancellationToken">The token the commit was asked to honour.</param>
    Task BeforeCommitAsync(WritePoint point, CancellationToken cancellationToken);
}
