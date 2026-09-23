using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Another language drawn inside a diagram's own: a node whose label is a fenced block is drawn as that block, and
/// what it drew is grafted rather than painted — so every piece of it still stands for the characters it was written
/// as, in the source that holds it.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid")]
public class DiagramNestingTests
{
    private const string Inner = "x^2 + y^2";
    private const string Src = "graph TD\n  a[\"```latex " + Inner + "\"] --> b[\"Next\"]\n";

    private static ContentElement Drawn(string source)
    {
        var element = (ContentElement)Alone.Drawn("mermaid", source,
            new DiagramRenderOptions { Palette = StyleFormat.Dark });

        element.Measure(new Size(900, 900));
        element.Arrange(new Rect(0, 0, 900, 900));
        element.UpdateLayout();
        return element;
    }

    private static List<Piece> Pieces(ContentElement element, string kind) =>
        [.. element.Laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)];

    private static List<string> Words(ContentElement element) =>
        [.. element.Laid.Root.SelfAndDescendants()
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .OfType<string>()];

    [TestMethod]
    public void AFencedLabelIsDrawnAsTheLanguageItNames() => UiThread.Run(() =>
    {
        var element = Drawn(Src);
        var nested = Pieces(element, MermaidPiece.Nested).Single();

        Assert.IsTrue(nested.SelfAndDescendants().Count() > 1, "the formula's own layout was set down inside the node");
        Assert.IsFalse(Words(element).Any(said => said.Contains("```", StringComparison.Ordinal)),
                       "the fence is what the label is, not something drawn on it");
    });

    [TestMethod]
    public void WhatItDrewStandsForTheCharactersItWasWrittenAs() => UiThread.Run(() =>
    {
        var element = Drawn(Src);

        var from = Src.IndexOf(Inner, StringComparison.Ordinal);
        var to = from + Inner.Length;

        var inside = Pieces(element, MermaidPiece.Nested).Single()
            .SelfAndDescendants()
            .Where(piece => piece.Part is not null && piece.Sits() is { Length: > 0 })
            .ToList();

        Assert.IsTrue(inside.Count > 0, "a nested piece names the source it was drawn from");
        foreach (var piece in inside)
        {
            var sits = piece.Sits();
            Assert.IsTrue(sits.Start >= from && sits.End <= to,
                          $"{piece.Kind} sits at {sits.Start}..{sits.End}, outside the label at {from}..{to}");
        }
    });

    [TestMethod]
    public void AnOrdinaryLabelIsStillJustWords() => UiThread.Run(() =>
    {
        var element = Drawn("graph TD\n  a[\"Plain\"] --> b[\"Next\"]\n");

        Assert.AreEqual(0, Pieces(element, MermaidPiece.Nested).Count);
        CollectionAssert.Contains(Words(element), "Plain");
    });

    [TestMethod]
    public void AFenceNamingALanguageNothingDrawsIsStillJustWords() => UiThread.Run(() =>
    {
        var element = Drawn("graph TD\n  a[\"```nosuchthing hello\"] --> b[\"Next\"]\n");

        Assert.AreEqual(0, Pieces(element, MermaidPiece.Nested).Count);
        CollectionAssert.Contains(Words(element), "```nosuchthing hello",
                                  "what nothing here draws is drawn as the words it is");
    });

    [TestMethod]
    public void TheSourceIsUntouchedByWhatIsDrawnInsideIt() => UiThread.Run(() =>
    {
        Assert.AreEqual(Src, Drawn(Src).Source);
    });

    [TestMethod]
    public void ItReachesTheInnerLanguagesOwnTreeAndNotThisOne() => UiThread.Run(() =>
    {
        var element = Drawn(Src);

        // What a piece stands for is a part of the formula's own parse tree — read by LaTeX's parser, not by
        // anything here — positioned where it was written in the document that holds it.
        var inside = Pieces(element, MermaidPiece.Nested).Single()
            .SelfAndDescendants()
            .Select(piece => piece.Part)
            .OfType<Nexaflow.Markdown.Ast.ISourcePart>()
            .ToList();

        Assert.IsTrue(inside.Count > 0);
        Assert.IsTrue(inside.All(part => part is not Nexaflow.Markdown.Ast.ContentPart { Kind: Nexaflow.Markdown.Mermaid.MermaidKinds.Words }),
                      "none of it is a word of the flowchart — it is the formula's own tree");
    });

    [TestMethod]
    public void TheOuterTreeHoldsOneNodeSayingWhatIsInsideIt() => UiThread.Run(() =>
    {
        var block = Nexaflow.Markdown.Mermaid.MermaidBlock.Read(Src);

        var links = block.Reading.Root.SelfAndDescendants()
            .Where(part => part.Kind == Nexaflow.Markdown.Ast.Kinds.Nested)
            .ToList();

        Assert.AreEqual(1, links.Count, "one node for the block, never a parse of it into this tree");
        Assert.AreEqual(Src, block.Reading.Root.Print(), "and the outer tree still prints as it was written");

        var link = Nexaflow.Markdown.Ast.ContentLink.Of(links[0]);
        Assert.IsNotNull(link);
        Assert.AreEqual("latex", link!.Value.Language);
        Assert.AreEqual(Inner, link.Value.Source);
        Assert.AreEqual(Src.IndexOf(Inner, System.StringComparison.Ordinal), link.Value.At,
                        "and it says where that source begins in the document holding it");
    });

    [TestMethod]
    public void EveryDiagramThatDrawsALabelDrawsOneThatIsAnotherContent() => UiThread.Run(() =>
    {
        // Nothing here was taught about nesting: every diagram's words go through one place, so a label that is a
        // block of something else is drawn as that block wherever labels are drawn.
        foreach (var (what, source) in new[]
                 {
                     ("flowchart, unquoted", "graph TD\n  a[```latex " + Inner + "] --> b[Next]\n"),
                     ("class", "classDiagram\n  class A[\"```latex " + Inner + "\"]\n  A --> B\n"),
                     ("pie", "pie\n  \"```latex " + Inner + "\" : 3\n  \"Rest\" : 1\n"),
                 })
        {
            var element = Drawn(source);

            Assert.AreEqual(1, Pieces(element, MermaidPiece.Nested).Count, what);
            Assert.IsFalse(Words(element).Any(said => said.Contains("```", StringComparison.Ordinal)),
                           $"{what}: the fence is what the label is, not something drawn on it");
        }
    });

    [TestMethod]
    public void WordsAreOnlyAnotherContentWhereTheGrammarSaidSo() => UiThread.Run(() =>
    {
        // A state description is free text that no grammar has declared may hold another content, so a fence in it is
        // the characters it is. Nothing gets nesting by how its words happen to begin.
        var element = Drawn("stateDiagram-v2\n  s1 : ```latex " + Inner + "\n  s1 --> s2\n");

        Assert.AreEqual(0, Pieces(element, MermaidPiece.Nested).Count);
        Assert.IsTrue(Words(element).Any(said => said.Contains("```latex", StringComparison.Ordinal)));
    });

    [TestMethod]
    public void AWideBlockIsOneThingRatherThanBrokenIntoLines() => UiThread.Run(() =>
    {
        // A label wider than the wrapping width is broken into lines. A block of another language is not words, so
        // there is nothing in it to break — breaking a tune in half is not a smaller tune.
        const string wide = "a^2 + b^2 + c^2 + d^2 + e^2 + f^2 + g^2 + h^2 + i^2 + j^2 + k^2 + l^2";
        var element = Drawn("graph TD\n  a[\"```latex " + wide + "\"] --> b[\"Next\"]\n");

        Assert.AreEqual(1, Pieces(element, MermaidPiece.Nested).Count);
        Assert.IsFalse(Words(element).Any(said => said.Contains("a^2", StringComparison.Ordinal)),
                       "it was drawn as the formula it is, not re-broken into rows of its own characters");
    });
}
