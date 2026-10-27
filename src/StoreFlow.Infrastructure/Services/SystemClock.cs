using StoreFlow.Application.Abstractions;

namespace StoreFlow.Infrastructure.Services;

/// <summary>The system clock. Injected everywhere so timestamps stay testable.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateTimeOffset LocalNow => DateTimeOffset.Now;

    /// <inheritdoc />
    public DateOnly LocalToday => DateOnly.FromDateTime(DateTime.Now);
}
