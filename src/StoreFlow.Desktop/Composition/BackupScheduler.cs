using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;

namespace StoreFlow.Desktop.Composition;

/// <summary>
/// Runs the scheduled backups while the shop is open.
/// </summary>
/// <remarks>
/// <para>The scheduler is a timer inside the application rather than a Windows scheduled task. A
/// scheduled task would launch the whole application in a background mode, which for a till that
/// holds its data in one local file would mean two copies of the shop's database open at once.
/// A timer inside the application cannot do that.</para>
/// <para>When the shop was closed on a day a backup was due, one backup is taken when the shop
/// opens again, not one for every missed day. That rule lives with the schedule, in the
/// application layer, and is tested there.</para>
/// </remarks>
public sealed class BackupScheduler(IServiceProvider services) : IDisposable
{
    /// <summary>How often the scheduler looks to see whether a backup is due.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _timer;
    private bool _disposed;

    /// <summary>
    /// Starts the scheduler. The first look happens at once, so that a shop which was closed when a
    /// backup was due gets its copy without waiting.
    /// </summary>
    public void Start()
    {
        ArgumentNullException.ThrowIfNull(services);

        _timer = new Timer(OnTick, null, TimeSpan.FromSeconds(20), Interval);

        StartupDiagnostics.Write("The backup scheduler is running.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _timer?.Dispose();
        _gate.Dispose();
    }

    private void OnTick(object? state)
    {
        _ = RunAsync();
    }

    /// <summary>
    /// Looks to see whether a backup is due, and takes it if it is.
    /// </summary>
    /// <returns>The number of backups that were taken.</returns>
    public async Task<int> RunAsync()
    {
        if (_disposed)
        {
            return 0;
        }

        await _gate.WaitAsync();

        try
        {
            var handler = services.GetService(typeof(ICommandHandler<RunScheduledBackupsCommand, Result<int>>))
                as ICommandHandler<RunScheduledBackupsCommand, Result<int>>;

            if (handler is null)
            {
                return 0;
            }

            Result<int> result = await handler.HandleAsync(new RunScheduledBackupsCommand(), CancellationToken.None);

            if (result.IsFailure)
            {
                StartupDiagnostics.Write("The scheduled backup failed: " + result.Error.Message);

                return 0;
            }

            if (result.Value > 0)
            {
                StartupDiagnostics.Write($"The scheduler took {result.Value} backup(s).");
            }

            return result.Value;
        }
#pragma warning disable CA1031 // A background timer must never take the shop down.
        catch (Exception ex)
        {
            StartupDiagnostics.Write("The scheduled backup failed.", ex);

            return 0;
        }
#pragma warning restore CA1031
        finally
        {
            _gate.Release();
        }
    }
}
