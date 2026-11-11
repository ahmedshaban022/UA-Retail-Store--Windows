using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Composition;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Watches the scheduler while the shop is closing around it.
/// </summary>
/// <remarks>
/// The scheduler runs on a timer and nobody watches the task it starts, so an exception that
/// escapes it is never seen: the shop simply stops taking its backups and nothing says why. The
/// scheduler therefore has to finish quietly however the shop closes around it.
/// </remarks>
public sealed class BackupSchedulerTests
{
    [Fact]
    public async Task A_backup_that_is_running_when_the_shop_closes_ends_quietly()
    {
        var gate = new TaskCompletionSource();

        var services = new ServiceCollection();

        services.AddSingleton<ICommandHandler<RunScheduledBackupsCommand, Result<int>>>(
            new BlockingHandler(gate.Task));

        using ServiceProvider provider = services.BuildServiceProvider();

        var scheduler = new BackupScheduler(provider);

        Task<int> run = scheduler.RunAsync();

        // The backup has started and is waiting. Now the shop closes.
        scheduler.Dispose();

        gate.SetResult();

        // The run must finish rather than throw. A throw here would land on a task nobody is
        // watching, and the shop would quietly stop taking its backups.
        int taken = await run;

        taken.ShouldBe(0);
    }

    [Fact]
    public async Task A_scheduler_that_has_been_closed_does_not_run_again()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ICommandHandler<RunScheduledBackupsCommand, Result<int>>>(
            new BlockingHandler(Task.CompletedTask));

        using ServiceProvider provider = services.BuildServiceProvider();

        var scheduler = new BackupScheduler(provider);

        scheduler.Dispose();

        int taken = await scheduler.RunAsync();

        taken.ShouldBe(0);
    }

    /// <summary>A handler that waits for the test to let it finish.</summary>
    private sealed class BlockingHandler(Task gate) : ICommandHandler<RunScheduledBackupsCommand, Result<int>>
    {
        /// <inheritdoc />
        public async Task<Result<int>> HandleAsync(
            RunScheduledBackupsCommand command,
            CancellationToken cancellationToken)
        {
            await gate;

            return Result<int>.Success(0);
        }
    }
}
