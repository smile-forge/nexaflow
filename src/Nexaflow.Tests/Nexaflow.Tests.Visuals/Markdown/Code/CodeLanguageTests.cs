using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Markdown.Languages;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Tests.Visuals.Markdown.Code;

/// <summary>
/// Code in a fence, coloured by the engine that already reads code — and drawn before it has.
///
/// <para>
/// The whole point is the order. A document re-lays on every keystroke and compiling a grammar's query costs
/// about fifteen milliseconds, so colouring is never on the way to drawing: code is read in two stages, as written
/// at once and by its grammar away from the thread that draws, and colours itself when the second reading lands.
/// These say both halves, and that the second one arrives.
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
    public void EveryGrammarIsALanguageOfItsOwn_ReadOnlyAfterEveryOtherLanguage()
    {
        Assert.AreEqual(typeof(CodeBuilder), ContentLanguages.For("csharp")!.Builder);
        Assert.AreSame(ContentLanguages.For("csharp"), ContentLanguages.For("c#"), "one language, whatever a writer calls it");
        Assert.AreNotSame(ContentLanguages.For("csharp"), ContentLanguages.For("python"), "and another for another grammar");

        // A word a language of its own claims must reach that one, however many words code answers to.
        Assert.AreNotEqual(typeof(CodeBuilder), ContentLanguages.For("mermaid")!.Builder);
        Assert.AreNotEqual(typeof(CodeBuilder), ContentLanguages.For("qr")!.Builder);
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
    public void CodeDrawsBeforeAnythingHasReadIt_WaitingForNothing()
    {
        var laid = Laying.Lay("csharp", Unread(Source), 480);

        Assert.IsTrue(laid.Size.Height > 0, "it drew");
        Assert.IsTrue(Pieces(laid).All(kind => kind == Kinds.Verbatim),
            "and every run is held as written, because nothing has said what any of it is");
    }

    [TestMethod]
    public void AndColoursItselfOnceTheReadingLands()
    {
        var kinds = Pieces(Landed("csharp", Unread(Source))).ToList();

        CollectionAssert.Contains(kinds, "keyword", $"what was found: {string.Join(", ", kinds.Distinct())}");
        Assert.IsTrue(kinds.Distinct().Count() > 1, "and not everything is one thing");
    }

    [TestMethod]
    public void AFenceInADocumentIsReadAgainWhenItsReadingLands()
    {
        var engine = new ContentEngine();
        var document = "# Code\n\n```csharp\n" + Unread(Source) + "```\n";

        Assert.IsTrue(Reread(engine, () => engine.Lay(null, EditState.For(document), StyleFormat.Dark, 480, readOnly: true)), "it said so");

        engine.Forget();
        CollectionAssert.Contains(Pieces(engine.Lay(null, EditState.For(document), StyleFormat.Dark, 480, readOnly: true)).ToList(), "keyword",
                                  "and what was read the first time is read again");
    }

    [TestMethod]
    public void WhatWasWrittenIsStillWhatIsDrawn()
    {
        var source = Unread(Source);
        var laid = Landed("csharp", source);

        // Every run says it is the source, at the offset it was cut from — so the caret lands where it looks.
        foreach (var piece in laid.Root.SelfAndDescendants())
        {
            if (piece.Words is not { } words || piece.Part is not { } part) continue;

            Assert.IsTrue(words.Maps);
            Assert.AreEqual(words.Glyphs.Text, source.Substring(part.Start, part.Length));
        }
    }

    [TestMethod]
    public void AndEveryPieceOfItSaysWhereItWasReadFrom()
    {
        // The two things a round trip cannot tell you: that every piece standing for characters says where it was
        // read from, and that what it says is held against the source. A tree printed in the order it was built
        // comes out the same whether its pieces know their places or not, so this asks them instead — and then
        // reverses the parts of every piece, which only still prints as the source if each of them does know.
        var tree = new CodeLines().Run(CodeParser.Parse(Source, "c-sharp", CodeSpans.Read("c-sharp", Source)));

        var faults = AstOracle.Faults(Source, tree).ToList();

        Assert.AreEqual(0, faults.Count, string.Join("\n", faults));
        Assert.AreEqual(Source, AstOracle.Reversed(tree).Print(), "reversed, which is a stage handing back what it was given in any order");
    }

    [TestMethod]
    public void ALineWrittenTwiceIsEachWhereItIsWritten()
    {
        // Reported from the app: the caret in the second of two identical lines landed in the first, because each line was
        // found by what it says rather than cut where it ends.
        const string twice = "x = 1;\r\nx = 1;\ny\n";

        var starts = Words(Laying.Lay("nothing-reads-this", twice, 480)).Select(part => part.Start).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 8, 15 }, starts, "held as written, each line where it is written");

        var read = Words(Landed("csharp", twice)).ToList();

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

    /// <summary>
    /// <paramref name="source"/> with a comment no other source has — what is read slowly is kept for every engine, so content
    /// nothing has read yet is content nobody has written before.
    /// </summary>
    private static string Unread(string source) => $"// {Guid.NewGuid():N}\n{source}";

    [TestMethod]
    public void TheSpaceBetweenTwoWordsAGrammarNamedIsStillThere()
    {
        // A grammar names the words and leaves the space between them unnamed, so a run of spaces is a token of its
        // own. Measured by what it is set in rather than by how far it moves what follows, such a token comes to
        // nothing and the words either side of it close up — `public static` set as `publicstatic`.
        var laid = Landed("csharp", "public static int Of(string text) => text.Length;");

        var along = laid.Root.SelfAndDescendants()
            .Where(piece => piece.Words is not null)
            .Select(piece => piece.Bounds.X)
            .ToList();

        Assert.IsTrue(along.Count > 4, $"the grammar made tokens of it, not {along.Count}");

        for (var at = 1; at < along.Count; at++)
            Assert.IsTrue(along[at] > along[at - 1],
                          $"token {at} starts at {along[at]}, which is not past the {along[at - 1]} of the one before it");
    }

    /// <summary><paramref name="source"/> laid out once the grammar has read it, which happens off the way to drawing.</summary>
    private static Laid Landed(string language, string source)
    {
        var engine = new ContentEngine();
        Laid Lay() => engine.Lay(language, EditState.For(source), StyleFormat.Dark, 480, readOnly: true);

        Assert.IsTrue(Reread(engine, () => Pieces(Lay()).Any(kind => kind != Kinds.Verbatim)), "the grammar read it");
        return Lay();
    }

    /// <summary>
    /// Lays out with <paramref name="lay"/> and waits for a slower reading of it to land — true at once where what
    /// <paramref name="lay"/> says shows it already has.
    /// </summary>
    private static bool Reread(ContentEngine engine, Func<object> lay)
    {
        using var landed = new ManualResetEventSlim();

        void Done(object? sender, EventArgs args) => landed.Set();

        engine.Reread += Done;

        try
        {
            return lay() is true || landed.Wait(TimeSpan.FromSeconds(10));
        }
        finally
        {
            engine.Reread -= Done;
        }
    }

    private static System.Collections.Generic.IEnumerable<string> Pieces(Laid laid) =>
        laid.Root.SelfAndDescendants().Where(piece => piece.Words is not null).Select(piece => piece.Kind);

    private static System.Collections.Generic.IEnumerable<ISourcePart> Words(Laid laid) =>
        laid.Root.SelfAndDescendants().Where(piece => piece.Words is not null && piece.Part is not null).Select(piece => piece.Part!);
}
