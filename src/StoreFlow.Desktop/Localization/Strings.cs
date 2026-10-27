namespace StoreFlow.Desktop.Localization;

/// <summary>Reads user-facing text in the language the application is currently running in.</summary>
public interface IStrings
{
    /// <summary>The text for a key, or the key itself when the resource is missing.</summary>
    string this[string key] { get; }

    /// <summary>The text for a key, with the arguments substituted into its placeholders.</summary>
    string Format(string key, params object?[] arguments);
}
