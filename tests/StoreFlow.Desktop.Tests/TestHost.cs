using System.ComponentModel;
using System.Runtime.CompilerServices;
using StoreFlow.Desktop.Theming;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Prepares the test host so that WPF behaves the way it does in the application.
/// </summary>
/// <remarks>
/// <para>WPF keeps a single application-wide resource assembly and a single dispatcher. Tests
/// that touch either of them interfere with each other, so the tests in this project run one
/// after another rather than side by side.</para>
/// <para>Pointing the resource assembly at the desktop assembly makes the application's own
/// resources reachable, so a test can ask for them the way the application does.</para>
/// </remarks>
internal static class TestHost
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            System.Windows.Application.ResourceAssembly = typeof(ThemeEngine).Assembly;
        }
        catch (InvalidOperationException)
        {
            // The property was already read, so WPF has fixed the application assembly. The tests
            // that need the desktop resources address them by assembly instead.
        }
    }

    /// <summary>Runs an action on a single-threaded-apartment thread, as WPF requires.</summary>
    /// <param name="action">The work to carry out.</param>
    internal static void RunOnStaThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // The exception is captured and rethrown on the calling thread.
            catch (Exception exception)
            {
                failure = exception;
            }
#pragma warning restore CA1031
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }
}
