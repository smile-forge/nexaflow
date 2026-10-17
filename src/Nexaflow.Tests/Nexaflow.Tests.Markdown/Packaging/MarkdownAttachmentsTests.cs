using System.IO;
using System.Linq;
using Nexaflow.Markdown.Packaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Packaging;

/// <summary>The files a document shows or links to from beside itself: found by its links, pictures and links'
/// definitions, each where its address is written — and nothing that is not beside it.</summary>
[TestClass]
[CoversNode("markdown-package")]
public class MarkdownAttachmentsTests
{
    private const string Written =
        "# Notes\n\n" +
        "![first](scan.figures/figure%201.png) and ![again](scan.figures/figure%201.png)\n\n" +
        "![other](<more/figure 1.png>)\n\n" +
        "[the spec][spec] and [the web](https://example.com/a.png) and [up](../up.png) and [here](#notes)\n\n" +
        "[spec]: spec.pdf\n";

    [TestMethod]
    public void TheFilesADocumentNamesBesideItself_AreFound_EachOnce()
        => CollectionAssert.AreEquivalent(
            new[] { Path.Combine("scan.figures", "figure 1.png"), Path.Combine("more", "figure 1.png"), "spec.pdf" },
            MarkdownAttachments.Paths(Written).ToArray(),
            "pictures and linked files beside it — not the web, not up out of its folder, not a place in itself");

    [TestMethod]
    public void EachIsFound_WhereItsAddressIsWritten()
    {
        var found = MarkdownAttachments.Find(Written);

        CollectionAssert.AreEquivalent(
            new[] { "scan.figures/figure%201.png", "scan.figures/figure%201.png", "more/figure 1.png", "spec.pdf" },
            found.Select(a => Written.Substring(a.At, a.Length)).ToArray(),
            "a link's definition is found in its own characters, an address in brackets without them");
    }
}
