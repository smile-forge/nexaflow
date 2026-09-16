using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// The tree a <c>mermaid</c> block is read into: what was written prints back as it was written, whatever it was, and
/// what every diagram shares — front matter, directives, comments, the header, the accessibility lines — is read into
/// its parts.
///
/// <para>
/// The constructs include what nobody means to write, because a block is read on every keystroke and most of what it
/// is handed is half-written.
/// </para>
/// </summary>
[TestClass]
[CoversNode("mermaid-block-ast")]
public class MermaidParserTests
{
    [TestMethod]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in MermaidConstructs.Blocks)
            Assert.AreEqual(source, MermaidParser.Parse(source).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in MermaidConstructs.Blocks)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, MermaidParser.Parse(typed).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in MermaidConstructs.Blocks)
            foreach (var place in MermaidParser.Parse(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= source.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {source.Length}");

                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
    }

    [TestMethod]
    public void EveryLineIsALineOfTheBlock_AndFrontMatterHoldsItsOwn()
    {
        var tree = MermaidParser.Parse("---\ntitle: T\n---\npie\n  \"A\" : 1");

        Assert.AreEqual(MermaidKinds.Block, tree.Kind);
        CollectionAssert.AreEqual(
            new[] { MermaidKinds.FrontMatter, MermaidKinds.Line, MermaidKinds.Line },
            tree.Children.Select(child => child.Kind).ToArray());

        var frontMatter = tree.Children[0];
        Assert.AreEqual(3, frontMatter.Children.Count, "the two fences and the line between them");
        Assert.IsTrue(frontMatter.Children.All(line => line.Kind == MermaidKinds.Line));
    }

    [TestMethod]
    public void AFrontMatterFieldIsItsKeyItsColonAndItsValue_AndItsIndentIsTheLines()
    {
        var tree = MermaidParser.Parse("---\ntitle: My Chart\nconfig:\n  theme: forest\n---\npie");
        var fields = Nodes(tree, MermaidKinds.Field).ToList();

        Assert.AreEqual(3, fields.Count);
        Assert.AreEqual("title", fields[0].Part(Roles.Name)?.Text);
        Assert.AreEqual("My Chart", fields[0].Part(MermaidRoles.Value)?.Text);
        Assert.IsNull(fields[1].Part(MermaidRoles.Value), "config: has nothing after its colon");
        Assert.AreEqual("forest", fields[2].Part(MermaidRoles.Value)?.Text);

        var nested = tree.SelfAndDescendants().Single(node => node.Kind == MermaidKinds.Line && node.Children.Any(child => child == fields[2]));
        Assert.AreEqual(Kinds.Space, nested.Children[0].Kind, "a nested field's line starts with its indent");
    }

    [TestMethod]
    public void FrontMatterThatIsNotAFieldIsHeldAsWritten_WithoutComplaint()
    {
        var tree = MermaidParser.Parse("---\n# settings\nconfig:\n  cScale:\n    - red\n---\nradar-beta");

        Assert.AreEqual("- red", Nodes(tree, MermaidKinds.Yaml).Single().Text);
        Assert.AreEqual("# settings", Nodes(tree, Kinds.Comment).Single().Text);
        Assert.IsFalse(tree.SelfAndDescendants().Any(node => node.Trouble is not null));
    }

    [TestMethod]
    public void FrontMatterNeverClosedOpensNothing_AndItsFenceIsHeldWithTheReason()
    {
        var tree = MermaidParser.Parse("---\nconfig:\npie\n");

        Assert.IsFalse(Nodes(tree, MermaidKinds.FrontMatter).Any());

        var header = Nodes(tree, MermaidKinds.Header).Single();
        Assert.AreEqual("---", header.Children.Single().Text);
        StringAssert.Contains(header.Children.Single().Trouble, "front matter");
    }

    [TestMethod]
    public void TheHeaderIsItsKeywordAndWhatFollowsIt()
    {
        var header = Nodes(MermaidParser.Parse("%% note\n\nxychart-beta horizontal\n  bar [1, 2]"), MermaidKinds.Header).Single();

        Assert.AreEqual("xychart-beta", header.Part(Roles.Name)?.Text);
        Assert.AreEqual("horizontal", header.Part(MermaidRoles.Arguments)?.Text);
        Assert.IsNull(header.Part(Roles.Name)!.Trouble);
    }

    [TestMethod]
    public void AKeywordStopsWherePunctuationStarts()
    {
        Assert.AreEqual("gitGraph", Keyword("gitGraph:\n  commit"));
        Assert.AreEqual("graph", Keyword("graph TD;"));
        Assert.AreEqual("stateDiagram-v2", Keyword("stateDiagram-v2"));
    }

    [TestMethod]
    public void AKeywordNamingNoTypeSaysSo()
    {
        var keyword = Nodes(MermaidParser.Parse("flowchart-elk TD"), MermaidKinds.Keyword).Single();
        StringAssert.Contains(keyword.Trouble, "'flowchart-elk' is not a Mermaid diagram type");
    }

    [TestMethod]
    public void AHeaderStartingWithNoKeywordIsStillTheHeader_HeldWithTheReason()
    {
        var header = Nodes(MermaidParser.Parse("123\n  a --> b"), MermaidKinds.Header).Single();

        Assert.IsNull(header.Part(Roles.Name));
        Assert.AreEqual(Kinds.Verbatim, header.Children.Single().Kind);
        Assert.IsNotNull(header.Children.Single().Trouble);
    }

    [TestMethod]
    public void EveryLineAfterTheHeaderIsAStatement_UnlessEveryDiagramReadsIt()
    {
        var tree = MermaidParser.Parse("flowchart TD\n  A --> B\n  %% a comment\n\n  B --> C");

        CollectionAssert.AreEqual(new[] { "A --> B", "B --> C" }, Nodes(tree, MermaidKinds.Statement).Select(node => node.Text).ToArray());
    }

    [TestMethod]
    public void ADirectiveIsOnePiece_HoweverManyLinesItTakes()
    {
        var tree = MermaidParser.Parse("%%{\n  init: {\n    \"theme\": \"forest\"\n  }\n}%%\nflowchart LR\n  a --> b");

        var directive = Nodes(tree, MermaidKinds.Directive).Single();
        Assert.AreEqual("%%{", directive.Part(Roles.Open)?.Text);
        Assert.AreEqual("}%%", directive.Part(Roles.Close)?.Text);
        StringAssert.Contains(directive.Part(Roles.Body)?.Text, "\"theme\": \"forest\"");

        Assert.AreEqual("flowchart", Keyword(tree), "the lines inside a directive are not the header");
    }

    [TestMethod]
    public void ADirectiveNeverClosedIsHeldWithTheReason_AndTheNextLineIsStillTheHeader()
    {
        var tree = MermaidParser.Parse("%%{init: {\"theme\": \"dark\"}\ngraph TD");

        var held = tree.SelfAndDescendants().Single(node => node.Trouble is not null);
        StringAssert.Contains(held.Trouble, "never closed");
        Assert.AreEqual("graph", Keyword(tree));
    }

    [TestMethod]
    public void AnAccessibleTitleAndDescriptionAreTheirNameAndTheirText()
    {
        var lines = Nodes(MermaidParser.Parse("graph LR\n  accTitle: Big decisions\n  accDescr :  Bob's burger stand  \n  a --> b"),
                          MermaidKinds.Accessibility).ToList();

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("accTitle", lines[0].Part(Roles.Name)?.Text);
        Assert.AreEqual("Big decisions", lines[0].Part(MermaidRoles.Value)?.Text);
        Assert.AreEqual("accDescr", lines[1].Part(Roles.Name)?.Text);
        Assert.AreEqual("Bob's burger stand", lines[1].Part(MermaidRoles.Value)?.Text);
    }

    [TestMethod]
    public void ADescriptionInBracesIsOnePiece_HoweverManyLinesItTakes()
    {
        var tree = MermaidParser.Parse("graph LR\n  accDescr {\n    Several\n    lines\n  }\n  a --> b");

        var description = Nodes(tree, MermaidKinds.Accessibility).Single();
        Assert.AreEqual("Several\n    lines", description.Part(MermaidRoles.Value)?.Text);
        CollectionAssert.AreEqual(new[] { "a --> b" }, Nodes(tree, MermaidKinds.Statement).Select(node => node.Text).ToArray());
    }

    [TestMethod]
    public void AWordThatOnlyStartsLikeAnAccessibilityLineIsTheDiagrams()
    {
        var tree = MermaidParser.Parse("graph LR\n  accTitleX --> b\n  accTitle\n  acctitle: lower");

        Assert.IsFalse(Nodes(tree, MermaidKinds.Accessibility).Any(), "Mermaid's keywords are case-sensitive");
        Assert.AreEqual(3, Nodes(tree, MermaidKinds.Statement).Count());
    }

    [TestMethod]
    public void BeforeTheHeaderAnAccessibilityLineIsTheHeader()
    {
        // Mermaid names the diagram first; anything else in that place is a header naming no type.
        var tree = MermaidParser.Parse("accTitle: too soon\npie");

        Assert.IsFalse(Nodes(tree, MermaidKinds.Accessibility).Any());
        Assert.AreEqual("accTitle", Keyword(tree));
    }

    [TestMethod]
    public void CleanBlocksHaveNothingToSay()
    {
        foreach (var source in new[]
                 {
                     "flowchart TD\n  A --> B",
                     "---\ntitle: T\nconfig:\n  theme: dark\n---\n%% c\n%%{init: {}}%%\npie title P\n  accTitle: t\n  accDescr { d }\n  \"A\" : 1",
                     "",
                     "  \n",
                 })
            Assert.IsFalse(MermaidParser.Parse(source).SelfAndDescendants().Any(node => node.Trouble is not null), source);
    }

    private static string? Keyword(string source) => Keyword(MermaidParser.Parse(source));

    private static string? Keyword(ContentNode tree) => Nodes(tree, MermaidKinds.Keyword).SingleOrDefault()?.Text;

    private static IEnumerable<ContentNode> Nodes(ContentNode tree, string kind) =>
        tree.SelfAndDescendants().Where(node => node.Kind == kind);
}
