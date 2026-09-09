using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// What laying out and drawing a page actually costs, so that a change to how it is stored can be shown
/// to have worked rather than argued to have.
///
/// <para>
/// Three numbers per subject, because they answer different questions and only one of them was ever
/// measured. <em>Compose</em> is building the tree — the 3 ms that a keystroke pays today. <em>Draw</em>
/// is executing the marks into a drawing context, which is what a repaint costs when nothing has moved.
/// <em>Raster</em> is that plus turning it into pixels. Whether it is worth caching drawn output as well
/// as composed layout turns entirely on the second number, and nobody has taken it.
/// </para>
/// <para>
/// Opt in with <c>NEXAFLOW_LAYOUT_BENCH</c> pointing at a folder; it appends, so a run before and a run
/// after land in one file side by side.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("a measurement, not a check")]
[DoNotParallelize]
public class LayoutCostBench
{
    private const int Passes = 200;

    private static readonly (string What, string Abc)[] Tunes =
    [
        ("a short tune", "X:1\nT:Speed the Plough\nM:4/4\nL:1/8\nK:G\n|:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|\n"),
        ("a tune with words", "X:1\nT:the Auld Grey Cat\nM:C|\nL:1/8\nK:EDorian\n"
                              + "\"Em\"e2e2 E3F | \"D\"GFGA BABc |\nw: one two three four five six sev-en eight\n"),
    ];

    private static readonly (string What, string Latex)[] Formulas =
    [
        ("a matrix", @"\begin{pmatrix} a & 4b^{2}+3 \\ c^4 & d+3i \end{pmatrix}"),
        ("a quadratic", @"\frac{-b \pm \sqrt{b^2-4ac}}{2a}"),
        ("a sum and an integral", @"\sum_{i=0}^{n} \alpha_i x^i = \int_0^\infty e^{-t}\,dt"),
    ];

    [TestMethod]
    public void WhatAPageCostsToLayOutAndToDraw() => UiThread.Run(() =>
    {
        var into = Environment.GetEnvironmentVariable("NEXAFLOW_LAYOUT_BENCH");
        if (string.IsNullOrWhiteSpace(into)) { Assert.Inconclusive("set NEXAFLOW_LAYOUT_BENCH"); return; }

        var lines = new List<string> { $"=== {DateTime.Now:yyyy-MM-dd HH:mm} — {Passes} passes each" };

        foreach (var (what, abc) in Tunes)
            lines.Add(Measure(what, () => AbcLayout.Build(abc, 700, Brushes.Black, 1.0),
                              layout => (layout.Root, layout.Size, (Action<DrawingContext>)(dc => layout.Paint(dc, Brushes.Black)))));

        foreach (var (what, latex) in Formulas)
            lines.Add(Measure(what, () => LatexLayout.Build(latex, 22)!,
                              layout => (layout.Tree.Root, layout.Size, dc => layout.Paint(dc, Brushes.Black))));

        lines.Add("");
        File.AppendAllLines(Path.Combine(into, "layout-cost.txt"), lines);
    });

    /// <summary>
    /// One subject, measured three ways. Every phase is warmed before it is timed — the first timed loop
    /// otherwise pays for jitting the whole path and makes composition look dearer than composition plus
    /// drawing, which is how the first version of this lied.
    /// </summary>
    private static string Measure<T>(string what, Func<T> compose,
                                     Func<T, (Piece Root, Size Size, Action<DrawingContext> Paint)> read)
    {
        for (var pass = 0; pass < 40; pass++) Draw(read(compose()));

        var laid = compose();
        var (root, size, paint) = read(laid);

        var nodes = root.SelfAndDescendants().Count();
        var ink = root.Ink().Count();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var composing = Stopwatch.StartNew();
        for (var pass = 0; pass < Passes; pass++) compose();
        composing.Stop();

        var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / Passes;

        var drawing = Stopwatch.StartNew();
        for (var pass = 0; pass < Passes; pass++) Draw((root, size, paint));
        drawing.Stop();

        var rastering = Stopwatch.StartNew();
        for (var pass = 0; pass < Passes; pass++) Raster((root, size, paint));
        rastering.Stop();

        return $"{what,-24} compose {Per(composing):F3} ms   draw {Per(drawing):F3} ms   "
               + $"raster {Per(rastering):F3} ms   {nodes} pieces ({ink} ink)   {bytes / 1024.0:F1} KB composing";
    }

    private static double Per(Stopwatch clock) => clock.Elapsed.TotalMilliseconds / Passes;

    /// <summary>The marks executed into a drawing context — what a repaint costs when nothing has moved.</summary>
    private static void Draw((Piece Root, Size Size, Action<DrawingContext> Paint) laid)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        laid.Paint(dc);
    }

    /// <summary>…and that turned into pixels, which is what actually reaches the screen.</summary>
    private static void Raster((Piece Root, Size Size, Action<DrawingContext> Paint) laid)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) laid.Paint(dc);

        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)laid.Size.Width), Math.Max(1, (int)laid.Size.Height),
                                            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
    }
}
