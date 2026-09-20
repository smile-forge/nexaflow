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
        var element = (ContentElement)DiagramRenderer.Render("mermaid", source,
            new DiagramRenderOptions { Palette = MarkdownPalette.Dark });

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
        Assert.AreEqual(Src, ((IEditableBlock)Drawn(Src)).Source);
    });
}
