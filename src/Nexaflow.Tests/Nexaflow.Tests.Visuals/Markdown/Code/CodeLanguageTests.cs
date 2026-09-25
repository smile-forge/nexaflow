using System;
using System.Linq;
using System.Threading;

using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Markdown.Languages;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Code;

/// <summary>
/// Code in a fence, coloured by the engine that already reads code — and drawn before it has.
///
/// <para>
/// The whole point is the order. A document re-lays on every keystroke and compiling a grammar's query costs
/// about fifteen milliseconds, so colouring is never on the way to drawing: the code appears at once in one
/// colour and colours itself a moment later. These say both halves, and that the second one arrives.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("md-code-highlighting")]
public class CodeLanguageTests
{
    private const string Source = "public class Thing\n{\n    private int _count;\n}\n";

    [TestMethod]
    public void AFenceIsNamedByWhateverAWriterCallsIt()
    {
        Assert.AreEqual("c-sharp", CodeGrammars.For("csharp"));
        Assert.AreEqual("c-sharp", CodeGrammars.For("c#"));
        Assert.AreEqual("python", CodeGrammars.For("py"));
        Assert.AreEqual("javascript", CodeGrammars.For("js"));
        Assert.AreEqual("cpp", CodeGrammars.For("c++"));

        // The extension table already answers most of it, so a word that is one needs no entry of its own.
        Assert.AreEqual("rust", CodeGrammars.For("rs"));

        Assert.IsNull(CodeGrammars.For("nothing-reads-this"));
        Assert.IsNull(CodeGrammars.For(null));
    }

    [TestMethod]
    public void TheTableReadsCodeOnlyAfterEveryLanguageOfItsOwn()
    {
        Assert.AreSame(ContentLanguages.Code, ContentLanguages.For("csharp"));
        Assert.AreSame(ContentLanguages.Code, ContentLanguages.For("python"));

        // A word a language of its own claims must reach that one, however many words code answers to.
        Assert.AreNotSame(ContentLanguages.Code, ContentLanguages.For("mermaid"));
        Assert.AreNotSame(ContentLanguages.Code, ContentLanguages.For("qr"));
    }

    [TestMethod]
    public void AndColouringCodeIsStillShowingIt()
    {
        // Everything else in the table draws a picture of what its source meant; code draws the source. A
        // reader searching a page finds the words in a code fence and not the words inside a chart, and the
        // help index goes looking for exactly this — it dropped every code fence the day code joined the table.
        Assert.IsTrue(ContentLanguages.For("csharp")!.Editing.ShowsWhatWasWritten);
        Assert.IsTrue(ContentLanguages.For("python")!.Editing.ShowsWhatWasWritten);

        foreach (var drawn in new[] { "mermaid", "qr", "abc", "lilypond", "smiles" })
            Assert.AreNotEqual(true, ContentLanguages.For(drawn)!.Editing.ShowsWhatWasWritten, $"{drawn} draws a picture");
    }

    [TestMethod]
    public void AskingForAReadingNeverWaitsForOne()
    {
        CodeSpans.Forget();

        Assert.IsNull(CodeSpans.For("c-sharp", Source), "nothing is known yet, and nothing was waited for");
    }

    [TestMethod]
    public void CodeDrawsBeforeAnythingHasReadIt()
    {
        CodeSpans.Forget();

        var laid = Laying.Lay("csharp", Source, 480);

        Assert.IsTrue(laid.Size.Height > 0, "it drew");
        Assert.IsTrue(Pieces(laid).All(kind => kind == Kinds.Verbatim),
            "and every run is held as written, because nothing has said what any of it is");
    }

    [TestMethod]
    public void AndColoursItselfOnceTheReadingLands()
    {
        CodeSpans.Forget();

        Assert.IsTrue(Read("c-sharp", Source), "the grammar read it");

        var kinds = Pieces(Laying.Lay("csharp", Source, 480)).ToList();

        CollectionAssert.Contains(kinds, "keyword", $"what was found: {string.Join(", ", kinds.Distinct())}");
        Assert.IsTrue(kinds.Distinct().Count() > 1, "and not everything is one thing");
    }

    [TestMethod]
    public void WhatWasWrittenIsStillWhatIsDrawn()
    {
        CodeSpans.Forget();
        Read("c-sharp", Source);

        var laid = Laying.Lay("csharp", Source, 480);

        // Every run says it is the source, at the offset it was cut from — so the caret lands where it looks.
        foreach (var piece in laid.Root.SelfAndDescendants())
        {
            if (piece.Words is not { } words || piece.Part is not { } part) continue;

            Assert.IsTrue(words.Maps);
            Assert.AreEqual(words.Glyphs.Text, Source.Substring(part.Start, part.Length));
        }
    }

    [TestMethod]
    public void ALineWrittenTwiceIsEachWhereItIsWritten()
    {
        // Reported from the app: the caret in the second of two identical lines landed in the first, because each line was
        // found by what it says rather than cut where it ends.
        const string twice = "x = 1;\r\nx = 1;\ny\n";
        CodeSpans.Forget();

        var starts = Words(Laying.Lay("nothing-reads-this", twice, 480)).Select(part => part.Start).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 8, 15 }, starts, "held as written, each line where it is written");

        Read("c-sharp", twice);
        var read = Words(Laying.Lay("csharp", twice, 480)).ToList();

        Assert.AreEqual(read.Count, read.Select(part => part.Start).Distinct().Count(), "and read, no two runs in one place");
        Assert.IsTrue(read.Any(part => part.Start >= 8 && part.Start < 14), "with runs of the second line in it");
    }

    [TestMethod]
    public void ADocumentDrawsItsFenceAsCode()
    {
        var laid = Laying.Lay(null, "```csharp\n" + Source + "```\n", 480, StyleFormat.Dark);

        Assert.AreEqual(0, laid.Root.SelfAndDescendants().Count(piece => piece.Kind == MarkdownPieces.Verbatim),
            "the fence was laid by the code language, not shown as characters by the document");
    }

    [TestMethod]
    public void AFenceInALanguageNoGrammarReadsIsStillShown()
    {
        var laid = Laying.Lay(null, "```nothing-reads-this\nx = 1\n```\n", 480, StyleFormat.Dark);

        Assert.IsTrue(laid.Size.Height > 0);
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    /// <summary>Waits for the grammar to read it, which happens off the way to drawing.</summary>
    private static bool Read(string grammar, string source)
    {
        using var landed = new ManualResetEventSlim();

        void Done(object? sender, EventArgs args) => landed.Set();

        CodeSpans.Ready += Done;

        try
        {
            if (CodeSpans.For(grammar, source) is not null) return true;

            return landed.Wait(TimeSpan.FromSeconds(10)) && CodeSpans.For(grammar, source) is { Count: > 0 };
        }
        finally
        {
            CodeSpans.Ready -= Done;
        }
    }

    private static System.Collections.Generic.IEnumerable<string> Pieces(Laid laid) =>
        laid.Root.SelfAndDescendants().Where(piece => piece.Words is not null).Select(piece => piece.Kind);

    private static System.Collections.Generic.IEnumerable<ISourcePart> Words(Laid laid) =>
        laid.Root.SelfAndDescendants().Where(piece => piece.Words is not null && piece.Part is not null).Select(piece => piece.Part!);
}
