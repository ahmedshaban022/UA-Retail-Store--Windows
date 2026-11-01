using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shouldly;
using StoreFlow.Desktop.Localization;
using Xunit.Abstractions;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// How a piece of text written left to right is kept in order inside an Arabic screen.
/// </summary>
/// <remarks>
/// <para>A code, a moment in time, a summary with a code in it: each is written left to right and
/// each must be read the same way in an Arabic window as in an English one. The brackets and the
/// hyphens inside them are part of the text, and a text engine that moves them to the other end of
/// the line turns <c>Rice 1kg (RICE-001)</c> into <c>(Rice 1kg (RICE-001</c>.</para>
/// <para>The invisible marks of the Unicode isolate are not enough. WPF's text engine treats them
/// as marks to be ignored, so the text they surround is laid out as if they were not there, and a
/// bracket at the end of the text ends up at the other end of the line. What does work is the
/// direction of the cell itself, and that is what the tables use.</para>
/// </remarks>
public sealed class BidiRenderingTests
{
    private readonly ITestOutputHelper _output;

    public BidiRenderingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void A_cell_written_left_to_right_reads_the_same_in_an_arabic_window()
    {
        TestHost.RunOnStaThread(() =>
        {
            const string text = "Rice 1kg (RICE-001)";

            byte[] english = Render(text, FlowDirection.LeftToRight, leftToRightCell: true);
            byte[] arabic = Render(text, FlowDirection.RightToLeft, leftToRightCell: true);

            arabic.ShouldBe(english);
        });
    }

    [Fact]
    public void A_moment_in_time_keeps_its_date_and_its_time_in_one_line()
    {
        TestHost.RunOnStaThread(() =>
        {
            const string text = "2026-11-01 07:53";

            byte[] english = Render(text, FlowDirection.LeftToRight, leftToRightCell: true);
            byte[] arabic = Render(text, FlowDirection.RightToLeft, leftToRightCell: true);

            arabic.ShouldBe(english);
        });
    }

    [Fact]
    public void The_isolate_marks_alone_do_not_carry_the_text()
    {
        TestHost.RunOnStaThread(() =>
        {
            const string text = "Rice 1kg (RICE-001)";

            byte[] english = Render(text, FlowDirection.LeftToRight, leftToRightCell: true);
            byte[] arabicWithMarks = Render(text, FlowDirection.RightToLeft, leftToRightCell: false, wrap: BidiText.Isolate);

            // The marks are in the string, and the text engine does not read them: the Arabic cell
            // with the marks is the same as the Arabic cell without them, and neither is the
            // English cell. This is why the tables give the cell its own direction.
            arabicWithMarks.ShouldNotBe(english);
        });
    }

    [Fact]
    public void What_the_three_ways_of_keeping_a_code_read_the_same_way()
    {
        TestHost.RunOnStaThread(() =>
        {
            const string text = "Rice 1kg (RICE-001)";

            byte[] english = Render(text, FlowDirection.LeftToRight, leftToRightCell: true);
            byte[] plainArabic = Render(text, FlowDirection.RightToLeft, leftToRightCell: false);
            byte[] isolateArabic = Render(text, FlowDirection.RightToLeft, leftToRightCell: false, wrap: BidiText.Isolate);
            byte[] embeddingArabic = Render(text, FlowDirection.RightToLeft, leftToRightCell: false, wrap: BidiText.Embed);
            byte[] flowArabic = Render(text, FlowDirection.RightToLeft, leftToRightCell: true);

            _output.WriteLine("plain in Arabic    == english : " + Same(plainArabic, english));
            _output.WriteLine("isolate in Arabic  == english : " + Same(isolateArabic, english));
            _output.WriteLine("embedding in Arabic == english : " + Same(embeddingArabic, english));
            _output.WriteLine("left-to-right cell == english : " + Same(flowArabic, english));
            _output.WriteLine("isolate in Arabic  == plain Arabic : " + Same(isolateArabic, plainArabic));
        });
    }

    private static bool Same(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Renders a piece of text and returns its ink.</summary>
    private static byte[] Render(
        string text,
        FlowDirection direction,
        bool leftToRightCell,
        Func<string?, string>? wrap = null)
    {
        var block = new TextBlock
        {
            Text = wrap is null ? text : wrap(text),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            FlowDirection = leftToRightCell ? FlowDirection.LeftToRight : direction,
            TextWrapping = TextWrapping.NoWrap,
        };

        var host = new Border { Child = block, FlowDirection = direction };

        host.Measure(new Size(600, 100));
        host.Arrange(new Rect(0, 0, 600, 100));
        host.UpdateLayout();

        var size = new Size(Math.Max(1, host.DesiredSize.Width), Math.Max(1, host.DesiredSize.Height));
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);

        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, (int)(bitmap.PixelWidth * 4), 0);

        return pixels;
    }
}
