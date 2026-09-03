using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System.Windows.Controls;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The seam that lets one renderer draw both a native <c>sequenceDiagram</c> and a C4 sequence: a
/// participant may carry an element card instead of a box, a message may carry a technology line,
/// and the foot boxes may be switched off. The load-bearing half of these tests is the *negative*
/// one — a native diagram must set none of it and must lay out exactly as it always did.
/// </summary>
[TestClass]
public class SequenceSharedRendererTests
{
    private const string NativeSrc =
        """
        sequenceDiagram
            participant U as User
            participant N as Nexaflow
            U->>N: Open notes.md
            N-->>U: Rendered document
        """;

    private static Canvas CanvasOf(SequenceDiagram d, MarkdownPalette? palette = null)
    {
        var border = (System.Windows.Controls.Border)WpfSequenceDiagramRenderer.Render(d, palette ?? MarkdownPalette.Dark);
        return (Canvas)((ScrollViewer)border.Child).Content;
    }

    // ── The native path is untouched ───────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void Native_SetsNoneOfTheC4Fields()
    {
        var d = new MermaidSequenceParser().Parse(NativeSrc);
        Assert.IsTrue(d.ShowFootBoxes, "Mermaid always repeats the heads at the bottom");
        Assert.IsTrue(d.Participants.All(p => p.Card is null), "no native participant is a card");
        Assert.IsTrue(d.Messages.All(m => m.Technology is null), "no native message has a technology");
    }

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void FootBoxesOn_IsTheDefaultAndKeepsTheBottomBand() => UiThread.Run(() =>
    {
        var d = new MermaidSequenceParser().Parse(NativeSrc);
        var on = CanvasOf(d);

        d.ShowFootBoxes = false;
        var off = CanvasOf(d);

        // Turning them off reclaims exactly the tallest head; nothing above the band moves.
        Assert.IsTrue(on.Height > off.Height, $"foot boxes should add height ({on.Height} vs {off.Height})");
        Assert.AreEqual(on.Width, off.Width, 1e-9, "width must not depend on the foot boxes");
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void FootBoxesOff_DropsTheRepeatedHeads() => UiThread.Run(() =>
    {
        var d = new MermaidSequenceParser().Parse(NativeSrc);
        int withFeet = CanvasOf(d).Children.Count;

        d.ShowFootBoxes = false;
        int withoutFeet = CanvasOf(d).Children.Count;

        Assert.IsTrue(withoutFeet < withFeet, "the bottom heads are real elements and should be gone");
    });

    // ── Message technology ────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void MessageTechnology_AddsARowAndAnElement() => UiThread.Run(() =>
    {
        var plain = new MermaidSequenceParser().Parse(NativeSrc);
        double plainH = CanvasOf(plain).Height;
        int plainCount = CanvasOf(plain).Children.Count;

        var teched = new MermaidSequenceParser().Parse(NativeSrc);
        foreach (var m in teched.Messages) m.Technology = "HTTPS";
        var canvas = CanvasOf(teched);

        Assert.IsTrue(canvas.Height > plainH, $"a technology line should grow the diagram ({plainH} -> {canvas.Height})");
        Assert.AreEqual(plainCount + teched.Messages.Count, canvas.Children.Count,
            "one extra text element per message carrying a technology");
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void BlankTechnology_ChangesNothing() => UiThread.Run(() =>
    {
        var plain = new MermaidSequenceParser().Parse(NativeSrc);
        var blank = new MermaidSequenceParser().Parse(NativeSrc);
        foreach (var m in blank.Messages) m.Technology = "   ";

        Assert.AreEqual(CanvasOf(plain).Height, CanvasOf(blank).Height, 1e-9);
        Assert.AreEqual(CanvasOf(plain).Children.Count, CanvasOf(blank).Children.Count);
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void SelfMessageWithTechnologyOnly_StillDrawsItsLabel() => UiThread.Run(() =>
    {
        // A C4 relationship may have a technology and no text; the self-message path used to draw a
        // label only when the text was non-empty.
        var d = new MermaidSequenceParser().Parse("sequenceDiagram\n  A->>A: \n");
        var bare = CanvasOf(d).Children.Count;

        var withTech = new MermaidSequenceParser().Parse("sequenceDiagram\n  A->>A: \n");
        withTech.Messages[0].Technology = "JDBC";
        Assert.IsTrue(CanvasOf(withTech).Children.Count > bare);
    });

    // ── Box groupings ─────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void LabelledBox_ReservesABandAboveTheHeads() => UiThread.Run(() =>
    {
        // The label used to sit three pixels above the tallest head, so any head taller than the
        // rest — a C4 card, a database glyph — was drawn straight through it.
        var plain = new MermaidSequenceParser().Parse("sequenceDiagram\n  participant A\n  A->>A: x\n");
        var boxed = new MermaidSequenceParser().Parse("sequenceDiagram\n  box Group\n  participant A\n  end\n  A->>A: x\n");

        Assert.IsTrue(CanvasOf(boxed).Height > CanvasOf(plain).Height,
            "a labelled box should reserve a band for its label");
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void UnlabelledBox_ReservesNothing() => UiThread.Run(() =>
    {
        var plain     = new MermaidSequenceParser().Parse("sequenceDiagram\n  participant A\n  A->>A: x\n");
        var unlabeled = new MermaidSequenceParser().Parse("sequenceDiagram\n  box transparent\n  participant A\n  end\n  A->>A: x\n");

        Assert.AreEqual(CanvasOf(plain).Height, CanvasOf(unlabeled).Height, 1e-9,
            "there is no label to make room for");
    });

    // ── Card participants ─────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void CardParticipant_RendersAndWidensItsColumn() => UiThread.Run(() =>
    {
        var plain = new MermaidSequenceParser().Parse(NativeSrc);
        double plainW = CanvasOf(plain).Width;

        var carded = new MermaidSequenceParser().Parse(NativeSrc);
        carded.Find("N")!.Card = new C4ElementInfo
        {
            Kind = C4ElementKind.Container,
            Technology = "Java, Spring MVC",
            Description = "Delivers the static content and the banking single page application.",
        };
        var canvas = CanvasOf(carded);

        Assert.IsTrue(canvas.Width > plainW, $"a card head is wider than a box ({plainW} -> {canvas.Width})");
        Assert.IsTrue(canvas.Height > CanvasOf(plain).Height, "and taller");
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void CardParticipant_HeadIsMeasuredByTheSharedMetrics() => UiThread.Run(() =>
    {
        var d = new MermaidSequenceParser().Parse(NativeSrc);
        var card = new C4ElementInfo { Kind = C4ElementKind.Person, Shape = C4ElementShape.Person, Description = "A customer." };
        d.Find("U")!.Card = card;

        // The renderer must size the column from C4ElementMetrics, not from the label alone —
        // otherwise the card it paints would not fit the space the timeline reserved.
        var (cardW, _) = C4ElementMetrics.Measure("User", card);
        Assert.IsTrue(CanvasOf(d).Width >= cardW, "the canvas must be at least as wide as the card");
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void CardParticipant_WorksOnBothPalettesAndWithEveryShape() => UiThread.Run(() =>
    {
        foreach (var palette in new[] { MarkdownPalette.Dark, MarkdownPalette.Light })
            foreach (var shape in Enum.GetValues<C4ElementShape>())
            {
                var d = new MermaidSequenceParser().Parse(NativeSrc);
                d.Find("N")!.Card = new C4ElementInfo { Kind = C4ElementKind.Container, Shape = shape, Description = "d" };
                Assert.IsNotNull(CanvasOf(d, palette), $"{shape} on {(palette == MarkdownPalette.Dark ? "dark" : "light")}");
            }
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void CardParticipant_MixesWithNativeBoxesAndActors() => UiThread.Run(() =>
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n  actor A as Admin\n  participant DB@{ \"type\": \"database\" }\n  participant S as Svc\n  A->>S: go\n  S->>DB: q\n");
        d.Find("S")!.Card = new C4ElementInfo { Kind = C4ElementKind.Container, Technology = "Go" };

        // The point of the seam: a card, an actor glyph and a typed box coexist on one timeline.
        Assert.IsNotNull(CanvasOf(d));
        Assert.IsNull(d.Find("A")!.Card);
        Assert.AreEqual(ParticipantKind.Database, d.Find("DB")!.Kind);
    });
}
