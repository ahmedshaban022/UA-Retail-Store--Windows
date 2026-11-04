using System.Reflection;

namespace StoreFlow.Desktop;

/// <summary>
/// The version of the running build.
/// </summary>
/// <remarks>
/// The installer passes the version it is building into the assembly, so a released build says
/// what it is. A development build takes the version from the project file. Either way there is
/// one answer, and it is the answer the update check compares against the published release.
/// </remarks>
public static class ApplicationVersion
{
    /// <summary>The version of the running build, for example <c>1.0.0</c>.</summary>
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        Assembly? assembly = Assembly.GetEntryAssembly();

        string? informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // A build carries its commit after a '+'. That is not part of the version.
            int cut = informational.IndexOf('+', StringComparison.Ordinal);

            return cut > 0 ? informational[..cut] : informational;
        }

        Version? version = assembly?.GetName().Version;

        return version is null ? "0.0.0" : version.ToString(3);
    }
}
