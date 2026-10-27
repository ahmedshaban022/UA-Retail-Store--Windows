using System.Windows;
using System.Windows.Threading;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.Theming;

namespace StoreFlow.Desktop;

/// <summary>
/// The WPF application entry point. It builds the host, prepares the data folder, applies the saved
/// theme and language, and then shows the first-run wizard, the login window or the main shell.
/// </summary>
public partial class App : System.Windows.Application
{
    private AppHost? _host;

    /// <summary>The running host, so views can reach a service they need.</summary>
    public static AppHost? Host { get; private set; }

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ArgumentNullException.ThrowIfNull(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            StartupDiagnostics.Write("Starting StoreFlow.");

            _host = await AppHost.CreateAsync();
            Host = _host;

            ThemeEngine.Apply(_host.Resources, _host.Theme);

            // Which face the interface ends up using is worth writing down: a shop PC has no
            // debugger, and "the Arabic looks wrong" is otherwise unanswerable.
            StartupDiagnostics.Write(
                "Font resolution:" + Environment.NewLine +
                ThemeEngine.DescribeFontResolution(_host.Theme, _host.Theme.Locale.Default));

            var window = await _host.ShowStartupWindowAsync();
            window.Show();

            _host.ShellReady();

            ScheduleCapture(window, e.Args);

            StartupDiagnostics.Write("Shell is on screen.");
        }
#pragma warning disable CA1031 // Do not catch general Exception - this is the outermost boundary
        catch (Exception ex)
        {
            // Anything that reaches here is a start-up failure, and it must be recorded and
            // reported rather than swallowed.
            StartupDiagnostics.Write("Start-up failed.", ex);

            MessageBox.Show(
                "StoreFlow could not start. The details were written to:\n" +
                StartupDiagnostics.LogPath,
                "StoreFlow",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
#pragma warning restore CA1031
    }

    /// <summary>Records any exception that escapes the dispatcher, then lets WPF handle it.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.Write("Unhandled dispatcher exception.", e.Exception);
    }

    /// <summary>
    /// Writes a picture of the shell when the application was started with
    /// <c>--capture &lt;path&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The picture is taken by the application itself rather than by a screen capture, because a
    /// right-to-left window is not reproduced faithfully by the tools that capture other windows.
    /// </remarks>
    private static void ScheduleCapture(Window window, string[] args)
    {
        const string Switch = "--capture";

        int index = Array.IndexOf(args, Switch);

        if (index < 0 || index + 1 >= args.Length)
        {
            return;
        }

        string path = args[index + 1];

        // The picture is taken a moment after the window is first drawn, so that the first page
        // has finished loading and the picture shows the interface rather than an empty frame.
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2.5),
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();

            try
            {
                ScreenCapture.Save(window, path);
                StartupDiagnostics.Write("Captured the shell to " + path);
            }
#pragma warning disable CA1031 // A failed capture must not take the application down.
            catch (Exception ex)
            {
                StartupDiagnostics.Write("The capture failed.", ex);
            }
#pragma warning restore CA1031
        };

        window.ContentRendered += (_, _) => timer.Start();
    }
}
