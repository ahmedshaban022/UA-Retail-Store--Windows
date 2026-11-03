using StoreFlow.Application.Abstractions;

namespace StoreFlow.Infrastructure.Persistence;

/// <summary>
/// The production write-fault injector: it does nothing, so writes behave exactly as they always
/// have. The seam only comes alive when a test asks for it.
/// </summary>
public sealed class NullWriteFaultInjector : IWriteFaultInjector
{
    /// <inheritdoc />
    public Task BeforeWriteAsync(WritePoint point, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task BeforeCommitAsync(WritePoint point, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
