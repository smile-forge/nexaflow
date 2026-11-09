using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Pages;
using NSubstitute;

namespace Nexaflow.Tests.Visuals.Pages;

/// <summary>Pages read into blocks written as one Markdown document, as a reader of the pages would take it.</summary>
[TestClass]
[CoversNode("page-reading")]
public class PageMarkdownTests
{
    private static PageBlock Block(PageBlockKind kind, string content = "") => new(kind, 0.1, 0.1, 0.9, 0.2, content);

    private static string Write(params PageBlock[][] pages) => PageMarkdown.Write(pages, (p, b) => $"[figure {p}.{b}]").TrimEnd();

    [TestMethod]
    public void AParagraphBrokenByAColumnOrAPage_IsOneParagraphAgain()
    {
        var markdown = Write(
            [Block(PageBlockKind.Text, "The output is computed as a weighted"), Block(PageBlockKind.Text, "sum of the values.")],
            [Block(PageBlockKind.Text, "A second paragraph, finished."), Block(PageBlockKind.Text, "Another, not broken off.")]);

        Assert.AreEqual("The output is computed as a weighted sum of the values.\n\nA second paragraph, finished.\n\nAnother, not broken off.", markdown);
    }

    [TestMethod]
    public void AParagraphGoingOnInTheNextColumn_IsOneParagraph_WhateverItsRestBeginsWith()
    {
        var markdown = Write([
            new PageBlock(PageBlockKind.Text, 0.05, 0.05, 0.47, 0.95, "Distinct-20 rose to 99.92%"),
            new PageBlock(PageBlockKind.Text, 0.52, 0.05, 0.94, 0.95, "98.73% on longer runs."),
            new PageBlock(PageBlockKind.Text, 0.52, 0.50, 0.94, 0.60, "A list item with no stop"),
            new PageBlock(PageBlockKind.Text, 0.52, 0.62, 0.94, 0.70, "Another, below it in the same column")]);

        Assert.AreEqual("Distinct-20 rose to 99.92% 98.73% on longer runs.\n\nA list item with no stop\n\nAnother, below it in the same column", markdown);
    }

    [TestMethod]
    public void AFigureSetIntoABrokenParagraph_ComesAfterIt_WithItsCaption()
    {
        var markdown = Write(
            [Block(PageBlockKind.Text, "An attention function maps a query to an output, computed as a weighted")],
            [Block(PageBlockKind.Margin, "4"), Block(PageBlockKind.Figure), Block(PageBlockKind.Caption, "Figure 2: Attention."),
             Block(PageBlockKind.Text, "sum of the values.")]);

        Assert.AreEqual("An attention function maps a query to an output, computed as a weighted sum of the values.\n\n" +
                        "[figure 1.1]\n\n*Figure 2: Attention.*", markdown);
    }

    [TestMethod]
    public void AWordBrokenAtTheLine_IsWholeAgain()
        => Assert.AreEqual("The computation is shared.", Write([Block(PageBlockKind.Text, "The computa-"), Block(PageBlockKind.Text, "tion is shared.")]));

    [TestMethod]
    public void Headings_TakeTheirLevelFromTheirNumbering_TheTitleBeforeThemIsTheDocuments()
    {
        var markdown = Write([
            Block(PageBlockKind.Title, "Unlimited OCR Works"), Block(PageBlockKind.Title, "Abstract"), Block(PageBlockKind.Title, "1. Introduction"),
            Block(PageBlockKind.Title, "3.2 Attention"), Block(PageBlockKind.Title, "3.2.1 Scaled Dot-Product Attention"), Block(PageBlockKind.Title, "A Appendix")]);

        Assert.AreEqual("# Unlimited OCR Works\n\n## Abstract\n\n## 1. Introduction\n\n### 3.2 Attention\n\n#### 3.2.1 Scaled Dot-Product Attention\n\n## A Appendix",
                        markdown);
    }

    [TestMethod]
    public void ABoxTitle_SaidAgainInAChapter_LeadsItsBoxInBold_AndOpensNoSection()
    {
        var markdown = Write([
            Block(PageBlockKind.Title, "1.2 Charges"), Block(PageBlockKind.Title, "Remember"), Block(PageBlockKind.Text, "Like charges repel."),
            Block(PageBlockKind.Title, "1.3 Fields"), Block(PageBlockKind.Title, "Remember"), Block(PageBlockKind.Text, "Fields add.")]);

        Assert.AreEqual("### 1.2 Charges\n\n**Remember**\n\nLike charges repel.\n\n### 1.3 Fields\n\n**Remember**\n\nFields add.", markdown);
    }

    [TestMethod]
    public void ASectionSaidOnceInEachChapter_StaysASection()
    {
        var markdown = Write([
            Block(PageBlockKind.Title, "Electrostatics"), Block(PageBlockKind.Title, "1.1 Charges"), Block(PageBlockKind.Title, "Summary"),
            Block(PageBlockKind.Title, "Currents"), Block(PageBlockKind.Title, "2.1 Flow"), Block(PageBlockKind.Title, "Summary")]);

        Assert.AreEqual("# Electrostatics\n\n### 1.1 Charges\n\n## Summary\n\n## Currents\n\n### 2.1 Flow\n\n## Summary", markdown);
    }

    [TestMethod]
    public void ANumberedItemOfASection_GoesUnderIt_AndAChapterNamedByAWordStaysAChapter()
    {
        var markdown = Write([
            Block(PageBlockKind.Title, "Book"), Block(PageBlockKind.Title, "3.2.1 Capacitors"), Block(PageBlockKind.Title, "Example 3.4: Two plates"),
            Block(PageBlockKind.Title, "Chapter 4 Currents"), Block(PageBlockKind.Title, "Appendix A Vectors")]);

        Assert.AreEqual("# Book\n\n#### 3.2.1 Capacitors\n\n##### Example 3.4: Two plates\n\n## Chapter 4 Currents\n\n## Appendix A Vectors", markdown);
    }

    [TestMethod]
    public void StepsNumberedWithinASection_GoUnderIt_WhileChaptersNumberedSoStayChapters()
    {
        var markdown = Write([
            Block(PageBlockKind.Title, "Book"), Block(PageBlockKind.Title, "3 Networks"), Block(PageBlockKind.Title, "3.10 Mesh currents"),
            Block(PageBlockKind.Title, "1. Draw the graph"), Block(PageBlockKind.Title, "4. Write the equations"),
            Block(PageBlockKind.Title, "3.10.1 The method"), Block(PageBlockKind.Title, "4 Semiconductors"), Block(PageBlockKind.Title, "4.1 Bands")]);

        Assert.AreEqual("# Book\n\n## 3 Networks\n\n### 3.10 Mesh currents\n\n#### 1. Draw the graph\n\n#### 4. Write the equations\n\n" +
                        "#### 3.10.1 The method\n\n## 4 Semiconductors\n\n### 4.1 Bands", markdown);
    }

    [TestMethod]
    public void Maths_IsBetweenDollars_InASentenceAndSetApart()
    {
        var markdown = Write([
            Block(PageBlockKind.Text, "We divide each by \\(  \\sqrt{d_k} \\) and apply a softmax:"),
            Block(PageBlockKind.Equation, "\\[\n\\operatorname{softmax}\\left(\\frac {Q K ^ {T}}{\\sqrt {d _ {k}}}\\right) V \\tag {1}\n\\]")]);

        Assert.AreEqual("We divide each by $\\sqrt{d_k}$ and apply a softmax:\n\n$$\n\\operatorname{softmax}\\left(\\frac {Q K ^ {T}}{\\sqrt {d _ {k}}}\\right) V \\tag {1}\n$$",
                        markdown);
    }

    [TestMethod]
    public void WhatMarkdownWouldReadAsMarks_IsWrittenAsItself()
        => Assert.AreEqual("It costs \\$5 for a\\_b \\* c.", Write([Block(PageBlockKind.Text, "It costs $5 for a_b * c.")]));

    [TestMethod]
    public void ATable_IsAMarkdownTable()
        => Assert.AreEqual("| Item | Qty |\n| --- | --- |\n| Bolt | 3 |",
                           Write([Block(PageBlockKind.Table, "<table><tr><td>Item</td><td>Qty</td></tr><tr><td>Bolt</td><td>3</td></tr></table>")]));

    [TestMethod]
    public void MathsInATable_IsBetweenDollars()
        => Assert.AreEqual("|  | $d_k$ |\n| --- | --- |\n| base | 64 |",
                           Write([Block(PageBlockKind.Table, "<table><tr><td></td><td>\\(d_k\\)</td></tr><tr><td>base</td><td>64</td></tr></table>")]));

    [TestMethod]
    public void Footnotes_AreGatheredAtTheEnd_AndThePagesEdgesLeftOut()
    {
        var markdown = Write(
            [Block(PageBlockKind.Margin, "Baidu"), Block(PageBlockKind.Text, "Products grow large."), Block(PageBlockKind.Footnote, "\\( ^{4} \\) To see why."),
             Block(PageBlockKind.Margin, "4")],
            [Block(PageBlockKind.Text, "The next page.")]);

        Assert.AreEqual("Products grow large.\n\nThe next page.\n\n---\n\n$^{4}$ To see why.", markdown);
    }
}

/// <summary>A figure cut whole, the parts the reading finds inside it its own, and found its caption.</summary>
[TestClass]
[CoversNode("page-reading")]
public class PageFiguresTests
{
    private static PageBlock Box(PageBlockKind kind, double left, double top, double right, double bottom, string content = "") =>
        new(kind, left, top, right, bottom, content);

    [TestMethod]
    public void AFigureAndThePartsReadInsideIt_AreOneFigure()
    {
        // As a textbook page reads: the figure, then each of its panels, a label under one and a letter naming it, all inside it.
        var figure = Box(PageBlockKind.Figure, 0.20, 0.09, 0.82, 0.37);
        var caption = Box(PageBlockKind.Caption, 0.13, 0.38, 0.79, 0.39, "Figure 1.23: Plates");
        var text = Box(PageBlockKind.Text, 0.13, 0.41, 0.88, 0.60, "Bringing in an insulator instead lowers the voltage.");
        var whole = PageFigures.Whole([
            Box(PageBlockKind.Margin, 0.06, 0.03, 0.08, 0.05, "1"), figure,
            Box(PageBlockKind.Figure, 0.20, 0.09, 0.35, 0.32), Box(PageBlockKind.Figure, 0.42, 0.09, 0.59, 0.32),
            Box(PageBlockKind.Equation, 0.20, 0.33, 0.35, 0.35, @"\[ U_a = E d \]"), Box(PageBlockKind.Text, 0.20, 0.355, 0.23, 0.37, "a"),
            caption, text]);

        CollectionAssert.AreEqual(new[] { PageBlockKind.Margin, PageBlockKind.Figure, PageBlockKind.Caption, PageBlockKind.Text }, whole.Select(block => block.Kind).ToArray());
        Assert.AreSame(figure, whole[1], "the figure itself, not a part of it");
    }

    [TestMethod]
    public void FiguresSideBySide_AndOneReadTwice_AreAsMany()
    {
        var left = Box(PageBlockKind.Figure, 0.1, 0.1, 0.45, 0.4);
        var right = Box(PageBlockKind.Figure, 0.55, 0.1, 0.9, 0.4);

        Assert.AreEqual(2, PageFigures.Whole([left, right]).Count, "neither inside the other");
        Assert.AreEqual(1, PageFigures.Whole([left, left with { }]).Count, "the same figure read twice is one, the first read");
    }

    [TestMethod]
    public void PanelsReadOneAfterAnother_OverOneCaption_AreOneFigure_TheBoxHoldingThemAll()
    {
        var whole = PageFigures.Whole([
            Box(PageBlockKind.Text, 0.12, 0.09, 0.86, 0.21, "A loop moves through the field."),
            Box(PageBlockKind.Figure, 0.14, 0.23, 0.51, 0.39), Box(PageBlockKind.Figure, 0.56, 0.23, 0.84, 0.37),
            Box(PageBlockKind.Caption, 0.12, 0.39, 0.76, 0.41, "Figure 6.5: A moving loop and its circuit")]);

        CollectionAssert.AreEqual(new[] { PageBlockKind.Text, PageBlockKind.Figure, PageBlockKind.Caption }, whole.Select(block => block.Kind).ToArray());
        Assert.AreEqual(new PageBlock(PageBlockKind.Figure, 0.14, 0.23, 0.84, 0.39, ""), whole[1]);
        Assert.AreEqual(2, PageFigures.Whole([Box(PageBlockKind.Figure, 0.1, 0.1, 0.4, 0.3), Box(PageBlockKind.Figure, 0.5, 0.1, 0.9, 0.3)]).Count,
                        "with no caption to say they are one, they stay two");
    }

    [TestMethod]
    public void AFiguresCaption_IsTheOneReadAfterIt_OrElseBeforeIt()
    {
        PageBlock figure = Box(PageBlockKind.Figure, 0.1, 0.1, 0.9, 0.4), below = Box(PageBlockKind.Caption, 0.1, 0.41, 0.9, 0.43, "Figure 2: Below."),
                  above = Box(PageBlockKind.Caption, 0.1, 0.05, 0.9, 0.07, "Figure 3: Above.");

        Assert.AreEqual("Figure 2: Below.", PageFigures.CaptionOf([above, figure, below], 1), "under it, as most are");
        Assert.AreEqual("Figure 3: Above.", PageFigures.CaptionOf([above, figure], 1), "over it, where none is under");
        Assert.IsNull(PageFigures.CaptionOf([figure, figure with { }, below], 0), "the next figure's is not its own");
        Assert.IsNull(PageFigures.CaptionOf([figure, below, Box(PageBlockKind.Text, 0.1, 0.5, 0.9, 0.6, "Text."), figure with { }], 3),
                      "nor one read before it with words between");
        Assert.IsNull(PageFigures.CaptionOf([figure, below, figure with { }], 2), "nor the one under the figure before");
    }
}

/// <summary>
/// A figure drawn again as a diagram only once it is shown to hold what the picture does: of a kind Mermaid draws, drawn as
/// Nexaflow draws it, its labels the picture's, and nothing found missing beside it — the picture kept otherwise, saying why.
/// </summary>
[TestClass]
[CoversNode("page-reading")]
public class DiagramDrawingTests
{
    private const string Drawing = "```mermaid\nflowchart LR\n    A[Source] --> B[Load]\n```";

    /// <summary>An image model answering each question as it is told to, writing down what it was asked.</summary>
    private static IAIService Model(List<string> asked, string kind, string drawing = Drawing, string labels = "Source\nLoad", string differences = "NONE")
    {
        var ai = Substitute.For<IAIService>();
        ai.AskAboutPictureAsync(Arg.Any<string>(), Arg.Any<ContextImage>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            string instruction = call.Arg<string>();
            lock (asked) asked.Add(instruction);
            return Task.FromResult<string?>(
                instruction == DiagramDrawing.KindInstruction ? kind
              : instruction == DiagramDrawing.LabelsInstruction ? labels
              : instruction == DiagramDrawing.DifferencesInstruction ? differences
              : drawing);
        });
        return ai;
    }

    /// <summary>A small white picture, as a figure cut from a page.</summary>
    private static byte[] Figure()
    {
        var pixels = new byte[60 * 40 * 4];
        Array.Fill(pixels, (byte)255);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(60, 40, 96, 96, PixelFormats.Bgra32, null, pixels, 60 * 4)));
        using var png = new MemoryStream();
        encoder.Save(png);
        return png.ToArray();
    }

    [TestMethod]
    public async Task ADrawingThatHoldsWhatThePictureDoes_TakesItsPlace()
    {
        var asked = new List<string>();
        var verdict = await DiagramDrawing.DrawAsync(Model(asked, "flowchart"), Figure(), CancellationToken.None);

        Assert.AreEqual("flowchart LR\n    A[Source] --> B[Load]", verdict.Diagram);
        Assert.AreEqual("flowchart", verdict.Kind);
        CollectionAssert.AreEqual(new[] { DiagramDrawing.KindInstruction, DiagramDrawing.Instruction("flowchart"), DiagramDrawing.LabelsInstruction, DiagramDrawing.DifferencesInstruction },
                                  asked, "its kind, its drawing, what is written in it, then the two side by side");
    }

    [TestMethod]
    public async Task AFigureMermaidDoesNotDraw_IsNeverDrawn()
    {
        var asked = new List<string>();
        var verdict = await DiagramDrawing.DrawAsync(Model(asked, "Circuit diagram."), Figure(), CancellationToken.None);

        Assert.IsNull(verdict.Diagram);
        Assert.AreEqual("circuit", verdict.Kind);
        StringAssert.Contains(verdict.Why, "circuit");
        Assert.AreEqual(1, asked.Count, "asked nothing more");
    }

    [TestMethod]
    public async Task ADrawingLeavingOutALabel_OrAddingOne_LeavesThePicture()
    {
        var left = await DiagramDrawing.DrawAsync(Model([], "flowchart", labels: "Source\nLoad\nDielectric"), Figure(), CancellationToken.None);
        Assert.IsNull(left.Diagram);
        StringAssert.Contains(left.Why, "leaves out dielectric");

        var asked = new List<string>();
        var added = await DiagramDrawing.DrawAsync(Model(asked, "flowchart", labels: "Source"), Figure(), CancellationToken.None);
        Assert.IsNull(added.Diagram);
        StringAssert.Contains(added.Why, "adds load");
        CollectionAssert.DoesNotContain(asked, DiagramDrawing.DifferencesInstruction, "nothing is compared by eye that the words already gave away");
    }

    [TestMethod]
    public async Task ADrawingTheModelFindsDifferent_SideBySide_LeavesThePicture()
    {
        var verdict = await DiagramDrawing.DrawAsync(Model([], "flowchart", differences: "- the battery symbol\n- the arrow from Load back to Source"),
                                                     Figure(), CancellationToken.None);

        Assert.IsNull(verdict.Diagram);
        StringAssert.Contains(verdict.Why, "the battery symbol; the arrow from Load back to Source");
    }

    [TestMethod]
    public async Task TheTwoAreShownAsOnePicture_TheDrawingDrawnBesideTheFigure()
    {
        var shown = new List<ContextImage>();
        var ai = Model([], "flowchart");
        ai.When(model => model.AskAboutPictureAsync(DiagramDrawing.DifferencesInstruction, Arg.Any<ContextImage>(), Arg.Any<CancellationToken>()))
          .Do(call => shown.Add(call.Arg<ContextImage>()));

        await DiagramDrawing.DrawAsync(ai, Figure(), CancellationToken.None);

        var both = BitmapDecoder.Create(new MemoryStream(shown.Single().Bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.IsTrue(both.PixelWidth > both.PixelHeight, "side by side");
    }

    [TestMethod]
    public void TheWordsWritten_AreComparedAsTheirLettersAndDigits()
    {
        var (missing, added) = DiagramDrawing.Labels(["U_a = E d", "Dielektrikum"], ["Ua = E d", "+"]);

        CollectionAssert.AreEqual(new[] { "dielektrikum" }, missing.ToArray());
        Assert.AreEqual(0, added.Count, "a subscript written either way, and a sign that is no word, are the same");
    }

    [TestMethod]
    public void AKind_IsTheFirstOfTheKindsOfferedThatAReplyNames()
    {
        Assert.AreEqual("er", DiagramDrawing.Kind("ER"));
        Assert.AreEqual("graph", DiagramDrawing.Kind("A graph."));
        StringAssert.Contains(DiagramDrawing.Instruction("graph"), "for a flowchart diagram", "a network of points drawn as a flowchart");
        Assert.AreEqual("sketch", DiagramDrawing.Kind("It is a sketch of two plates."));
        Assert.IsNull(DiagramDrawing.Kind("I cannot tell."));
        Assert.IsNull(DiagramDrawing.Kind(null));
    }

    [TestMethod]
    public void AListedReply_IsItsLines_AndNONEIsNone()
    {
        CollectionAssert.AreEqual(new[] { "one", "two", "three" }, DiagramDrawing.Lines("- one\n* two\n1. three\n")!.ToArray());
        Assert.AreEqual(0, DiagramDrawing.Lines("NONE.")!.Count);
        Assert.IsNull(DiagramDrawing.Lines(null));
    }

    [TestMethod]
    public void AFlowchartInAFence_IsTheDiagram()
        => Assert.AreEqual("flowchart TD\n    A[Inputs] --> B[Embedding]",
                           DiagramDrawing.Diagram("Here it is.\n```mermaid\nflowchart TD\n    A[Inputs] --> B[Embedding]\n```\n"));

    [TestMethod]
    public void NoneOrNoFence_IsNoDiagram()
    {
        Assert.IsNull(DiagramDrawing.Diagram("NONE"));
        Assert.IsNull(DiagramDrawing.Diagram("flowchart TD\n A --> B"));
        Assert.IsNull(DiagramDrawing.Diagram(null));
    }

    [TestMethod]
    public void ADiagramNexaflowDoesNotDraw_OrDrawsWithTrouble_IsNoDiagram()
    {
        Assert.IsNull(DiagramDrawing.Diagram("```mermaid\nnotadiagram\n  a b c\n```"));
        Assert.IsNull(DiagramDrawing.Diagram("```mermaid\nflowchart TD\n    A[Inputs --> B\n```"));
    }
}
