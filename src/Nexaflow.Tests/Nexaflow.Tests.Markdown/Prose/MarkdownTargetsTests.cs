using System.Collections.Generic;
using System.IO;
using System.Linq;



using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// What a markdown document defines for its links — where a link by name goes, what an abbreviation stands for, what a heading
/// is called — is found by working the document over (<see cref="WithTargets"/>), and the links naming it are read with it
/// (<see cref="WithReferences"/>). The parse reads each block on its own, and knows none of it.
/// </summary>
[TestClass]
[CoversNode("reference-links")]
public class MarkdownTargetsTests
{
    private const string Defined = "\n\n[ref]: https://example.org\n[One Two]: https://two.org\n";

    [TestMethod]
    public void TheParse_HoldsALinkByName_AsTheWordsItWasWrittenIn()
    {
        var read = MarkdownParser.Parsing()("see [one][ref]\n\n[ref]: https://example.org\n").Tree;

        Assert.IsFalse(Links(read).Any(), "The parse read a link by a name it was never told.");
    }

    [TestMethod]
    public void ALinkByName_GoesWhereItsDefinitionSays_WhereverTheDefinitionIsWritten()
    {
        foreach (var source in new[]
        {
            "see [one][ref]\n\n[ref]: https://example.org\n",
            "[ref]: https://example.org\n\nsee [one][ref]\n",
            "[ref]: https://example.org\nsee [one][ref]\n",
            "see [one][ref]\n\n- an item\n\n  [ref]: https://example.org\n",
        })
        {
            var link = Links(Worked(source)).Single();

            Assert.AreEqual("https://example.org", MarkdownLinks.Goes(link), source);
            Assert.AreEqual("[one][ref]", link.Print(), source);
        }
    }

    [TestMethod]
    public void EveryWayOfNamingADefinition_IsALink_AndAPictureIsToo()
    {
        foreach (var (written, kind, goes) in new[]
        {
            ("[words][ref]", Kinds.Link, "https://example.org"),
            ("[ref][]", Kinds.Link, "https://example.org"),
            ("[ref]", Kinds.Link, "https://example.org"),
            ("[REF]", Kinds.Link, "https://example.org"),
            ("[one   two]", Kinds.Link, "https://two.org"),
            ("[*some* words][ref]", Kinds.Link, "https://example.org"),
            ("![a picture][ref]", MarkdownKinds.Image, "https://example.org"),
        })
        {
            var source = $"before {written} after" + Defined;
            var tree = Worked(source);
            var link = Links(tree).Single();

            Assert.AreEqual(kind, link.Kind, written);
            Assert.AreEqual(goes, MarkdownLinks.Goes(link), written);
            Assert.AreEqual(written, link.Print(), written);
            Assert.AreEqual(source, tree.Print(), written);
        }
    }

    [TestMethod]
    public void BracketsNamingNothingDefined_StayTheWordsTheyWere()
    {
        // A name follows the second, so it is a link by that name or nothing — never one by its own words.
        foreach (var written in new[] { "[words][nothing]", "[nothing]", "[ref][nothing]", "[] and [ ]", "[unclosed" })
        {
            var source = $"before {written} after" + Defined;
            var tree = Worked(source);

            Assert.IsFalse(Links(tree).Any(), written);
            Assert.AreEqual(source, tree.Print(), written);
        }
    }

    [TestMethod]
    public void ALinkHoldsNoLink_ButBracketsRoundOneAreWords()
    {
        var tree = Worked("see [a [ref] b] here" + Defined);
        var link = Links(tree).Single();

        Assert.AreEqual("[ref]", link.Print());
    }

    [TestMethod]
    [CoversNode("abbreviations")]
    public void AnAbbreviation_IsKnownWhereverItIsDefined_AsAWholeWordOnly()
    {
        var tree = Worked("HTML is a thing, and so is HTML. XHTML is not, nor HTMLs, nor `HTML`.\n\n*[HTML]: HyperText Markup Language\n");

        var said = tree.SelfAndDescendants().Where(node => node is { Kind: MarkdownKinds.Abbreviation, Role: Roles.Element }).ToList();

        Assert.AreEqual(2, said.Count);
        foreach (var one in said)
        {
            Assert.AreEqual("HTML", one.Print());
            Assert.AreEqual("HyperText Markup Language", one.HeldAs(Roles.Tip));
        }
    }

    [TestMethod]
    public void ADefinition_IsPutBackWhereItWasWritten_AndSaidInTheOrderWritten_TheFirstOfANameStanding()
    {
        const string Source = "words\n\n[b]: /b\n[a]: /a\n\nmore\n\n[A]: /second\n";
        var targets = new LinkTargets();
        var tree = Worked(Source, targets);

        var written = tree.SelfAndDescendants().Where(node => node is { Kind: MarkdownKinds.Reference, Role: Roles.Trivia }).ToList();

        CollectionAssert.AreEqual(new[] { "[b]: /b\n", "[a]: /a\n", "[A]: /second\n" }, written.Select(node => node.Print()).ToArray());
        CollectionAssert.AreEqual(new int?[] { Source.IndexOf("[b]"), Source.IndexOf("[a]"), Source.IndexOf("[A]") },
                                  written.Select(node => node.Offset).ToArray());
        Assert.AreEqual("/a", targets.Named(MarkdownKinds.Reference, WithTargets.Label("a"))?.Meaning);
    }

    [TestMethod]
    [CoversNode("markdown-anchors")]
    public void TwoHeadingsSayingTheSame_AreToldApartByTheOrderTheyAreWrittenIn()
    {
        var targets = new LinkTargets();
        var tree = Worked("# Notes\n\n## Notes\n\n### `Notes` & *more*\n", targets);

        CollectionAssert.AreEqual(new[] { "notes", "notes-1", "notes--more" },
                                  targets.Every(MarkdownKinds.Anchor).Select(target => target.Name).ToArray());
        Assert.AreEqual(3, tree.SelfAndDescendants().Count(node => node.Kind == MarkdownKinds.Anchor && node.Held is string));
    }

    /// <summary><paramref name="source"/> read, and worked over as a document's links are.</summary>
    private static ContentNode Worked(string source, LinkTargets? targets = null)
    {
        targets ??= new LinkTargets();

        return new AstPipeline(new WithTargets(targets), new WithReferences(targets)).Run(MarkdownParser.Parsing()(source).Tree);
    }

    private static IEnumerable<ContentNode> Links(ContentNode tree) =>
        tree.SelfAndDescendants().Where(node => node is { Kind: Kinds.Link or MarkdownKinds.Image, Role: Roles.Element });
}
