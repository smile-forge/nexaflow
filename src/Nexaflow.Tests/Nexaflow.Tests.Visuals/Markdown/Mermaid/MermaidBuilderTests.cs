using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// The drawing end every Mermaid diagram shares: a diagram drawn at the origin is framed under its title with whatever
/// could not be read set beneath it, and a block whose header names no type is shown as written with the reason.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-builder")]
public class MermaidBuilderTests
{
    /// <summary>The clear air between the card's edge and what is drawn on it — see MermaidBuilder.</summary>
    private const double Pad = 12;
    [TestMethod]
    public void AFrontMatterTitleIsSetOverTheDiagram_AndCarriesWhatWasWrittenForIt() => UiThread.Run(() =>
    {
        const string source = "---\ntitle: Pets\n---\npie\n  \"Dogs\" : 1";
        var laid = Box.Build(source);

        var title = Pieces(laid, MermaidPiece.Title).Single();
        Assert.AreEqual("Pets", Text(source, title.Part));

        var box = Pieces(laid, Box.Kind).Single();
        Assert.IsTrue(box.Bounds.Top >= title.Bounds.Bottom, "the diagram starts under its title");
        Assert.AreEqual(0, laid.Trouble.Count);
    });

    [TestMethod]
    public void WithNoTitleTheDiagramIsAtTheTop() => UiThread.Run(() =>
    {
        var laid = Box.Build("pie\n  \"Dogs\" : 1");

        // At the top of the card, which every diagram is drawn on — see MermaidBuilder.Card.
        Assert.IsFalse(Pieces(laid, MermaidPiece.Title).Any());
        Assert.AreEqual(laid.Root.Bounds.Top + Pad, Pieces(laid, Box.Kind).Single().Bounds.Top, 0.001);
        Assert.AreEqual(new Size(Box.Width + (Pad * 2), Box.Height + (Pad * 2)), laid.Size);
    });

    [TestMethod]
    public void TheWholeBlockIsOnePieceStandingForAllOfIt() => UiThread.Run(() =>
    {
        const string source = "pie\n  \"Dogs\" : 1";
        var root = Box.Build(source).Root;

        Assert.AreEqual(MermaidPiece.Diagram, root.Kind);
        Assert.AreEqual(source, Text(source, root.Part));
    });

    [TestMethod]
    public void WhatCouldNotBeReadIsSaidBeneath_AndWhereItWasWritten() => UiThread.Run(() =>
    {
        const string source = "pie\n  %%{init: never closed\n  \"Dogs\" : 1";
        var laid = Box.Build(source);

        var trouble = laid.Trouble.Single();
        Assert.AreEqual("%%{init: never closed", source.Substring(trouble.Start, trouble.Length));

        var reason = Pieces(laid, MermaidPiece.Trouble).Single();
        Assert.IsTrue(reason.Bounds.Top >= Pieces(laid, Box.Kind).Single().Bounds.Bottom, "beneath what did draw");
        Assert.IsTrue(laid.Size.Height >= reason.Bounds.Bottom - 0.001, "and inside the room the block takes");
    });

    [TestMethod]
    public void ADiagramThatThrowsIsShownAsWrittenWithTheReason() => UiThread.Run(() =>
    {
        var laid = Box.Build("pie", fail: true);

        Assert.IsTrue(laid.ShowsSource);
        StringAssert.Contains(laid.Trouble.Single().Message, "drew nothing");
    });

    [TestMethod]
    public void AHeaderNamingNoTypeDispatchesToTheBlockAsWritten() => UiThread.Run(() =>
    {
        const string source = "---\ntitle: T\n---\nwibble TD\n  a --> b";
        var content = (ContentElement)DiagramRenderer.Render("mermaid", source, MarkdownPalette.Dark);

        Assert.IsTrue(content.IsReadOnly);
        content.Measure(new Size(600, double.PositiveInfinity));
        Assert.IsTrue(content.DesiredSize.Width > 0 && content.DesiredSize.Height > 0);

        var trouble = content.Diagnostics.Single();
        Assert.AreEqual("wibble", source.Substring(trouble.Start, trouble.Length), "the wave is under the word that named nothing");
    });

    [TestMethod]
    public void AnUnknownDiagramShowsEveryCharacter_AndSaysWhy() => UiThread.Run(() =>
    {
        const string source = "---\ntitle: T\n---\nwibble TD\n  a --> b";
        var laid = UnknownDiagramBuilder.Build(source, MarkdownPalette.Dark, 1.0);

        var shown = Pieces(laid, LayoutText.SourceKind).Single();
        Assert.AreEqual(source, Text(source, shown.Part));
        Assert.IsFalse(Pieces(laid, MermaidPiece.Title).Any(), "its front matter is on the page already, title and all");
        Assert.AreEqual(1, Pieces(laid, MermaidPiece.Trouble).Count());
    });

    [TestMethod]
    public void AKnownDiagramNeverReachesTheUnknownBuilder() => UiThread.Run(() =>
    {
        // Every diagram a builder is named for is drawn on the shared layout tree — a drawing, not its own characters —
        // including a flowchart asking for nodes that fold, which is something the tree itself now does.
        foreach (var source in new[]
                 {
                     "pie\n  \"A\" : 1",
                     "C4Context\n  Person(a, \"A\")",
                     "---\nconfig:\n  nexaflow:\n    collapsed:\n      - b\n---\nflowchart TD\n  a --> b",
                 })
        {
            var drawn = (ContentElement)DiagramRenderer.Render("mermaid", source, MarkdownPalette.Dark);
            Assert.IsFalse(drawn.Laid.Root.SelfAndDescendants().Any(piece => piece.Kind == LayoutText.SourceKind), source);
        }
    });

    private static IEnumerable<Piece> Pieces(Laid laid, string kind) =>
        laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind);

    private static string Text(string source, Nexaflow.Markdown.Ast.ISourcePart? part) =>
        part is null ? "" : source.Substring(part.Start, part.Length);

    /// <summary>A diagram that is one box of a known size — what the frame is tested around.</summary>
    private sealed class Box(string source, bool fail) : MermaidBuilder(MermaidBuilders.Read(source), new DiagramLaying(MarkdownPalette.Dark))
    {
        public const string Kind = "Box";
        public const double Width = 120;
        public const double Height = 80;

        public static Laid Build(string source, bool fail = false) => new Box(source, fail).Lay();

        protected override Size Draw(MermaidBlock block, LayoutBuilder build)
        {
            if (fail) throw new System.InvalidOperationException("the diagram drew nothing");

            build.Open(Kind);
            build.Draw(new RuleMark(new Rect(0, 0, Width, Height), Brushes.Gray));
            build.Close();
            return new Size(Width, Height);
        }
    }
}
