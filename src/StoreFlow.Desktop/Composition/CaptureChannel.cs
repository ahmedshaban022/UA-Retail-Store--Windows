using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace StoreFlow.Desktop.Composition;

/// <summary>
/// Lets a harness ask the running application to photograph one of its own windows.
/// </summary>
/// <remarks>
/// <para>The application takes its own pictures. A window that is asked to draw itself is drawn
/// faithfully, right-to-left included, whereas a picture taken from outside the process comes back
/// mirrored and shows a frame that is not the one the owner sees. For that reason nothing in this
/// project photographs a window with a window message; the application draws itself instead.</para>
/// <para>The channel is a named pipe that is only opened when the application is started with
/// <c>--capture-pipe &lt;name&gt;</c>, so a shop installation never has one. The harness sends one
/// line and reads one line back:</para>
/// <list type="bullet">
/// <item><description><c>PING</c> answers <c>PONG</c>, and says the application is listening.</description></item>
/// <item><description><c>CAPTURE &lt;window id&gt; &lt;path&gt;</c> draws that window and writes the
/// picture to the path, and answers <c>OK</c> or <c>ERR &lt;what went wrong&gt;</c>.</description></item>
/// <item><description><c>QUIT</c> closes the channel.</description></item>
/// </list>
/// <para>The window id is the automation identifier of the window, so a dialog is named the same way
/// the harness names it when it looks for the dialog. The shell is called <c>MainWindow</c>.</para>
/// </remarks>
public sealed class CaptureChannel : IDisposable
{
    /// <summary>The command-line switch that opens the channel.</summary>
    public const string Switch = "--capture-pipe";

    private readonly string _pipeName;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _stopping = new();

    private Task? _listener;
    private bool _disposed;

    /// <summary>Creates the channel.</summary>
    /// <param name="pipeName">The name of the pipe to listen on.</param>
    /// <param name="dispatcher">The dispatcher of the interface thread.</param>
    public CaptureChannel(string pipeName, Dispatcher dispatcher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _pipeName = pipeName;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Reads the name of the pipe from the command line, when the application was asked to open one.
    /// </summary>
    /// <param name="args">The command line of the application.</param>
    /// <returns>The name of the pipe, or <c>null</c> when the channel was not asked for.</returns>
    public static string? ReadPipeName(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        int index = Array.IndexOf(args, Switch);

        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        string name = args[index + 1];

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Starts listening for commands.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _listener = Task.Run(ListenAsync);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the channel.</summary>
    /// <param name="disposing">Whether the channel was disposed rather than finalized.</param>
    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _stopping.Cancel();
            _stopping.Dispose();
        }

        _disposed = true;
    }

    /// <summary>Waits for a harness, and serves it one command at a time.</summary>
    private async Task ListenAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);

                string? line;
                while (!string.IsNullOrWhiteSpace(
                    line = await reader.ReadLineAsync(_stopping.Token).ConfigureAwait(false)))
                {
                    string reply = await ExecuteAsync(line).ConfigureAwait(false);

                    byte[] bytes = Encoding.UTF8.GetBytes(reply + "\n");
                    await server.WriteAsync(bytes, _stopping.Token).ConfigureAwait(false);
                    await server.FlushAsync(_stopping.Token).ConfigureAwait(false);

                    if (string.Equals(line.Trim(), "QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // The harness hung up or went away. The channel waits for the next one.
                StartupDiagnostics.Write("The capture channel lost its connection.");
            }
            catch (ObjectDisposedException)
            {
                return;
            }
#pragma warning disable CA1031 // One bad command must not silence the channel for good.
            catch (Exception ex)
            {
                StartupDiagnostics.Write("The capture channel hit an unexpected fault.", ex);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>Carries out one command and returns the answer.</summary>
    private async Task<string> ExecuteAsync(string line)
    {
        string[] parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "ERR an empty command";
        }

        switch (parts[0].ToUpperInvariant())
        {
            case "PING":
                return "PONG";

            case "CAPTURE":
                return Capture(parts);

            case "QUIT":
                return "BYE";

            default:
                return "ERR an unknown command " + parts[0];
        }
    }

    /// <summary>Draws the window the command names and writes the picture to the path.</summary>
    private string Capture(string[] parts)
    {
        if (parts.Length < 3)
        {
            return "ERR a capture needs a window id and a path";
        }

        string windowId = parts[1];
        string path = parts[2].Trim();

        string result = "ERR the window could not be drawn";

        try
        {
            // The window is looked up and drawn on the interface thread, because that is the only
            // thread that may read the visual tree of a window.
            _dispatcher.Invoke(() =>
            {
                Window? target = FindWindow(windowId);

                if (target is null)
                {
                    result = "ERR there is no window called " + windowId;
                    return;
                }

                ScreenCapture.Save(target, path);
                result = "OK " + path;
            });
        }
#pragma warning disable CA1031 // A failed capture must not take the channel down.
        catch (Exception ex)
        {
            StartupDiagnostics.Write("The capture failed.", ex);
            return "ERR " + ex.Message;
        }
#pragma warning restore CA1031

        if (result.StartsWith("OK", StringComparison.Ordinal))
        {
            StartupDiagnostics.Write("Captured " + windowId + " to " + path + ".");
        }

        return result;
    }

    /// <summary>Finds the open window with the given automation identifier.</summary>
    private static Window? FindWindow(string windowId)
    {
        // The window is named in full because this project has a namespace of its own called
        // Application, which would otherwise be taken for the WPF application.
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            string automationId = AutomationProperties.GetAutomationId(window);

            if (string.Equals(automationId, windowId, StringComparison.Ordinal))
            {
                return window;
            }
        }

        return null;
    }
}
