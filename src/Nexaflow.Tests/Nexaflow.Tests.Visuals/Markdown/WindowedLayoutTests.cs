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
using Nexaflow.Markdown.Code;

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
    public void Code_LaidAStretchAtATime_IsTheCodeLaidWhole()
    {
        var code = MarkdownLayoutBench.GeneratedCode(48 * 1024);

        foreach (var (text, said) in new[] { (code, "code"), (code.Replace("\n", "\r\n"), "code with returns"), (code.TrimEnd('\n'), "code ending mid-line") })
            foreach (var length in Lengths(text))
                Same(Whole(text, NoGrammar), Stretched(text, NoGrammar, length), $"{said} at {length}");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void StringsAndCommentsRunningAcrossStretches_AreColouredAsInTheCodeLaidWhole()
    {
        // Code is read and laid a stretch at a time, cut where a line ends — which falls inside what runs over lines: a verbatim or raw
        // string, a template literal, a triple-quoted string, a block comment, script and style written in a page. What each part is,
        // is found in the whole of the code's syntax, so cut anywhere it must colour exactly as the code laid whole does.
        var csharp = Repeated(at => $"class C{at}\n{{\n    string s = @\"one\n two \"\"quoted\"\"\n three\";\n    string r = \"\"\"\n        raw one\n        raw two\n        \"\"\";\n    /* block\n       comment */\n    int B() => {at};\n}}\n");
        var javascript = Repeated(at => $"const t{at} = `one\n${{x}} two\nthree`;\n/* block\n   comment */\nconst re = /a+b/g; // {at}\nfunction f{at}() {{\n  return 'one';\n}}\n");
        var python = Repeated(at => $"def f{at}():\n    \"\"\"A doc\n    over lines\n    \"\"\"\n    s = '''one\ntwo'''\n    return {at}  # done\n");
        var html = Repeated(at => $"<div class=\"c{at}\">\n<script>\nconst a = `one\ntwo`;\n/* block\n comment */\n</script>\n<style>\n.c{at} {{\n  color: red;\n}}\n</style>\n</div>\n");

        foreach (var (named, text) in new[] { ("csharp", csharp), ("javascript", javascript), ("python", python), ("html", html) })
        {
            // Coloured at all — a page's comments are its script's, so a page's are coloured only where what it embeds is read.
            var whole = Whole(text, named);
            var kinds = whole.Root.SelfAndDescendants().Select(piece => piece.Kind).ToHashSet();
            Assert.IsTrue(kinds.Any(kind => kind.StartsWith("comment", StringComparison.Ordinal)) && kinds.Any(kind => kind.StartsWith("string", StringComparison.Ordinal)),
                          $"{named} was never coloured: {string.Join(", ", kinds)}");

            foreach (var length in new[] { 40, 150, 700 })
                Same(whole, Stretched(text, named, length), $"{named} at {length}");
        }

        static string Repeated(Func<int, string> one) => string.Concat(Enumerable.Range(0, 24).Select(one));
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void CodeWrittenIn_IsLaidAsTheCodeItBecameLaidWhole()
    {
        var text = MarkdownLayoutBench.GeneratedCode(24 * 1024);
        var engine = Shown(text, NoGrammar, new ContentEngine { WindowedFrom = 0, StretchLength = 1024 });
        ToTheEnd(engine);

        var lines = Enumerable.Range(0, text.Length).Where(at => at == 0 || text[at - 1] == '\n').ToList();

        foreach (var at in lines.Where((_, index) => index % 37 == 0))
            foreach (var (start, length, written) in new[] { (at, 0, "x"), (at, 0, "\n"), (Math.Max(at - 1, 0), 1, "") })
            {
                var after = engine.State.Document.Apply(ContentChange.Write(start, length, written));

                engine.Apply(engine.State with { Document = after, Caret = 0, Selected = null }, notify: false);
                Same(Whole(after.ToString(), NoGrammar), ToTheEnd(engine), $"{written.Replace("\n", "\\n")} written over {length} at {start}");

                engine.Apply(engine.State with { Document = after.Apply(after.Inverse!), Caret = 0, Selected = null }, notify: false);
            }
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void AKeystrokeInCode_LaysTheLineItWasTypedOn_AndSetsDownEveryOtherAsItWasLaid()
    {
        var text = MarkdownLayoutBench.GeneratedCode(8 * 1024);
        var engine = Shown(text, NoGrammar, new ContentEngine());
        var before = Lines(engine.Laid);

        var at = text.IndexOf('\n', text.Length / 2) + 1;
        engine.Apply(engine.State with { Document = engine.State.Document.Apply(ContentChange.Write(at, 0, "x")), Caret = at + 1, Selected = null }, notify: false);
        var after = Lines(engine.Laid);

        Assert.AreEqual(before.Count, after.Count, "Lines came or went.");
        Assert.AreEqual(1, after.Count(line => !before.Contains(line)), "Not just the line typed on was laid again.");

        // A line is put down as the tree it was laid as, wherever it now stands: every line but the one typed on is the same tree.
        static List<LayoutTree> Lines(Laid laid) => [.. Enumerable.Range(1, laid.Tree.Count - 1).Select(at => laid.Tree.MountOf(at)?.Tree).OfType<LayoutTree>()];
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ColouredCodeWrittenIn_IsColouredAsTheCodeItBecameLaidWhole()
    {
        // A comment opened, a string opened and each closed again, far from most of what they colour: every unit the change reaches
        // in the syntax is coloured again, not only the one written in.
        var text = MarkdownLayoutBench.GeneratedCode(16 * 1024);
        var engine = Shown(text, "csharp", new ContentEngine { WindowedFrom = 0, StretchLength = 1024 });
        ToTheEnd(engine);

        var middle = text.IndexOf('\n', text.Length / 3) + 1;
        var later = text.IndexOf('\n', 2 * text.Length / 3) + 1;

        foreach (var (at, written) in new[] { (middle, "/*"), (later, "*/"), (middle, "\""), (later, "\"") })
        {
            var after = engine.State.Document.Apply(ContentChange.Write(at, 0, written));
            engine.Apply(engine.State with { Document = after, Caret = 0, Selected = null }, notify: false);

            Same(Whole(after.ToString(), "csharp"), ToTheEnd(engine), $"{written} written at {at}");
        }
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void LongCodeWrittenIn_KeepsItsColours_UntilTypingPauses()
    {
        var text = MarkdownLayoutBench.GeneratedCode(200 * 1024);
        var engine = Shown(text, "csharp", new ContentEngine());
        Settled(engine);

        var at = text.IndexOf('\n', 2000) + 1;
        engine.Apply(engine.State.MoveCaretTo(at), notify: false);
        engine.Input(new ContentText("x"));

        // Laid by the keystroke itself, before anything parsed since is taken in: what was written over is uncoloured, and the rest of
        // its unit is not.
        Assert.IsTrue(Called(engine.Laid).Count(kind => kind != Kinds.Verbatim && kind != CodeKinds.Code) > 100, "The unit written in lost its colours while it waited.");

        // Once it has paused, every piece laid is what the same piece of the code laid whole is.
        Settled(engine);

        var whole = Whole(engine.State.Document.ToString(), "csharp").Root.SelfAndDescendants()
                        .Where(piece => piece.Words is not null && piece.Part is not null)
    .GroupBy(piece => Span(piece.Part))
                        .ToDictionary(same => same.Key, same => same.Last().Kind);

        foreach (var piece in engine.Laid.Root.SelfAndDescendants())
            if (piece.Words is not null && piece.Part is { } part)
                Assert.AreEqual(whole[Span(part)], piece.Kind, $"what {Span(part)} is coloured as once typing paused");

        static List<string> Called(Laid laid) => [.. laid.Root.SelfAndDescendants().Select(piece => piece.Kind)];
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void LongCode_StandsAsTallAsItIs_BeforeAnyMoreOfItIsLaid()
    {
        var text = MarkdownLayoutBench.GeneratedCode(400 * 1024);
        var engine = Shown(text, NoGrammar, new ContentEngine());

        Assert.IsTrue(engine.ReadingRest.Wait(Patience), "The rest was never read.");
        engine.Relay();

        Assert.IsTrue(engine.Unlaid, "All of it was laid.");
        Assert.AreEqual(Whole(text, NoGrammar).Size.Height, engine.Laid.Size.Height, 1e-6, "Code's lines say how tall it is, laid or not.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void LongCode_LaysOnlyTheLinesTheScreenReaches_OfAUnitThousandsOfLinesLong()
    {
        var text = MarkdownLayoutBench.GeneratedCode(400 * 1024);
        var engine = Shown(text, NoGrammar, new ContentEngine());

        Assert.IsTrue(engine.ReadingRest.Wait(Patience), "The rest was never read.");
        engine.Relay();

        // A unit is a stretch of 32 KB, a thousand lines and more; a screen and a screen either side of it is a few hundred.
        var laid = CodeLines(engine.Laid).Count;
        Assert.IsTrue(laid > 0 && laid < 400, $"Laid {laid} lines for the first screen.");
        Assert.AreEqual(Whole(text, NoGrammar).Size.Height, engine.Laid.Size.Height, 1e-6, "Laid in part, it stands as tall as all of it.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ScrollingWithinAUnitOfCode_LaysWhatComesOnScreen_AndSetsDownEveryLineLaidAsItWas()
    {
        var text = MarkdownLayoutBench.GeneratedCode(400 * 1024);
    var engine = Shown(text, NoGrammar, new ContentEngine());

        // Further down the first unit, past every line laid for the first screen.
        var line = text.IndexOf('\n', 24 * 1024) + 1;
        Assert.IsNotNull(engine.UnlaidTop(line), "It was laid already.");

        var top = engine.Laid.Size.Height * line / text.Length;
        engine.OnScreen = new LayoutRect(0, top - 200, Room, 400);
        for (var pass = 0; pass < 20; pass++) engine.Relay();

        Assert.IsNull(engine.UnlaidTop(line), "What came on screen was never laid.");

        var before = CodeLines(engine.Laid);
        Assert.IsTrue(before.Count < 400, $"Laid {before.Count} lines for a screen and a screen either side.");

        // Half a screen further: every line laid that the screen still reaches is set down as it was laid.
        engine.OnScreen = new LayoutRect(0, top, Room, 400);
        for (var pass = 0; pass < 20; pass++) engine.Relay();

        var after = CodeLines(engine.Laid);
        Assert.IsTrue(after.Count(laid => before.Contains(laid)) > before.Count * 3 / 4, "The lines laid before that the screen still reaches were laid again.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ACaretSentToALineOfAUnitNotLaidThere_IsLaidThere_AndThenBroughtOntoThePage()
    {
        var text = MarkdownLayoutBench.GeneratedCode(400 * 1024);
        var engine = Shown(text, NoGrammar, new ContentEngine());
        var brought = new List<LayoutRect>();
        engine.Revealing += (_, at) => brought.Add(at);

        // In the first unit, which is laid, on a line it did not lay.
        var far = text.IndexOf('\n', 24 * 1024) + 1;
        Assert.IsNotNull(engine.UnlaidTop(far), "It was laid already.");

        engine.Apply(engine.State.MoveCaretTo(far), notify: false);
        for (var pass = 0; pass < 100 && brought.Count == 0; pass++) engine.Relay();

        Assert.AreEqual(1, brought.Count, "It was never brought onto the page.");
        Assert.AreEqual(engine.Laid.Root.CaretRect(far), brought[0]);
    }

    /// <summary>The trees every line of code was laid as, as <paramref name="laid"/> puts them down, at any depth.</summary>
    private static List<LayoutTree> CodeLines(Laid laid)
    {
        var lines = new List<LayoutTree>();
        Gathered(laid.Tree);

        return lines;

        void Gathered(LayoutTree tree)
        {
            foreach (var (_, mount) in tree.Mounts)
            {
                if (mount.Tree.Root.Kind == CodeKinds.Line) lines.Add(mount.Tree);
                else Gathered(mount.Tree);
            }
        }
    }

    /// <summary>Code in a language no grammar reads: drawn as written, so no slower reading lands to colour it.</summary>
    private const string NoGrammar = "no-grammar-code";

    /// <summary>Markdown, under a word a fence names to have what it holds read as markdown rather than shown as written.</summary>
    private const string WindowedProse = "windowed-prose";

    static WindowedLayoutTests() => ContentLanguages.Register(ContentLanguages.Markdown with { Reads = static word => word == WindowedProse });

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
        // Until a round lands nothing: what landed is laid, and laying it may ask for more — the units a parse changed — which lands in turn.
        for (var round = 0; round < 16; round++)
        {
            for (var pass = 0; pass < 1000 && engine.Unlaid; pass++) engine.Relay();

            Assert.IsTrue(SpinWait.SpinUntil(() => !engine.Rereading, Patience), "A slower reading never landed.");
            if (!engine.MarkLanded()) return;

            engine.Relay();
        }

        Assert.Fail("Slower readings went on landing.");
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

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ALongFence_IsLaidOnlyWhereTheReaderIs_StandingAsTallAsAllOfIt()
    {
        var text = Fence(512 * 1024);
        var engine = Shown(text, null, new ContentEngine());
        var whole = Whole(text, null);

        var (laid, all) = (Lines(engine.Laid), Lines(whole));
        Assert.IsTrue(laid > 0 && laid < all / 4, $"Laid {laid} of the fence's {all} lines for one screen.");
        Assert.AreEqual(whole.Size.Height, engine.Laid.Size.Height, 1e-6, "A fence of code stands as tall as its lines say, laid or not.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void AKeystrokeInALongFence_LaysTheUnitOfItTypedIn_AndSetsDownEveryOtherAsItWasLaid()
    {
        var text = Fence(512 * 1024);
        var engine = Shown(text, null, new ContentEngine());
        var at = text.IndexOf('\n', 2000) + 1;

        // The caret in the fence first, so what it wants laid round it is laid before the keystroke as after.
        engine.Apply(engine.State.MoveCaretTo(at), notify: false);
        engine.Relay();
        var before = CodeUnits(engine.Laid);

        engine.Apply(engine.State with { Document = engine.State.Document.Apply(ContentChange.Write(at, 0, "x")), Caret = at + 1, Selected = null }, notify: false);
        var after = CodeUnits(engine.Laid);

        // The fence's window is found again from the body as the keystroke left it, so every unit of it laid before is still laid as it was.
        Assert.AreEqual(before.Count, after.Count, "Units of the fence came or went.");
        Assert.AreEqual(1, after.Count(unit => !before.Contains(unit)), "Not just the unit typed in was laid again.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void ACaretSentIntoTheMiddleOfALongFence_IsLaidThere_AndThenBroughtOntoThePage()
    {
        var text = Fence(512 * 1024);
        var engine = Shown(text, null, new ContentEngine());
        var brought = new List<LayoutRect>();
        engine.Revealing += (_, at) => brought.Add(at);

        var far = text.IndexOf('\n', text.Length / 2) + 1;
        engine.Apply(engine.State.MoveCaretTo(far), notify: false);
        for (var pass = 0; pass < 1000 && brought.Count == 0; pass++) engine.Relay();

        Assert.AreEqual(1, brought.Count, "It was never brought onto the page.");
        Assert.AreEqual(engine.Laid.Root.CaretRect(far), brought[0]);
        Assert.IsTrue(Lines(engine.Laid) < Lines(Whole(text, null)) / 2, "The fence was laid from its start to there.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void TheReaderScrollingIntoALongFence_LaysWhatComesOnScreen_AndNothingBetween()
    {
        var text = Fence(512 * 1024);
        var engine = Shown(text, null, new ContentEngine());
        var middle = text.IndexOf('\n', text.Length / 2) + 1;

        // Where the middle of the fence stands, going by the lines above it, which say how tall they are laid or not.
        var top = engine.Laid.Size.Height * middle / text.Length;
        engine.OnScreen = new LayoutRect(0, top - 200, Room, 400);
        for (var pass = 0; pass < 20; pass++) engine.Relay();

        Assert.IsFalse(engine.Laid.Root.CaretRect(middle).IsEmpty, "What came on screen was never laid.");
        Assert.IsTrue(Lines(engine.Laid) < Lines(Whole(text, null)) / 2, "The fence was laid from its start to there.");
    }

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void WhatTheReaderIsLookingAt_InsideALongFence_StaysStill_AsWhatStandsAboveItInTheFenceIsLaid()
    {
        var text = $"# Top\n\nSome words first.\n\n````{WindowedProse}\n" + Paragraphs(4000) + "````\n";
        var engine = Shown(text, null, new ContentEngine { StretchLength = 1024 });
        var far = text.IndexOf("Paragraph 3501 ", StringComparison.Ordinal);

        Assert.IsTrue(engine.Uncover(far), "It was laid already.");
        for (var pass = 0; pass < 100 && engine.Laid.Root.CaretRect(far + 1).IsEmpty; pass++) engine.Relay();

        var at = engine.Laid.Root.CaretRect(far + 1);
        Assert.IsFalse(at.IsEmpty, "It was never laid.");

        var shifted = 0.0;
        engine.Shifted += (_, by) => shifted += by;

        // The reader looks at it, so what stands a screen above it inside the fence, estimated so far, is laid.
        engine.OnScreen = new LayoutRect(0, at.Y - 10, Room, 400);
        for (var pass = 0; pass < 20; pass++) engine.Relay();

        Assert.AreNotEqual(0, shifted, "What stands above it came to the height it was estimated at, to the hair.");
        Assert.AreEqual(at.Y + shifted, engine.Laid.Root.CaretRect(far + 1).Y, 1e-6, "What the reader was looking at moved by other than the page was told.");
    }

    /// <summary>A document that is one fence of C#, <paramref name="size"/> characters of it.</summary>
    private static string Fence(int size) => $"```csharp\n{MarkdownLayoutBench.GeneratedCode(size)}```\n";

    /// <summary>The trees every unit of code was laid as, as <paramref name="laid"/> puts them down, at any depth.</summary>
    private static List<LayoutTree> CodeUnits(Laid laid)
    {
        var units = new List<LayoutTree>();
        Gathered(laid.Tree);

        return units;

        void Gathered(LayoutTree tree)
        {
            foreach (var (_, mount) in tree.Mounts)
            {
                if (mount.Tree.Root.Kind == CodeKinds.Code) units.Add(mount.Tree);
                else if (mount.Tree.Root.Kind != CodeKinds.Line) Gathered(mount.Tree);
            }
        }
    }

    /// <summary>How many lines of code <paramref name="laid"/> laid, at any depth.</summary>
    private static int Lines(Laid laid)
    {
        var lines = 0;
        Counted(laid.Tree);

        return lines;

        void Counted(LayoutTree tree)
        {
            foreach (var (_, mount) in tree.Mounts)
            {
                if (mount.Tree.Root.Kind == CodeKinds.Line) lines++;
                else Counted(mount.Tree);
            }
        }
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
            engine.MarkLanded();
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

        // Every slower reading it asked for landed and laid — what the same content laid whole is compared with.
        Settled(engine);

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
