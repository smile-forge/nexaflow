using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Chemistry;

namespace Nexaflow.Tests.Visuals.Markdown.Chemistry;

/// <summary>
/// Writes the chemical-structure figures for the user documentation, through the real builder so the picture in the
/// docs is the picture the app draws. Opt-in: set <c>NEXAFLOW_WRITE_FIGURES</c> to the output folder.
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("writes a documentation figure")]
public class SmilesFigureWriter
{
    /// <summary>What the help page shows: the format's own example, and the molecules a reader is likely to try first.</summary>
    private const string Figure = """
        chemistry
        c1ccccc1 "Benzene"
        CC(=O)O "Acetic acid"
        CCO "Ethanol"
        CC(=O)Oc1ccccc1C(=O)O "Aspirin"
        Cn1cnc2c1c(=O)n(C)c(=O)n2C "Caffeine"
        OC[C@H]1OC(O)[C@H](O)[C@@H](O)[C@@H]1O "Glucose"
        """;

    /// <summary>Cages, which are drawn as the solids they are, with the bond at the back broken where it passes behind.</summary>
    private const string Cages = """
        C1C2CC3CC1CC(C2)C3 "Adamantane"
        C12C3C4C1C5C2C3C45 "Cubane"
        C1N2CN3CN1CN(C2)C3 "Hexamine"
        O=P12OP3(=O)OP(=O)(O1)OP(=O)(O2)O3 "Phosphorus pentoxide"
        CC12CC3CC(C)(C1)CC(N)(C3)C2 "Memantine"
        C1CC2CCC1C2 "Norbornane"
        """;

    [TestMethod]
    public void WriteSmilesFigure()
    {
        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_WRITE_FIGURES");
        if (string.IsNullOrEmpty(folder)) Assert.Inconclusive("set NEXAFLOW_WRITE_FIGURES to write the figure");

        Directory.CreateDirectory(folder);

        UiThread.Run(() =>
        {
            Write(Path.Combine(folder, "smiles.png"), Figure, MarkdownPalette.Light, Brushes.White, 720);
            Write(Path.Combine(folder, "smiles-cages.png"), Cages, MarkdownPalette.Light, Brushes.White, 720);
        });
    }

    private static void Write(string path, string source, MarkdownPalette palette, Brush ground, double width)
    {
        var host = new Border
        {
            Background = ground,
            Padding = new Thickness(16),
            Width = width,
            Child = SmilesBuilder.Element(source, DiagramRenderOptions.For(palette)),
        };

        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
                                            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var file = File.Create(path);
        encoder.Save(file);
    }
}
