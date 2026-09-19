using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;
using Nexaflow.Visuals.Text.Markdown.Music;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// Writes the figure of a score engraved from ABC, through the picture a reader copies or saves from the block, so
/// the figure is what the app draws. Writing is opt-in: set <c>NEXAFLOW_WRITE_FIGURES</c> to the output folder (the
/// help's <c>images/markdown</c> to refresh it). That the tune engraves with nothing wrong is checked every run.
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("writes documentation figures")]
public class MusicFigureWriter
{
    /// <summary>A reel with a repeat, beamed quavers and a lyric line — enough to show what the engraver does.</summary>
    private const string Reel =
        """
        X:1
        T:Speed the Plough
        R:reel
        C:Trad.
        M:4/4
        L:1/8
        K:G
        |:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|
        GABc dedB|dedB dedB|c2ec B2dB|A2AG A2:|
        w:one two three four five six sev-en eight
        """;

    [TestMethod]
    public void TheTuneEngravesWithNothingWrong() => UiThread.Run(() =>
    {
        var block = Drawn(Reel);
        Assert.AreEqual(0, block.Diagnostics.Count, string.Join(" | ", block.Diagnostics.Select(d => d.Message)));
    });

    [TestMethod]
    public void WriteFigure()
    {
        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_WRITE_FIGURES");
        if (string.IsNullOrEmpty(folder)) Assert.Inconclusive("set NEXAFLOW_WRITE_FIGURES to write the figure");

        Directory.CreateDirectory(folder);

        UiThread.Run(() =>
        {
            // The score is engraved in the dark theme's ink, so it needs the dark theme's paper under it. On its own
            // it is pale staves on nothing, which disappears wherever the figure is shown against white.
            var host = new Border
            {
                Background = Ground,
                Padding = new Thickness(18),
                Width = 760,
                Child = Drawn(Reel),
            };

            host.Measure(new Size(760, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();

            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
                                                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(Path.Combine(folder, "music-abc.png"));
            encoder.Save(stream);
        });
    }

    /// <summary>The dark theme's paper, the same near-black the other dark figures are drawn on.</summary>
    private static readonly Brush Ground = Freeze(new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x16)));

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>Engraved as the help page's own figures are drawn: in the dark theme, laid out for the page's width.</summary>
    private static ContentElement Drawn(string source)
    {
        var block = MusicScore.Engraved(MusicDialect.Abc, source, MarkdownPalette.Dark);
        block.Measure(new Size(720, double.PositiveInfinity));
        block.Arrange(new Rect(block.DesiredSize));
        return block;
    }
}
