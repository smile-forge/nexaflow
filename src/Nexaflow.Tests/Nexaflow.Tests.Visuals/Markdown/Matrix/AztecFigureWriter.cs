using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// Writes the Aztec figure for the user documentation, through the real renderer so the picture in the
/// docs is the picture the app draws. Opt-in: set <c>NEXAFLOW_WRITE_FIGURES</c> to the output folder.
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("writes a documentation figure")]
public class AztecFigureWriter
{
    [TestMethod]
    public void WriteAztecFigure()
    {
        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_WRITE_FIGURES");
        if (string.IsNullOrEmpty(folder)) Assert.Inconclusive("set NEXAFLOW_WRITE_FIGURES to write the figure");

        Directory.CreateDirectory(folder);

        UiThread.Run(() =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White };

            foreach (var (caption, source) in ((string, string)[])
                     [
                         ("compact",     "type: url\nurl: https://markdown.org\ncellSize: 6"),
                         ("full range",  "type: url\nurl: https://markdown.org\nformat: full\ncellSize: 6"),
                         ("GS1",         "type: gs1\ndata: (01)04150123456782(10)LOT7(21)SN9\ncellSize: 6"),
                         ("styled",      "type: text\ntext: An Aztec Code\ncellSize: 6\ndark: #1D4ED8\nlight: #EFF6FF"),
                     ])
            {
                Assert.IsTrue(AztecBlockParser.TryParse(source, out var block, out string? error), error);

                var column = new StackPanel
                {
                    Margin            = new Thickness(14, 12, 14, 6),
                    VerticalAlignment = VerticalAlignment.Bottom,   // so the captions share a baseline
                };
                column.Children.Add(WpfAztecRenderer.Render(block!, MarkdownPalette.Light));
                column.Children.Add(new TextBlock
                {
                    Text                = caption,
                    FontFamily          = new FontFamily("Segoe UI"),
                    FontSize            = 12,
                    Foreground          = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                row.Children.Add(column);
            }

            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            row.Arrange(new Rect(row.DesiredSize));
            row.UpdateLayout();

            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(row.DesiredSize.Width),
                                                (int)Math.Ceiling(row.DesiredSize.Height),
                                                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(row);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            string path = Path.Combine(folder, "aztec.png");
            using var file = File.Create(path);
            encoder.Save(file);
        });
    }
}
