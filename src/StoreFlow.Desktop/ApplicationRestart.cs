using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace StoreFlow.Desktop;

/// <summary>
/// Starts the programme again, for the moments when the shop's database has been replaced
/// underneath it.
/// </summary>
/// <remarks>
/// A restore puts a different database file on disk. The running programme is still holding the
/// old one, and every sale it takes from that moment on would be written to a file that is no
/// longer the shop's. Rather than ask the owner to remember to close and reopen the programme,
/// the programme restarts itself.
/// </remarks>
public static class ApplicationRestart
{
    /// <summary>
    /// Starts the programme again and closes this one.
    /// </summary>
    public static void Restart()
    {
        string? executable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executable))
        {
            System.Windows.Application.Current.Shutdown();

            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty,
            });
        }
        catch (Win32Exception)
        {
            // The new copy could not be started. Closing this one anyway would leave the shop
            // with no programme at all, so this one stays up and the owner is told by the
            // message the wizard has already shown.
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }
}
