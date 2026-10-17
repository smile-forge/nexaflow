using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Pages;

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

/// <summary>A model's reply taken as a diagram only when it is one Nexaflow draws as written.</summary>
[TestClass]
[CoversNode("page-reading")]
public class DiagramDrawingTests
{
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
