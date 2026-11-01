using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.Rtf;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A long document is read and laid a stretch at a time, as far as the reader has looked (<see cref="ContentEngine"/>) — and
/// laid on to its end, it must come out exactly as the same document laid whole: every piece the same, in the same place,
/// standing for the same characters, wherever the stretches happened to stop.
/// </summary>
[TestClass]
public class WindowedLayoutTests
{
    private const double Room = 640;

    private static readonly LayoutStyle Style = WpfStyles.Dark;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long the stretches <paramref name="text"/> is laid in are: a few blocks long and a handful of paragraphs long — or, for
    /// a long sample, about thirty stretches and about eight. Every stretch taken in lays the reading again, so a long sample in
    /// stretches of a few blocks is hundreds of layings out, where thirty seams test as much.
    /// </summary>
    private static int[] Lengths(string text) => text.Length < 16 * 1024 ? [64, 1024] : [text.Length / 32, text.Length / 8];

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void EveryMarkdownSample_LaidAStretchAtATime_IsTheSampleLaidWhole()
    {
        var documents = TestSampleData.Files("markdown").ToList();
        documents.Add(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        foreach (var path in documents)
        {
            var text = File.ReadAllText(path);

            foreach (var length in Lengths(text))
                Same(Whole(text, null), Stretched(text, null, length), $"{Path.GetFileName(path)} at {length}");
        }
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void EveryRtfSample_LaidAStretchAtATime_IsTheSampleLaidWhole()
    {
        foreach (var path in TestSampleData.Files("rtf"))
        {
            var text = File.ReadAllText(path, Encoding.Latin1);

            foreach (var length in Lengths(text))
                Same(Whole(text, RtfParser.Language), Stretched(text, RtfParser.Language, length), $"{Path.GetFileName(path)} at {length}");
        }
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ALongDocument_IsLaidOnlyAsFarAsTheReaderHasLooked_OnAPageAsLongAsAllOfIt()
    {
        var text = Paragraphs(4000);
        var engine = Shown(text, null, new ContentEngine());
        var whole = Whole(text, null);

        Assert.IsTrue(engine.Unlaid, "All of it was laid.");
        Assert.IsTrue(engine.Laid.Tree.Count < whole.Tree.Count / 10,
                      $"Laid {engine.Laid.Tree.Count} pieces of {whole.Tree.Count} for one screen.");

        // The page is about as long as the whole document, going by what the laid part came to.
        Assert.AreEqual(whole.Size.Height, engine.Laid.Size.Height, whole.Size.Height * 0.1);
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void TheReaderComingNearWhatIsNotLaid_AsksForMore_AndMoreIsLaid()
    {
        var engine = Shown(Paragraphs(4000), null, new ContentEngine());
        var grew = 0;
        engine.Grew += (_, _) => Interlocked.Increment(ref grew);

        var laid = engine.Laid.Root.SelfAndDescendants().Count();
        var bottom = engine.Laid.Root.Children.Max(piece => piece.Bounds.Bottom);

        engine.OnScreen = new LayoutRect(0, bottom - 200, Room, 400);

        Assert.IsTrue(grew > 0, "Nothing said there was more to lay.");

        engine.Relay();
        Assert.IsTrue(engine.Laid.Root.SelfAndDescendants().Count() > laid, "Laying again laid nothing more.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ACaretSentToTheEnd_IsLaidThere_AndNothingBetween()
    {
        var text = Paragraphs(4000);
        var engine = Shown(text, null, new ContentEngine());

        engine.Apply(engine.State.MoveCaretTo(text.Length), notify: false);
        for (var pass = 0; pass < 10_000 && engine.UnlaidTop(text.Length) is not null; pass++) engine.Relay();

        Assert.IsNull(engine.UnlaidTop(text.Length), "The end was never laid.");
        Assert.IsFalse(engine.Laid.Root.CaretRect(text.Length).IsEmpty, "The end has nowhere to put the caret.");
        Assert.IsTrue(engine.Unlaid, "Everything before the end was laid too.");
        Assert.IsTrue(engine.Laid.Tree.Count < Whole(text, null).Tree.Count / 2, $"Laid {engine.Laid.Tree.Count} pieces for the first screen and the end.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void WhatTheReaderIsLookingAt_StaysStill_AsWhatStandsAboveItIsLaid()
    {
        var text = Paragraphs(4000);
        var engine = Shown(text, null, new ContentEngine { StretchLength = 1024 });
        var far = text.IndexOf("Paragraph 3501 ", StringComparison.Ordinal);

        Assert.IsTrue(engine.Uncover(far), "It was laid already.");
        for (var pass = 0; pass < 10_000 && engine.UnlaidTop(far) is not null; pass++) engine.Relay();

        // The first words of the unit laid there, nothing above it laid yet.
        var start = engine.Readings.Last(unit => unit.Start <= far).Start;
        var inside = text.IndexOf("Paragraph ", start, StringComparison.Ordinal) + 1;
        var at = engine.Laid.Root.CaretRect(inside);

        var shifted = 0.0;
        engine.Shifted += (_, by) => shifted += by;

        // The reader looks at them, so what stands a screen above them, estimated so far, is laid.
        engine.OnScreen = new LayoutRect(0, at.Y - 10, Room, 400);
        for (var pass = 0; pass < 20; pass++) engine.Relay();

        Assert.AreNotEqual(0, shifted, "What stands above it came to the height it was estimated at, to the hair.");
        Assert.AreEqual(at.Y + shifted, engine.Laid.Root.CaretRect(inside).Y, 1e-6, "What the reader was looking at moved by other than the page was told.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    [CoversNode("markdown-outline")]
    public void TheOutlineOfALongDocument_HoldsEveryHeading_WithOnlyTheFirstScreenLaid()
    {
        var text = Paragraphs(4000);
        var engine = Shown(text, null, new ContentEngine());

        for (var round = 0; round < 3; round++)
        {
            Assert.IsTrue(engine.ReadingRest.Wait(Patience), "The rest was never read.");
            engine.Relay();
        }

        Assert.IsTrue(engine.Unlaid, "All of it was laid.");
        CollectionAssert.AreEqual(Enumerable.Range(0, 80).Select(at => $"Part {at}").ToList(), engine.Headings.Select(heading => heading.Title).ToList());
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ASlowerReadingLanding_LaysAgainOnlyTheUnitThatAskedForIt()
    {
        var engine = new ContentEngine { WindowedFrom = 0, StretchLength = 512 };
        Shown(Paragraphs(60), null, engine);
        engine.OnScreen = new LayoutRect(0, 0, Room, 1e12);
        Settled(engine);

        // A fence written in the middle, whose reading nothing has asked for: the one slower reading still to land.
        var middle = engine.State.Document.ToString().IndexOf("Paragraph 30 ", StringComparison.Ordinal);
        var fence = ContentChange.Write(middle, 0, $"```csharp\n// {Guid.NewGuid():N}\nclass A {{ int B() => 1; }}\n```\n\n");
        engine.Apply(engine.State with { Document = engine.State.Document.Apply(fence), Caret = 0, Selected = null }, notify: false);
        for (var pass = 0; pass < 1000 && engine.Unlaid; pass++) engine.Relay();

        Assert.IsTrue(SpinWait.SpinUntil(() => !engine.Rereading, Patience), "The fence's reading never landed.");

        var before = Trees(engine.Laid);
        engine.RelayLanded();
        var after = Trees(engine.Laid);

        Assert.AreEqual(before.Count, after.Count, "Units were laid or let go.");
        Assert.AreEqual(1, after.Count(tree => !before.Contains(tree)), "Not just the unit holding the fence was laid again.");
        Assert.IsTrue(engine.Laid.Root.SelfAndDescendants().Any(piece => piece.Kind == "keyword"), "The fence is not drawn as its reading read it.");

        static List<LayoutTree> Trees(Laid laid) => [.. laid.Root.Children.Select(piece => piece.Tree!).Distinct()];
    }

    /// <summary><paramref name="engine"/> with all it is to lay laid, and every slower reading it asked for landed and laid.</summary>
    private static void Settled(ContentEngine engine)
    {
        for (var round = 0; round < 8; round++)
        {
            for (var pass = 0; pass < 1000 && engine.Unlaid; pass++) engine.Relay();

            Assert.IsTrue(SpinWait.SpinUntil(() => !engine.Rereading, Patience), "A slower reading never landed.");
            engine.RelayLanded();

            if (!engine.Rereading && !engine.Unlaid) return;
        }
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ALongRtfDocument_OpensAStretchAtATime_AndLaidToItsEndIsItLaidWhole()
    {
        var text = RtfParagraphs(3000);
        var engine = Shown(text, RtfParser.Language, new ContentEngine());

        Assert.IsTrue(engine.Unlaid, "All of it was laid.");

        Same(Whole(text, RtfParser.Language), ToTheEnd(engine), "a long RTF document");
    }

    // ── Laying ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ALongDocumentWrittenIn_IsLaidAsTheDocumentItBecameLaidWhole()
    {
        var path = Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md"));
        var text = File.ReadAllText(path);
        var engine = new ContentEngine { WindowedFrom = 0, StretchLength = 1024 };
        Shown(text, null, engine);
        ToTheEnd(engine);

        var middle = text.IndexOf("\n\n", text.Length / 2, StringComparison.Ordinal) + 2;

        string[] edits = [text.Insert(middle, "Typed in the middle. "), "Opening words.\n\n" + text.Insert(middle, "Typed in the middle. "), text];

        foreach (var edit in edits)
        {
            engine.Apply(engine.State with { Document = ContentDocument.Of(edit), Caret = Math.Min(engine.State.Caret, edit.Length), Selected = null }, notify: false);

            Same(Whole(edit, null), ToTheEnd(engine), $"written in, {edit.Length} characters");
        }
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ReadingsNestedThreeDeep_WrittenInAtEveryLineOfEveryLevel_AreLaidAsTheWrittenDocumentLaidWhole()
    {
        foreach (var quoted in new[] { false, true })
        {
            var text = NestedSamples.ThreeDeep(quoted);
            var name = quoted ? "three deep, in quotes" : "three deep";

            // Every reading at every depth read a few dozen characters at a time: a unit only ever stops where a line starts, so writing
            // at every line, and a character into it, writes at every unit boundary of every level.
            var engine = Shown(text, null, new ContentEngine { WindowedFrom = 0, StretchLength = 64 });
            ToTheEnd(engine);

            var lines = Enumerable.Range(0, text.Length).Where(at => at == 0 || text[at - 1] == '\n').ToList();

            foreach (var at in lines)
                foreach (var (start, length, written) in new[] { (at, 0, "x"), (at + 1, 0, "x"), (at, 0, "\n"), (at, 1, "") })
                {
                    if (start + length > text.Length) continue;

                    var before = engine.State.Document;
                    var after = before.Apply(ContentChange.Write(start, length, written));
                    var said = $"{name}, {(written.Length == 0 ? "a character taken" : written.Replace("\n", "\\n") + " written")} at {start}";

                    engine.Apply(engine.State with { Document = after, Caret = Math.Min(engine.State.Caret, after.Length), Selected = null }, notify: false);
                    Same(Whole(after.ToString(), null), ToTheEnd(engine), said);

                    engine.Apply(engine.State with { Document = after.Apply(after.Inverse!), Caret = 0, Selected = null }, notify: false);
                }

            Same(Whole(text, null), ToTheEnd(engine), $"{name}, every edit taken back");
        }
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ALongRtfDocumentWrittenIn_IsLaidAsTheDocumentItBecameLaidWhole()
    {
        var text = RtfParagraphs(1500);
        var engine = Shown(text, RtfParser.Language, new ContentEngine { StretchLength = 4096 });
        ToTheEnd(engine);

        var middle = text.IndexOf("Paragraph 700 ", StringComparison.Ordinal);
        var edit = text.Insert(middle, @"\b more\b0  ");

        engine.Apply(engine.State with { Document = ContentDocument.Of(edit), Caret = 0, Selected = null }, notify: false);

        Same(Whole(edit, RtfParser.Language), ToTheEnd(engine), "a long RTF document written in");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void AWordInTheLastParagraphOfALongRtfDocument_IsFound_WithOnlyTheFirstScreenLaid()
    {
        var text = RtfParagraphs(3000);
        var engine = Shown(text, RtfParser.Language, new ContentEngine());

        var found = MarkdownFind.Drawn(engine.DrawnWords, "Paragraph 2999");
        var whole = MarkdownFind.Drawn(DrawnText.Of(RtfParser.Parse(text)), "Paragraph 2999");

        Assert.IsTrue(engine.Unlaid, "Looking laid all of it.");
        Assert.AreEqual(1, found.Count);
        CollectionAssert.AreEqual(whole.ToList(), found.ToList());
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ACaretSentPastWhatIsLaid_IsLaidTo_AndThenBroughtOntoThePage()
    {
        var text = Paragraphs(4000);
        var engine = Shown(text, null, new ContentEngine());
        var brought = new List<LayoutRect>();
        engine.Revealing += (_, at) => brought.Add(at);

        var far = text.IndexOf("Paragraph 3501 ", StringComparison.Ordinal);
        Assert.IsTrue(far > 0);
        engine.Apply(engine.State.MoveCaretTo(far), notify: false);
        for (var pass = 0; pass < 1000 && brought.Count == 0; pass++) engine.Relay();

        Assert.AreEqual(1, brought.Count, "It was never brought onto the page.");
        Assert.AreEqual(engine.Laid.Root.CaretRect(far), brought[0]);
        Assert.IsTrue(engine.Unlaid, "Everything past it was laid too.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    [CoversNode("reference-links")]
    public void ALinkDefinedAtTheEndOfALongDocument_IsALinkAtItsTop_BeforeTheEndIsLaid()
    {
        var text = "See [the site][site] first.\n\n" + Paragraphs(4000) + "[site]: https://example.com/site\n";
        var engine = Shown(text, null, new ContentEngine());

        Assert.IsTrue(engine.ReadingRest.Wait(Patience), "The rest was never read.");
        engine.Relay();

        Assert.IsTrue(engine.Unlaid, "All of it was laid.");

        var link = engine.Readings.SelectMany(unit => unit.SelfAndDescendants()).FirstOrDefault(part => part.Kind == Kinds.Link && part.Print() == "[the site][site]");
        Assert.IsNotNull(link, "What the end defines never reached the top.");
        Assert.AreEqual("https://example.com/site", MarkdownLinks.Goes(link!.Node));
    }

    /// <summary><paramref name="text"/> laid whole, with every slower reading of it landed.</summary>
    private static Laid Whole(string text, string? named)
    {
        var engine = new ContentEngine();
        using var landed = new ManualResetEventSlim();
        engine.Reread += (_, _) => landed.Set();

        var laid = engine.Lay(named, EditState.For(text), Style, Room, false);

        for (var settling = 0; settling < 8 && (engine.Rereading || landed.IsSet); settling++)
        {
            if (engine.Rereading) landed.Wait(Patience);

            landed.Reset();
            engine.Forget();
            laid = engine.Lay(named, EditState.For(text), Style, Room, false);
        }

        return laid;
    }

    /// <summary><paramref name="text"/> shown a stretch at a time, however short — stretches about <paramref name="length"/> long — and laid to its end.</summary>
    private static Laid Stretched(string text, string? named, int length) =>
        ToTheEnd(Shown(text, named, new ContentEngine { WindowedFrom = 0, StretchLength = length }));

    /// <summary><paramref name="engine"/> showing <paramref name="text"/> at the room, as an element showing it does.</summary>
    private static ContentEngine Shown(string text, string? named, ContentEngine engine)
    {
        engine.Show(named, Style);
        engine.Start(text);
        engine.Fit(Room);

        return engine;
    }

    /// <summary>What <paramref name="engine"/> lays with the reader looking at all of it, once the reading of the rest has landed.</summary>
    private static Laid ToTheEnd(ContentEngine engine)
    {
        engine.OnScreen = new LayoutRect(0, 0, Room, 1e12);

        for (var round = 0; round < 2; round++)
        {
            for (var pass = 0; pass < 100_000 && engine.Unlaid; pass++) engine.Relay();

            Assert.IsTrue(engine.ReadingRest.Wait(Patience), "The rest was never read.");
            engine.Relay();
        }

        Assert.IsFalse(engine.Unlaid, "The end was never laid.");

        return engine.Laid;
    }

    // ── Documents ───────────────────────────────────────────────────────────

    private static string Paragraphs(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(at =>
            at % 50 == 0 ? $"## Part {at / 50}\n\n" : $"Paragraph {at} says *something* about [a link](https://example.com/{at}) and goes on a while.\n\n"));

    private static string RtfParagraphs(int count) =>
        @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}{\colortbl;\red200\green0\blue0;}\f0\fs24 " +
        string.Concat(Enumerable.Range(0, count).Select(at => at % 40 == 0
            ? $@"\pard\sb120\sa60\b\fs32 Part {at / 40}\b0\fs24\par "
            : $@"\pard\fi360 Paragraph {at} has \cf1 colour\cf0  and \i slant\i0  in it.\par ")) + "}";

    // ── Comparing ───────────────────────────────────────────────────────────

    /// <summary>
    /// Two layouts of one source agree about every piece — what it is, where, and what it stands for — and about everything
    /// around them. Where is within a hair: a stretch is moved down to where it stands, which adds the same distances in another
    /// order than laying it all at once does.
    /// </summary>
    private static void Same(Laid expected, Laid actual, string what)
    {
        var want = expected.Root.SelfAndDescendants().ToList();
        var got = actual.Root.SelfAndDescendants().ToList();
        Assert.AreEqual(want.Count, got.Count, $"{what}: pieces{Diverged(want, got)} — past them: {string.Join(", ", got.Skip(want.Count).Select(piece => $"{piece.Kind} {Span(piece.Part)} {piece.Bounds.Y:0.#}"))}");

        for (var at = 0; at < want.Count; at++)
        {
            var (a, b) = (want[at], got[at]);

            Assert.AreEqual(a.Kind, b.Kind, $"{what}: piece {at}");
            // Except the line at today, drawn from the clock: two layouts made a moment apart need not agree on the moment.
            if (a.Kind != Nexaflow.Markdown.Mermaid.Gantt.GanttPiece.Today)
                Assert.IsTrue(Near(a.Bounds, b.Bounds), $"{what}: piece {at} ({a.Kind}) stands at {b.Bounds}, not {a.Bounds}");
            Assert.AreEqual(Span(a.Part), Span(b.Part), $"{what}: what piece {at} ({a.Kind}) stands for");
        }

        var (wanted, found) = (expected.Places.ToList(), actual.Places.ToList());
        var differs = Enumerable.Range(0, Math.Min(wanted.Count, found.Count)).FirstOrDefault(at => (wanted[at].Offset, wanted[at].Trailing) != (found[at].Offset, found[at].Trailing), -1);
        if (differs < 0 && wanted.Count != found.Count) differs = Math.Min(wanted.Count, found.Count);

        string Around(List<CaretPlace> places) =>
            string.Join("; ", places.Skip(Math.Max(0, differs - 3)).Take(7).Select(place => $"{place.Offset}{(place.Trailing ? "]" : "[")} {place.Against.Kind}/{place.Against.Depth} x{place.CaretRect().X:0.#}"));

        Assert.AreEqual(-1, differs, $"{what}: caret places part at {differs} of {wanted.Count} and {found.Count} — laid whole: {(differs < 0 ? "" : Around(wanted))} — a stretch at a time: {(differs < 0 ? "" : Around(found))}");

        CollectionAssert.AreEquivalent(expected.Trouble.Select(Said).ToList(), actual.Trouble.Select(Said).ToList(), $"{what}: trouble");

        Assert.IsTrue(Near(new LayoutRect(expected.Size), new LayoutRect(actual.Size)), $"{what}: the size is {actual.Size}, not {expected.Size}");
    }

    private static bool Near(LayoutRect want, LayoutRect got) =>
        want.IsEmpty ? got.IsEmpty
        : !got.IsEmpty && Math.Abs(want.X - got.X) < 1e-6 && Math.Abs(want.Y - got.Y) < 1e-6
          && Math.Abs(want.Width - got.Width) < 1e-6 && Math.Abs(want.Height - got.Height) < 1e-6;

    /// <summary>Where two layouts of the same content stop agreeing, for a count that does not match.</summary>
    private static string Diverged(IReadOnlyList<LayoutPiece> want, IReadOnlyList<LayoutPiece> got)
    {
        for (var at = 0; at < Math.Min(want.Count, got.Count); at++)
        {
            if (want[at].Kind == got[at].Kind && Span(want[at].Part) == Span(got[at].Part)) continue;

            return $"\nfirst differ at piece {at}:"
                 + $"\n  whole  : {want[at].Kind} at {want[at].Bounds} for {Span(want[at].Part)}"
                 + $"\n  windowed: {got[at].Kind} at {got[at].Bounds} for {Span(got[at].Part)}";
        }

        return $"\nthey agree for the first {Math.Min(want.Count, got.Count)} pieces, so one simply has more";
    }

    private static (int, int)? Span(ISourcePart? part) => part is null ? null : (part.Start, part.Length);

    private static string Said(LayoutDiagnostic trouble) => $"{trouble.Start}+{trouble.Length} {trouble.Message} {Span(trouble.Part)}";
}
