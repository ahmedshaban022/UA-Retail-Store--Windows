using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Printing;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Draws a printable document on a canvas, using the same layout the printer uses.
/// </summary>
/// <remarks>
/// The preview is not a second rendering of the sale: it is the same layout, drawn with the
/// interface's own text. That is what makes it worth trusting (PRD §5.1 GEN-05).
/// </remarks>
public static class ReceiptPreview
{
    /// <summary>How many interface units there are in a point, at the usual 96 dots per inch.</summary>
    public const double UnitsPerPoint = 96d / 72d;

    /// <summary>
    /// Fills the canvas with the document, laid out to the given width.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="document">The document to draw.</param>
    /// <param name="contentWidth">The width of the text, in points.</param>
    public static void Fill(Canvas canvas, PrintDocumentData document, double contentWidth)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(document);

        canvas.Children.Clear();

        foreach (PrintTextLine line in PrintLayout.Build(document, contentWidth))
        {
            var text = new TextBlock
            {
                Text = line.Text,
                FontSize = line.FontSize * UnitsPerPoint,
                FontWeight = line.IsBold ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = line.Alignment switch
                {
                    PrintTextAlignment.Center => TextAlignment.Center,
                    PrintTextAlignment.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                },
                Width = line.Width * UnitsPerPoint,
                Height = line.Height * UnitsPerPoint,
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(text, line.Left * UnitsPerPoint);
            Canvas.SetTop(text, line.Top * UnitsPerPoint);

            canvas.Children.Add(text);
        }
    }

    /// <summary>
    /// The height the document needs, in interface units.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="contentWidth">The width of the text, in points.</param>
    /// <returns>The height, in interface units.</returns>
    public static double MeasureHeight(PrintDocumentData document, double contentWidth)
    {
        ArgumentNullException.ThrowIfNull(document);

        return PrintLayout.HeightOf(PrintLayout.Build(document, contentWidth)) * UnitsPerPoint;
    }
}
