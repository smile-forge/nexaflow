using System.IO;
using Nexaflow.Core.Services.Tools;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Pages;

namespace Nexaflow.Tests.Core.Unit.Tools;

/// <summary>
/// The makers' own readings of the recipe's sample pages — where a machine has them
/// (<c>NEXAFLOW_TEST_PAGE_READING_REFERENCE</c>) — read into blocks and written as Markdown: what a person gets from
/// pages of a paper, its columns and its small print.
/// </summary>
[TestClass]
[CoversNode("page-reading")]
public class PageReadingReferenceTests
{
    private static string Reading(string page)
    {
        string? reference = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_PAGE_READING_REFERENCE");
        if (string.IsNullOrEmpty(reference))
            Assert.Inconclusive("NEXAFLOW_TEST_PAGE_READING_REFERENCE names no page reading reference on this machine.");
        return File.ReadAllText(Path.Combine(reference!, "reader", page + ".md"));
    }

    private static string Markdown(params string[] pages)
        => PageMarkdown.Write([.. pages.Select(p => PageMarks.Read(Reading(p)))], (p, b) => $"![figure]({p}-{b}.png)");

    [TestMethod]
    public void APapersPages_ReadAsOneDocument_ItsTitleItsHeadingsItsMathsAndItsFigures()
    {
        string markdown = Markdown("paper-p1", "paper-p3", "paper-p5");

        StringAssert.StartsWith(markdown, "# ", "the paper's title heads the document");
        StringAssert.Contains(markdown, "\n## 1. Introduction\n");
        StringAssert.Contains(markdown, "\n### 3.1. Long-horizon Parsing\n");
        StringAssert.Contains(markdown, "![figure](0-", "the first page's figure is in it");
        Assert.IsFalse(markdown.Contains("<|det|>") || markdown.Contains("<|/det|>"), "no reader's marks are left");
        Assert.IsFalse(markdown.Contains("\\("), "maths in a sentence is between dollars");
        Assert.IsFalse(markdown.Contains("<table"), "tables are Markdown");
    }

    [TestMethod]
    public void TwoColumns_ReadAsOneText()
    {
        var blocks = PageMarks.Read(Reading("two-columns"));
        string markdown = Markdown("two-columns");

        Assert.IsTrue(blocks.Count(b => b.Kind == PageBlockKind.Text) >= 2, "the reader gives each column its block");
        Assert.AreEqual(1, markdown.Trim().Split("\n\n").Length, "the column broken mid-sentence goes on in the next, as one paragraph");
    }
}
