using StoreFlow.Application.Abstractions;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The fault injector the tests use: it fails the one write the test asks for and lets every other
/// write through.
/// </summary>
public sealed class ControllableWriteFaultInjector : IWriteFaultInjector
{
    /// <summary>The write to fail, or null to let every write through.</summary>
    public WritePoint? FailAt { get; set; }

    /// <summary>The write points this injector was asked to fail, in order.</summary>
    public List<WritePoint> Failed { get; } = [];

    /// <inheritdoc />
    public Task BeforeWriteAsync(WritePoint point, CancellationToken cancellationToken) => Fail(point);

    /// <inheritdoc />
    public Task BeforeCommitAsync(WritePoint point, CancellationToken cancellationToken) => Fail(point);

    private Task Fail(WritePoint point)
    {
        if (FailAt != point)
        {
            return Task.CompletedTask;
        }

        Failed.Add(point);

        throw new WriteFaultException(point);
    }
}
