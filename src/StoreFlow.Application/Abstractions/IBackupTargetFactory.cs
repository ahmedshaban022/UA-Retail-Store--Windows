namespace StoreFlow.Application.Abstractions;

/// <summary>
/// Builds the backup target a piece of work names. The engine works against the port, so a new
/// destination is added by writing one implementation and registering it here, without the backup
/// engine changing at all.
/// </summary>
public interface IBackupTargetFactory
{
    /// <summary>
    /// The kinds of target this build can write to.
    /// </summary>
    /// <returns>The target kinds, in the order they should be offered.</returns>
    IReadOnlyList<string> GetKnownKinds();

    /// <summary>
    /// Builds the target with the given kind.
    /// </summary>
    /// <param name="kind">The target kind, for example <c>LocalFolder</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The target.</returns>
    Task<IBackupTarget> ResolveAsync(string kind, CancellationToken cancellationToken = default);
}
