using System.IO;
using System.Text;
using StoreFlow.Infrastructure;

namespace StoreFlow.Desktop.Composition;

/// <summary>
/// Writes start-up diagnostics to a plain text file next to the rolling application log.
/// </summary>
/// <remarks>
/// A shop PC has no console and no debugger. If the application fails before it can show a window,
/// the only trace the owner can send is a file. Every start-up failure is therefore recorded here,
/// including the full exception chain, before the process gives up.
/// </remarks>
public static class StartupDiagnostics
{
    private static readonly object Gate = new();
    private static string? _logPath;

    /// <summary>The file start-up diagnostics are appended to.</summary>
    public static string LogPath => _logPath ??= Path.Combine(
        new AppPaths().LogsDirectory,
        "startup.log");

    /// <summary>Appends a diagnostic entry.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="exception">The exception, when there is one.</param>
    public static void Write(string message, Exception? exception = null)
    {
        // A diagnostics writer that throws would turn a recoverable start-up problem into a crash
        // with no trace at all, so every failure here is deliberately swallowed.
#pragma warning disable CA1031 // Do not catch general Exception
        try
        {
            var paths = new AppPaths();
            Directory.CreateDirectory(paths.LogsDirectory);

            var entry = new StringBuilder()
                .AppendLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z] {message}");

            if (exception is not null)
            {
                entry.AppendLine(exception.ToString());
            }

            lock (Gate)
            {
                File.AppendAllText(LogPath, entry.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Diagnostics must never take the application down.
        }
#pragma warning restore CA1031
    }
}
