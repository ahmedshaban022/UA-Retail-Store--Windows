using System.Reflection;

namespace StoreFlow.Infrastructure.Backup;

/// <summary>
/// The version of the shop that wrote a backup. It is read from the shipped assemblies, so it
/// travels with the build and cannot drift from what is actually installed.
/// </summary>
public static class ApplicationVersion
{
    private static readonly Lazy<string> Version = new(Read, isThreadSafe: true);

    /// <summary>The version of the installed application.</summary>
    public static string Current => Version.Value;

    private static string Read()
    {
        Assembly assembly = typeof(ApplicationVersion).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The informational version carries the commit hash after a "+". The version alone is
            // what a person can read on a backup manifest.
            int plus = informational.IndexOf('+', StringComparison.Ordinal);

            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
