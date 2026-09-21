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
        Assert.IsInstanceOfType<CodeLanguage>(ContentLanguages.For("csharp"));
        Assert.IsInstanceOfType<CodeLanguage>(ContentLanguages.For("python"));

        // A word a language of its own claims must reach that one, however many words code answers to.
        Assert.IsNotInstanceOfType<CodeLanguage>(ContentLanguages.For("mermaid"));
        Assert.IsNotInstanceOfType<CodeLanguage>(ContentLanguages.For("qr"));
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

        var laid = CodeBuilder.Lay(Source, "c-sharp", StyleFormat.Dark, 480, 0);

        Assert.IsTrue(laid.Size.Height > 0, "it drew");
        Assert.IsTrue(Pieces(laid).All(kind => kind == Kinds.Verbatim),
            "and every run is held as written, because nothing has said what any of it is");
    }

    [TestMethod]
    public void AndColoursItselfOnceTheReadingLands()
    {
        CodeSpans.Forget();

        Assert.IsTrue(Read("c-sharp", Source), "the grammar read it");

        var kinds = Pieces(CodeBuilder.Lay(Source, "c-sharp", StyleFormat.Dark, 480, 0)).ToList();

        CollectionAssert.Contains(kinds, "keyword", $"what was found: {string.Join(", ", kinds.Distinct())}");
        Assert.IsTrue(kinds.Distinct().Count() > 1, "and not everything is one thing");
    }

    [TestMethod]
    public void WhatWasWrittenIsStillWhatIsDrawn()
    {
        CodeSpans.Forget();
        Read("c-sharp", Source);

        var laid = CodeBuilder.Lay(Source, "c-sharp", StyleFormat.Dark, 480, 0);

        // Every run says it is the source, at the offset it was cut from — so the caret lands where it looks.
        foreach (var piece in laid.Root.SelfAndDescendants())
        {
            if (piece.Words is not { } words || piece.Part is not { } part) continue;

            Assert.IsTrue(words.Maps);
            Assert.AreEqual(words.Glyphs.Text, Source.Substring(part.Start, part.Length));
        }
    }

    [TestMethod]
    public void ADocumentDrawsItsFenceAsCode()
    {
        var laid = MarkdownBuilder.Lay("```csharp\n" + Source + "```\n", StyleFormat.Dark, 480);

        Assert.AreEqual(0, laid.Root.SelfAndDescendants().Count(piece => piece.Kind == MarkdownPieces.Verbatim),
            "the fence was laid by the code language, not shown as characters by the document");
    }

    [TestMethod]
    public void AFenceInALanguageNoGrammarReadsIsStillShown()
    {
        var laid = MarkdownBuilder.Lay("```nothing-reads-this\nx = 1\n```\n", StyleFormat.Dark, 480);

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
}
