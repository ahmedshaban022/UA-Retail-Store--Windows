namespace StoreFlow.Application.Abstractions;

/// <summary>
/// The application's only source of time. Injecting it keeps posting timestamps deterministic in
/// tests and keeps every timestamp in UTC (PRD §6.3).
/// </summary>
public interface IClock
{
    /// <summary>The current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>The current instant in the machine's local time zone.</summary>
    DateTimeOffset LocalNow { get; }

    /// <summary>Today's date in the machine's local time zone.</summary>
    DateOnly LocalToday { get; }
}
