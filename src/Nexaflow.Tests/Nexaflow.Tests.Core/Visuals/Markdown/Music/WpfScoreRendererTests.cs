using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Parsers;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;
using MDuration = Nexaflow.Visuals.Text.Markdown.Music.Model.Duration;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Music;

/// <summary>
/// The native engraver produces a sized element and paints without faulting. Rendering to a
/// <see cref="RenderTargetBitmap"/> forces <c>OnRender</c> to run (measure/arrange alone would not), so
/// this exercises every draw path — clef, key/time, notes, stems, beams, bar lines — end to end.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("score-renderer")]
public class WpfScoreRendererTests
{
    private const string SpeedThePlough =
        "X:1\nT:Speed the Plough\nM:4/4\nC:Trad.\nK:G\n" +
        "|:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|\n" +
        "  GABc dedB|dedB dedB|c2ec B2dB|A2F2 G4:|\n";

    private static void ForceRender(FrameworkElement fe, double width)
    {
        fe.Measure(new Size(width, double.PositiveInfinity));
        fe.Arrange(new Rect(new Point(0, 0), fe.DesiredSize));
        fe.UpdateLayout();
        int w = Math.Max(1, (int)Math.Ceiling(fe.DesiredSize.Width));
        int h = Math.Max(1, (int)Math.Ceiling(fe.DesiredSize.Height));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(fe);   // throws if any draw path faults
    }

    [TestMethod]
    public void SpeedThePlough_RendersToSizedElement() => UiThread.Run(() =>
    {
        var score = new AbcParser().Parse(SpeedThePlough);
        var fe = WpfScoreRenderer.Render(score, MarkdownPalette.Dark);
        Assert.IsNotNull(fe);
        ForceRender(fe, 680);
        Assert.IsTrue(fe.DesiredSize.Width > 100, "score should have a real width");
        Assert.IsTrue(fe.DesiredSize.Height > 60, "score should have a real height");
    });

    [TestMethod]
    public void BassClefWholeAndQuarterNotes_Render() => UiThread.Run(() =>
    {
        // A bass-staff line like LilyPond's cantus firmus: clef change, whole note, ledger notes.
        var score = new Score();
        var st = new Staff { Clef = ClefKind.Bass, Key = KeySignature.CMajor, Time = new TimeSignature(4, 4) };
        var m1 = new Measure();
        foreach (var (step, oct) in new[] { (0, 3), (0, 4), (6, 3), (5, 3) })
            m1.Events.Add(new Note { Pitch = new Pitch(step, 0, oct), Duration = MDuration.Quarter });
        var m2 = new Measure { EndBarline = BarlineKind.Final };
        m2.Events.Add(new Note { Pitch = new Pitch(0, 0, 4), Duration = MDuration.Whole });
        st.Measures.Add(m1);
        st.Measures.Add(m2);
        score.Staves.Add(st);

        var fe = WpfScoreRenderer.Render(score, MarkdownPalette.Dark);
        ForceRender(fe, 680);
        Assert.IsTrue(fe.DesiredSize.Height > 60);
    });

    [TestMethod]
    public void RendersOnLightPalette_Too() => UiThread.Run(() =>
    {
        var score = new AbcParser().Parse(SpeedThePlough);
        var fe = WpfScoreRenderer.Render(score, MarkdownPalette.Light);
        ForceRender(fe, 520);   // narrower → forces more system line-breaks
        Assert.IsNotNull(fe);
    });

    [TestMethod]
    public void AllLinesExceptLast_ShareOneWidth() => UiThread.Run(() =>
    {
        // Four full 4-bar lines → every line (incl. the full last one) reaches the same right edge.
        // Rendered wide, so the old stretch-cap bug would have made them unequal.
        const string fourLines =
            "X:1\nT:Speed the Plough\nM:4/4\nK:G\n" +
            "|:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|\n" +
            "  GABc dedB|dedB dedB|c2ec B2dB|A2F2 G4:|\n" +
            "|:g2gf gdBd|g2f2 e2d2|c2ec B2dB|c2A2 A2df|\n" +
            "  g2gf g2Bd|g2f2 e2d2|c2ec B2dB|A2F2 G4:|\n";
        var se = (ScoreElement)WpfScoreRenderer.Render(new AbcParser().Parse(fourLines), MarkdownPalette.Dark);
        ForceRender(se, 1000);
        var rights = se.SystemRightEdges;
        Assert.IsTrue(rights.Count >= 3, "expected several systems");
        for (int i = 1; i < rights.Count; i++)
            Assert.AreEqual(rights[0], rights[i], 1.5, $"system {i} width differs from system 0");
    });

    [TestMethod]
    public void Selection_NoteClick_MeasureClick_DragExtends_Clear() => UiThread.Run(() =>
    {
        var score = new AbcParser().Parse(SpeedThePlough);
        var se = (ScoreElement)WpfScoreRenderer.Render(score, MarkdownPalette.Light);
        se.Measure(new Size(700, double.PositiveInfinity));
        se.Arrange(new Rect(new Point(0, 0), se.DesiredSize));
        se.UpdateLayout();

        // A pointer-down directly ON a note head selects just that note.
        var head = se.HeadCenterOf(10);
        Assert.IsNotNull(head, "note #10 should have a laid-out head");
        se.BeginPointerSelect(head!.Value);
        se.EndPointerSelect();
        Assert.AreEqual((10, 10), se.SelectedRange, "clicking a note head selects that single note");

        // A pointer-down on the measure background (same x, off the head vertically) selects the measure.
        se.BeginPointerSelect(new Point(head.Value.X, head.Value.Y + 30));
        se.EndPointerSelect();
        var measure = se.SelectedRange!.Value;
        Assert.IsTrue(measure.Start <= 10 && measure.End >= 10 && measure.End > measure.Start,
            "clicking the background selects the note's whole measure");

        // A drag extends note-by-note from the anchor.
        var from = se.HeadCenterOf(8)!.Value;
        se.BeginPointerSelect(from);
        se.ExtendPointerSelect(new Point(se.DesiredSize.Width - 30, from.Y));
        se.EndPointerSelect();
        var grown = se.SelectedRange!.Value;
        Assert.AreEqual(8, grown.Start, "drag anchors on the initial note");
        Assert.IsTrue(grown.End > 15, "drag extends past the anchor's measure");

        bool fired = false;
        se.SelectionChanged += (_, __) => fired = true;
        se.ClearSelection();
        Assert.IsNull(se.SelectedRange, "clear removes the selection");
        Assert.IsTrue(fired, "SelectionChanged fires on clear");
    });

    [TestMethod]
    public void Selection_IsCleared_WhenAnotherBlockOrTextTakesIt() => UiThread.Run(() =>
    {
        var a = (ScoreElement)WpfScoreRenderer.Render(new AbcParser().Parse(SpeedThePlough), MarkdownPalette.Light);
        var b = (ScoreElement)WpfScoreRenderer.Render(new AbcParser().Parse(SpeedThePlough), MarkdownPalette.Light);
        foreach (var se in new[] { a, b })
        {
            se.Measure(new Size(700, double.PositiveInfinity));
            se.Arrange(new Rect(new Point(0, 0), se.DesiredSize));
            se.UpdateLayout();
        }

        a.SelectMeasureAt(new Point(a.DesiredSize.Width / 2, 85));
        Assert.IsNotNull(a.SelectedRange, "a is selected");

        b.SelectMeasureAt(new Point(b.DesiredSize.Width / 2, 85));
        Assert.IsNotNull(b.SelectedRange, "b is selected");
        Assert.IsNull(a.SelectedRange, "selecting b must clear a");

        InteractiveSelection.ClearActive();   // a plain text/background click on the page
        Assert.IsNull(b.SelectedRange, "ClearActive drops the remaining selection");
    });

    [TestMethod]
    public void BravuraFont_IsBundledAndLoadable()
    {
        // Not gating (the engraver falls back to geometry), but flags a broken font copy early.
        Assert.IsTrue(WpfScoreRenderer.FontAvailable,
            "Bravura.otf did not load from the output MusicFonts folder — check the csproj CopyToOutputDirectory.");
    }
}
