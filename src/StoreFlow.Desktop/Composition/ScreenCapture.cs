using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StoreFlow.Desktop.Composition;

/// <summary>
/// Writes a picture of a window to a PNG file.
/// </summary>
/// <remarks>
/// <para>A shop owner who reports a problem can be asked for a picture of the screen, and the
/// support team can ask the application for one. The picture is taken from the window's own
/// visual tree, so it is exactly what the owner sees — including a right-to-left layout, which a
/// screen capture or a window message does not reproduce reliably.</para>
/// </remarks>
public static class ScreenCapture
{
    /// <summary>Writes a window to a PNG file.</summary>
    /// <param name="window">The window to photograph.</param>
    /// <param name="path">Where to write the file.</param>
    public static void Save(Window window, string path)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // The window has to have been laid out, otherwise the picture is empty.
        if (window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            throw new InvalidOperationException("The window has not been laid out yet.");
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(window.ActualWidth),
            (int)Math.Round(window.ActualHeight),
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(full);
        encoder.Save(stream);
    }
}
