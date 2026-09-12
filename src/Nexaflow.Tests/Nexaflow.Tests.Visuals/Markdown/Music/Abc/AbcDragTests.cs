using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music;


namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// Dragging across a real tune, every pair of pieces, looking for the one that throws.
///
/// <para>
/// A selection is driven by a pointer, so it is asked about pairs nobody would choose deliberately: a
/// note and a chord three systems away, a grace note and a syllable, the same piece twice. Every one of
/// those has to come back with an answer. The suite had tests for the pairs that mean something and none
/// for the pairs that merely happen, which is why a drag across this tune took the page down.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class AbcDragTests
{
    /// <summary>
    /// The tune the trouble was found on: grace notes, chord symbols, repeats, and a line continuation.
    /// </summary>
    private const string AuldGreyCat =
        "X: 1\nT: the Auld Grey Cat\nM: C|\nL: 1/8\nK: EDorian\n"
        + "z2 |\\\n"
        + "\"Em\"{^d}e2e2 E3F | GFGA BABc | \"D\"{c}d2d2 D3E | FAdB AFED |\n"
        + "\"Em\"{^d}e2e2 E3F | GFGA BABc | \"D\"dcBA \"B7\"BAGF | \"Em\"E4 e2z2 :|\n";

    [TestMethod]
    public void EveryDragAcrossItComesBackWithAnAnswer() => UiThread.Run(() =>
    {
        var layout = AbcBuilder.Build(AuldGreyCat, 900, Brushes.Black, 1.0);
        var pieces = layout.Root.Leaves().ToList();

        Assert.IsTrue(pieces.Count > 20, $"only {pieces.Count} pieces — the tune did not engrave");

        var trouble = new List<string>();

        foreach (var from in pieces)
            foreach (var to in pieces)
            {
                try
                {
                    var chosen = ContentSelection.Between(layout.Root, from, to);

                    foreach (var (start, length) in chosen.Ranges)
                        if (start < 0 || length < 0 || start + length > AuldGreyCat.Length)
                            trouble.Add($"{Kind(from)}→{Kind(to)}: range {start}+{length} is outside the tune");
                }
                catch (Exception ex)
                {
                    trouble.Add($"{Kind(from)}→{Kind(to)}: {ex.GetType().Name}: {ex.Message}");
                }

                if (trouble.Count > 4) break;
            }

        Assert.AreEqual(0, trouble.Count, string.Join("\n", trouble.Take(5)));
    });

    [TestMethod]
    public void NothingThatDrewNothingIsLeftAsSomethingToPointAt() => UiThread.Run(() =>
    {
        // The invariant the crash came from, asserted where it belongs. Ink is a promise that a reader can
        // point at the thing; an empty rectangle cannot be pointed at, hit-tested, washed or stood beside,
        // and every query that trusted the promise had to survive one that could not keep it. One of them
        // did not — inflating an empty rectangle throws, inside OnRender, which stops the element being
        // drawn at all.
        foreach (var (what, abc) in AbcConstructs.Everything.Concat([("the Auld Grey Cat", AuldGreyCat)]))
        {
            var layout = AbcBuilder.Build(abc, 700, Brushes.Black, 1.0);

            foreach (var node in layout.Root.Leaves())
                Assert.IsTrue(!node.Bounds.IsEmpty && node.Bounds.Width > 0 && node.Bounds.Height > 0,
                              $"{what}: a {Kind(node)} is ink and drew nothing");
        }
    });

    [TestMethod]
    public void ZoomTheGraces() => UiThread.Run(() =>
    {
        var into = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_COMPARE");
        if (string.IsNullOrWhiteSpace(into)) { Assert.Inconclusive("set NEXAFLOW_ABC_COMPARE"); return; }

        const string One = "X:1\nL:1/8\nK:G\n{g}A {/g}B {^d}c {gAG}d |\n";

        var element = MusicScore.Engraved(MusicDialect.Abc, One, MarkdownPalette.Light, zoom: 4.0);
        element.Measure(new System.Windows.Size(1600, double.PositiveInfinity));
        element.Arrange(new System.Windows.Rect(new System.Windows.Point(0, 0), element.DesiredSize));

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)element.DesiredSize.Width, (int)element.DesiredSize.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(into, "zoom-grace.png"));
        encoder.Save(stream);
    });

    [TestMethod]
    public void AScoreFillsThePageItIsGivenAndSitsInTheMiddleOfIt() => UiThread.Run(() =>
    {
        // What a window hands a block, and what the block does with it. The music is set into a share of
        // the width and centred in the rest — so what it must never do is take a width of its own choosing.
        const double Given = 1284;

        var score = new MusicScore(MusicDialect.Abc, AuldGreyCat, MarkdownPalette.Light, 0);
        score.Measure(new System.Windows.Size(Given, double.PositiveInfinity));
        score.Arrange(new System.Windows.Rect(new System.Windows.Point(0, 0), score.DesiredSize));
        score.UpdateLayout();

        Assert.AreEqual(Given, score.DesiredSize.Width, 1,
                        "the block is the page: it takes the whole width and puts the margins inside");

        var music = score.Score.Laid.Size.Width * 1.0;
        var wanted = Given * score.PageWidth;

        Assert.IsTrue(music > wanted * 0.9,
                      $"the music engraved to {music:F0} of the {wanted:F0} it was given — it is not filling the page");
    });

    [TestMethod]
    public void AndEveryLineOfAShortTuneComesOutTheSameLength() => UiThread.Run(() =>
    {
        // Five one-bar lines, each ended by the writer rather than by the width. None of them fills the
        // page and none of them should — but they have to agree with each other, or the block reads as
        // ragged rather than as short. The width is chosen once for the block; this is what says every
        // line actually reached it.
        const string Meters =
            "X:1\nM:4/4\nK:C\nA4|\nM:C\nA4|\nM:C|\nA4|\nM:6/8\nA3A3|\nM:none\nA4|\n";

        var layout = AbcBuilder.Build(Meters, 900, Brushes.Black, 1.0);

        var ends = layout.Root.SelfAndDescendants()
            .Where(n => Kind(n) == "system")
            .Select(n => n.Bounds.Right)
            .ToList();

        Assert.AreEqual(5, ends.Count, "five lines, one per meter");

        // To the pixel, not to the last decimal: the ends are the sum of a chain of doubles, so they agree
        // to a rounding error and asserting exact equality would be asserting the arithmetic rather than
        // the engraving.
        Assert.IsTrue(ends.Max() - ends.Min() <= 1.5,
                      $"the lines came out different lengths: {string.Join(", ", ends.Select(e => e.ToString("F0")))}");
    });

    [TestMethod]
    public void ADragAlongTheStaffNeverTakesTheChordOverIt() => UiThread.Run(() =>
    {
        // Reported from the app: a drag along the notes took the chord over the next bar the moment it reached
        // the bar line. The bar line was on no run, so the drag fell back to everything written between its two
        // ends — and a chord symbol is written between a bar line and the note it stands over. Reaching a beam
        // by its own bar did the same.
        const string Tune = "X:1\nL:1/8\nK:C\n\"C\"C2E2 G3A | \"G\"GFED CDEF |\n";
        var layout = AbcBuilder.Build(Tune, 900, Brushes.Black, 1.0);
        var first = Of(layout, "note")[0];
        var chords = Of(layout, "chord");

        var ends = layout.Root.SelfAndDescendants().Where(n => Kind(n) is "barline" or "beam-bar").ToList();
        Assert.IsTrue(ends.Count >= 3, "the bar lines, and the beams the drag can reach by their bars");

        foreach (var end in ends)
        {
            var chosen = ContentSelection.Between(layout.Root, first, end.Selectable());
            foreach (var chord in chords)
                Assert.IsFalse(Taken(chosen, chord), $"a drag along the staff to a {Kind(end)} took the chord {Written(Tune, chord)}");
        }
    });

    [TestMethod]
    public void ADragAlongTheStaffTakesTheRestsOnIt() => UiThread.Run(() =>
    {
        // A rest is on the staff as much as a note is. Left off the staff's run, a drag from one side of it to
        // the other passed it by — and the wash, joined across the gap, claimed it anyway.
        const string Tune = "X:1\nL:1/4\nK:C\nC z E z | G2 z2 |\n";
        var layout = AbcBuilder.Build(Tune, 900, Brushes.Black, 1.0);
        var notes = Of(layout, "note");

        var chosen = ContentSelection.Between(layout.Root, notes[0], notes[^1]);
        var passed = Of(layout, "rest").Where(rest => rest.Sits().Start < notes[^1].Sits().Start).ToList();

        Assert.AreEqual(2, passed.Count, "the two rests between the first note and the last");
        foreach (var rest in passed)
            Assert.IsTrue(Taken(chosen, rest), $"the rest at {rest.Sits().Start} was dragged across and not taken");
    });

    [TestMethod]
    public void TheWashIsOneShapeAcrossTheGapsInWhatWasTaken() => UiThread.Run(() =>
    {
        // Reported from the app: each note washed on its own, so a drag along a staff read as a scatter of
        // separate selections. The gaps between what was taken are spanned — along the staff, and down from the
        // notes to the words under them when both were taken.
        const string Tune = "X:1\nL:1/4\nK:C\nC D E F | G A B c |\nw: one two three four five six sev-en eight\n";
        var layout = AbcBuilder.Build(Tune, 900, Brushes.Black, 1.0);
        var notes = Of(layout, "note");
        var words = Of(layout, "syllable");

        var along = Washed(layout, ContentSelection.Between(layout.Root, notes[0], notes[5]));
        Assert.IsTrue(along.FillContains(Gap(notes[0].Ink(), notes[1].Ink(), across: true)),
                      "the space between two notes taken together is washed");
        Assert.IsTrue(along.FillContains(Gap(notes[3].Ink(), notes[4].Ink(), across: true)),
                      "and so is the bar line the drag took with them");

        var block = Washed(layout, ContentSelection.Between(layout.Root, notes[1], words[2]));
        Assert.IsTrue(block.FillContains(Gap(notes[2].Ink(), words[2].Ink(), across: false)),
                      "a block from the notes down to the words is washed between them");
    });

    [TestMethod]
    public void TheWashNeverReachesAcrossWordsNobodyTook() => UiThread.Run(() =>
    {
        // Two lines of music with their words under each. A drag along the staff from one line into the next
        // takes notes on both and none of the words between, and the wash has to say so: spanning down from one
        // staff to the other would claim a line of lyrics nobody selected.
        const string Tune = "X:1\nL:1/4\nK:C\nC D E F |\nw: one two three four\nG A B c |\nw: five six sev-en eight\n";
        var layout = AbcBuilder.Build(Tune, 900, Brushes.Black, 1.0);
        var notes = Of(layout, "note");
        var words = Of(layout, "syllable");

        var chosen = ContentSelection.Between(layout.Root, notes[0], notes[5]);
        Assert.IsFalse(Taken(chosen, words[0]), "the words are not what was dragged along");

        var wash = Washed(layout, chosen);
        foreach (var word in words.Take(4))
            Assert.IsFalse(wash.FillContains(Centre(word.Ink())), $"the wash reached across {Written(Tune, word)}");
    });

    [TestMethod]
    public void ADragUpFromTheWordsAndAcrossTakesNoChord() => UiThread.Run(() =>
    {
        // Reported from the app, on a hymn with a chord over every note: a drag from a word in the third verse up
        // to the notes and back across two columns took the chords over the last two. Each row of a block ran from
        // its first piece to its last, and in a tune a chord symbol is written between two notes.
        const string Tune = "X:1\nM:3/4\nL:1/8\nK:F\n\"Bb\"B2\"F/C\"A2\"C7\"G2| \"F\"F6|]\n"
                          + "w:en-gran-de-cer.\nw:no co-ra-cao.\nw:o co-ra-cao.\n";
        var layout = AbcBuilder.Build(Tune, 900, Brushes.Black, 1.0);
        var note = At(layout, Tune, "B2");

        var chosen = ContentSelection.Between(layout.Root, At(layout, Tune, "ra", after: "w:o"), note);

        Assert.IsTrue(Taken(chosen, note), "the note the drag ended on");
        foreach (var chord in Of(layout, "chord"))
            Assert.IsFalse(Taken(chosen, chord), $"a block below the chords took {Written(Tune, chord)}");
    });

    [TestMethod]
    public void TheWashRunsOnUnderASlur() => UiThread.Run(() =>
    {
        // Reported from the app: the wash broke wherever a slur or a tie crossed the gap between two notes. Nobody
        // typed the arc, so it is named by the group it is drawn in, and a group only partly taken read as something
        // unchosen lying in the gap — when it is around the notes, not between them.
        const string Tune = "X:1\nM:3/4\nL:1/8\nK:F\n\"F\"c2A2F2| (\"Bb\"G2\"F/C\"F2)\"C\"E2| \"F\"F4z2|]\n";
        var layout = AbcBuilder.Build(Tune, 900, Brushes.Black, 1.0);
        var (g, f) = (At(layout, Tune, "G2"), At(layout, Tune, "F2", after: "G2"));

        var wash = Washed(layout, ContentSelection.Between(layout.Root, At(layout, Tune, "c2"), f));
        Assert.IsTrue(wash.FillContains(Gap(g.Ink(), f.Ink(), across: true)), "the gap the slur crosses is washed");
    });

    [TestMethod]
    public void TheWashOverANoteCoversTheStaffItStandsOn() => UiThread.Run(() =>
    {
        // Reported from the app: washing only what a note drew left the top line of its staff showing over a low
        // note, which read as a gap in what was selected.
        const string Tune = "X:1\nL:1/4\nK:C\nE F G A |\n";
        var layout = AbcBuilder.Build(Tune, 900, Brushes.Black, 1.0);
        var notes = Of(layout, "note");
        var staff = notes[0].Bounds;   // a note reserves exactly the staff it stands on
        var ink = notes[0].Ink();

        Assert.IsTrue(ink.Top > staff.Top + 2, "a note on the bottom line does not reach the top line by itself");

        var wash = Washed(layout, ContentSelection.Between(layout.Root, notes[0], notes[1]));
        Assert.IsTrue(wash.FillContains(new System.Windows.Point(ink.X + (ink.Width / 2), staff.Top + 0.5)),
                      "the top line of the staff over it is washed");
    });

    [TestMethod]
    public void AWashOverABlockOfVersesIsOneShape() => UiThread.Run(() =>
    {
        // Reported from the app, on the same hymn: the block from the third verse up to the notes washed each column
        // on its own. The verses are set as tightly as their letters allow, so the wash's pad reached into the verse
        // below — which read as an unchosen word lying between two chosen ones — and a note whose stem reaches down
        // to its words became one patch with them, which joined the words beside it and never the note.
        const string Tune = "X:1\nM:3/4\nL:1/8\nK:F\n\"Bb\"B2\"F/C\"A2\"C7\"G2| \"F\"F6|]\n"
                          + "w:en-gran-de-cer.\nw:no co-ra-cao.\nw:o co-ra-cao.\nw:Con-ti-goa-i.\n";
        var layout = AbcBuilder.Build(Tune, 900, Brushes.Black, 1.0);
        var (b, a, en, gran) = (At(layout, Tune, "B2"), At(layout, Tune, "A2"), At(layout, Tune, "en"), At(layout, Tune, "gran"));

        var wash = Washed(layout, ContentSelection.Between(layout.Root, At(layout, Tune, "ra", after: "w:o"), b));

        Assert.IsTrue(wash.FillContains(Gap(en.Ink(), gran.Ink(), across: true)), "between two words of the first verse");
        Assert.IsTrue(wash.FillContains(Gap(b.Ink(), a.Ink(), across: true)), "between the first two notes");
        Assert.IsTrue(wash.FillContains(Gap(a.Ink(), gran.Ink(), across: false)), "between a note and the word sung on it");
        Assert.IsFalse(wash.FillContains(Centre(At(layout, Tune, "ti").Ink())), "the fourth verse was not taken");
    });

    /// <summary>The deepest piece naming the first <paramref name="written"/> after <paramref name="after"/>.</summary>
    private static Piece At(Laid layout, string tune, string written, string after = "")
    {
        var offset = tune.IndexOf(written, tune.IndexOf(after, StringComparison.Ordinal), StringComparison.Ordinal);
        return layout.Root.SelfAndDescendants().Last(p => p.Stands() && p.Sits().Start <= offset && offset < p.Sits().End);
    }

    private static List<Piece> Of(Laid layout, string kind) =>
        [.. layout.Root.SelfAndDescendants().Where(n => Kind(n) == kind)];

    private static bool Taken(ContentSelection chosen, Piece piece) =>
        chosen.Ranges.Any(r => piece.Sits().Start >= r.Start && piece.Sits().End <= r.Start + r.Length);

    private static Geometry Washed(Laid layout, ContentSelection chosen) =>
        layout.Root.Wash([.. chosen.Ranges.Select(r => new EditRange(r.Start, r.Length))], 1.5);

    private static string Written(string tune, Piece piece) => $"'{tune.Substring(piece.Sits().Start, piece.Sits().Length)}'";

    /// <summary>A point in the space between two things — side by side, or one over the other.</summary>
    private static System.Windows.Point Gap(System.Windows.Rect one, System.Windows.Rect other, bool across) =>
        across
            ? new((one.Right + other.Left) / 2, (Math.Max(one.Top, other.Top) + Math.Min(one.Bottom, other.Bottom)) / 2)
            : new((Math.Max(one.Left, other.Left) + Math.Min(one.Right, other.Right)) / 2, (one.Bottom + other.Top) / 2);

    private static System.Windows.Point Centre(System.Windows.Rect box) => new(box.X + (box.Width / 2), box.Y + (box.Height / 2));

    private static System.Windows.Point Middle(Piece node) =>
        new(node.Bounds.X + (node.Bounds.Width / 2), node.Bounds.Y + (node.Bounds.Height / 2));

    private static string Kind(Piece node) => node.Exists ? node.Kind : "?";
}
