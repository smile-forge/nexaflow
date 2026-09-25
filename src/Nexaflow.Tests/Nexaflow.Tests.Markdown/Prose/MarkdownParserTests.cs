using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// The tree a markdown document is read into: the blocks it is written in, each holding its own source and
/// nothing read out of it.
///
/// <para>
/// What was written prints back as it was written, whatever it was — and the documents include what nobody
/// means to write, a fence never closed, a table with a row missing, a heading with nothing after it, because
/// a document is read on every keystroke and most of what it is handed is half-written.
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class MarkdownParserTests
{
    private static readonly (string What, string Source)[] Documents =
    [
        ("nothing at all", ""),
        ("one paragraph", "just some words"),
        ("two paragraphs", "one\n\ntwo\n"),
        ("a heading and a paragraph", "# Title\n\nSome words.\n"),
        ("a heading with nothing after it", "## "),
        ("a setext heading", "Title\n=====\n\nwords\n"),
        ("a list", "- one\n- two\n- three\n"),
        ("a numbered list", "1. one\n2. two\n"),
        ("a task list", "- [ ] to do\n- [x] done\n"),
        ("a nested list", "- one\n  - under\n- two\n"),
        ("a quote", "> quoted\n> more\n"),
        ("an alert", "> [!NOTE]\n> Something worth knowing.\n"),
        ("a rule", "one\n\n---\n\ntwo\n"),
        ("a pipe table", "| a | b |\n|---|---|\n| 1 | 2 |\n"),
        ("a table with alignment", "| a | b | c |\n|:--|:-:|--:|\n| 1 | 2 | 3 |\n"),
        ("a table with no outer pipes", "a | b\n--|--\n1 | 2\n"),
        ("a fence", "```csharp\nvar x = 1;\n```\n"),
        ("a fence naming nothing", "```\nplain\n```\n"),
        ("a fence never closed", "```csharp\nvar x = 1;\n"),
        ("a diagram fence", "```mermaid\npie\n  \"a\" : 1\n```\n"),
        ("front matter", "---\ntitle: A Thing\n---\n\nwords\n"),
        ("indented code", "    indented\n    code\n"),
        ("a link reference", "[a]: https://example.org\n\nsee [a]\n"),
        ("a definition list", "Term\n:   what it means\n"),
        ("html", "<div>\n  raw\n</div>\n"),
        ("everything at once",
         "# Title\n\nWords with **bold** and `code`.\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n- [ ] a task\n\n"
         + "```mermaid\npie\n```\n\n> quoted\n"),
        ("windows line endings", "# Title\r\n\r\none\r\n\r\ntwo\r\n"),
        ("only blank lines", "\n\n\n"),
        ("only space", "   "),
        ("a tab", "\tindented\n"),
    ];

    [TestMethod]
    public void EveryDocumentReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in Documents)
            Assert.AreEqual(source, MarkdownParser.Read(source).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryDocumentReadsBackToo()
    {
        foreach (var (what, source) in Documents)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, MarkdownParser.Read(typed).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in Documents)
            foreach (var place in MarkdownParser.Read(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= source.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {source.Length}");

                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
    }

    // ── A document is a list of blocks ──────────────────────────────────────

    [TestMethod]
    public void EachBlockIsOneNodeHoldingItsOwnSource()
    {
        var blocks = Blocks("# Title\n\nSome words.\n");

        Assert.AreEqual(2, blocks.Count);
        Assert.AreEqual(MarkdownKinds.Heading, blocks[0].Kind);
        Assert.AreEqual("# Title\n", Body(blocks[0]).TrimEnd('\n') + "\n");
        Assert.AreEqual(MarkdownKinds.Paragraph, blocks[1].Kind);

        // A block owns the line ending that ends it, which is what lets the next one start where it does.
        Assert.AreEqual("Some words.\n", Body(blocks[1]));
    }

    [TestMethod]
    public void NothingIsReadOutOfABlocksBody()
    {
        // The body is the next parser's, so it is one stretch held as written — not emphasis and words.
        var paragraph = Blocks("Words with **bold** in them.")[0];

        Assert.AreEqual(Kinds.Verbatim, paragraph.Part(Roles.Body)?.Kind);
        Assert.AreEqual("Words with **bold** in them.", Body(paragraph).TrimEnd('\n'));
    }

    [TestMethod]
    public void WhatFallsBetweenTwoBlocksIsKeptWhereItWas()
    {
        var document = MarkdownParser.Read("one\n\n\n\ntwo\n");

        Assert.AreEqual("one\n\n\n\ntwo\n", document.Print());
        Assert.AreEqual(2, Blocks("one\n\n\n\ntwo\n").Count);
    }

    [TestMethod]
    public void EveryKindOfBlockIsNamedByWhatReadsIt()
    {
        Assert.AreEqual(MarkdownKinds.Table, Blocks("| a | b |\n|---|---|\n| 1 | 2 |\n")[0].Kind);
        Assert.AreEqual(MarkdownKinds.List, Blocks("- one\n")[0].Kind);
        Assert.AreEqual(MarkdownKinds.Quote, Blocks("> quoted\n")[0].Kind);
        Assert.AreEqual(MarkdownKinds.Rule, Blocks("---\n\nwords\n")[0].Kind);
        Assert.AreEqual(MarkdownKinds.FrontMatter, Blocks("---\ntitle: x\n---\n\nwords\n")[0].Kind);
        Assert.AreEqual(MarkdownKinds.Html, Blocks("<div>\nraw\n</div>\n")[0].Kind);
    }

    // ── A fence is the one block that says its own language ─────────────────

    [TestMethod]
    public void AFenceKeepsTheLanguageTheWriterNamed()
    {
        var fence = Blocks("```csharp\nvar x = 1;\n```\n")[0];

        Assert.AreEqual(MarkdownKinds.Fence, fence.Kind);
        Assert.AreEqual("csharp", fence.Part(Roles.Name)?.Text);
        Assert.AreEqual("var x = 1;\n", Body(fence));
    }

    [TestMethod]
    public void AFencesBodyIsHeldAsWritten()
    {
        // What is in there is a different language, and reading it is its own parser's business.
        var fence = Blocks("```mermaid\npie\n  \"a\" : 1\n```\n")[0];

        Assert.AreEqual(Kinds.Verbatim, fence.Part(Roles.Body)?.Kind);
        Assert.AreEqual("pie\n  \"a\" : 1\n", Body(fence));
    }

    [TestMethod]
    public void AFenceNamingNothingStillHoldsItsSource()
    {
        var fence = Blocks("```\nplain\n```\n")[0];

        Assert.IsNull(fence.Part(Roles.Name));
        Assert.AreEqual("plain\n", Body(fence));
    }

    [TestMethod]
    public void AFenceNeverClosedIsStillAFence()
    {
        // Half-written input is what an editor holds all day.
        var fence = Blocks("```csharp\nvar x = 1;\n")[0];

        Assert.AreEqual(MarkdownKinds.Fence, fence.Kind);
        Assert.AreEqual("csharp", fence.Part(Roles.Name)?.Text);
    }

    [TestMethod]
    public void WhatTheDocumentDefinesIsWrittenDownForItsWords()
    {
        var read = MarkdownParser.Read("see [one][ref] and HTML\n\n[ref]: https://example.org\n\n*[HTML]: HyperText Markup Language\n");

        var defined = MarkdownDefinitions.Of(read)?.Text ?? string.Empty;

        StringAssert.Contains(defined, "[ref]: https://example.org");
        StringAssert.Contains(defined, "*[HTML]: HyperText Markup Language");
        Assert.AreEqual("see [one][ref] and HTML\n\n[ref]: https://example.org\n\n*[HTML]: HyperText Markup Language\n", read.Print(),
                        "and nothing of it is written twice");
    }

    [TestMethod]
    public void ALinkNamingADefinitionGoesWhereTheDefinitionSays()
    {
        var read = MarkdownParser.Parsing()("see [one][ref]\n\n[ref]: https://example.org\n").Tree;

        var link = read.SelfAndDescendants().First(node => node.Kind == MarkdownKinds.Link);

        Assert.AreEqual("https://example.org", MarkdownLinks.Goes(link));
    }

    [TestMethod]
    public void AnAbbreviationDefinedElsewhereIsKnownInTheSentence()
    {
        var read = MarkdownParser.Parsing()("*[HTML]: HyperText Markup Language\n\nHTML is a thing\n").Tree;

        Assert.IsTrue(read.SelfAndDescendants().Any(node => node.Kind == MarkdownKinds.Abbreviation));
    }

    [TestMethod]
    public void APipeTableUnderALineOfWordsIsABlockOfItsOwn()
    {
        // GFM lets a table interrupt a paragraph, and the paragraph's own span is left reaching over it.
        var read = MarkdownParser.Read("Some intro text\n| a | b |\n|---|---|\n| 1 | 2 |\n");

        CollectionAssert.AreEqual(new[] { MarkdownKinds.Paragraph, MarkdownKinds.Table },
                                  read.Children.Where(child => child.Role != Roles.Trivia && !child.IsDerived).Select(child => child.Kind).ToArray());
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static IReadOnlyList<ContentNode> Blocks(string source) =>
        [.. MarkdownParser.Read(source).Children.Where(child => child.Role != Roles.Trivia)];

    private static string Body(ContentNode node) => node.Part(Roles.Body)?.Print() ?? string.Empty;
}
