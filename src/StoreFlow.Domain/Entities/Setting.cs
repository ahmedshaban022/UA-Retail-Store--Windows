namespace StoreFlow.Domain.Entities;

/// <summary>
/// A configuration entry. Values are stored as JSON so a setting can evolve without a schema
/// migration, and every write records when it changed.
/// </summary>
public sealed class Setting
{
    /// <summary>The setting key; the primary key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The JSON-encoded value.</summary>
    public string Value { get; set; } = "{}";

    public DateTimeOffset UpdatedUtc { get; set; }
}
