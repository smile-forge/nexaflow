using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// A <c>mermaid</c> block read back from its tree: which diagram the header names, and the front matter's title,
/// config and where the diagram starts — the answers every diagram's renderer is handed.
/// </summary>
[TestClass]
[CoversNode("mermaid-block-ast")]
public class MermaidBlockTests
{
    private static readonly (string Keyword, MermaidDiagram Diagram)[] Keywords =
    [
        ("flowchart", MermaidDiagram.Flowchart),
        ("graph", MermaidDiagram.Flowchart),
        ("pie", MermaidDiagram.Pie),
        ("quadrantChart", MermaidDiagram.Quadrant),
        ("sequenceDiagram", MermaidDiagram.Sequence),
        ("gantt", MermaidDiagram.Gantt),
        ("gitGraph", MermaidDiagram.GitGraph),
        ("mindmap", MermaidDiagram.Mindmap),
        ("stateDiagram", MermaidDiagram.State),
        ("stateDiagram-v2", MermaidDiagram.State),
        ("classDiagram", MermaidDiagram.Class),
        ("classDiagram-v2", MermaidDiagram.Class),
        ("requirementDiagram", MermaidDiagram.Requirement),
        ("kanban", MermaidDiagram.Kanban),
        ("xychart", MermaidDiagram.XyChart),
        ("xychart-beta", MermaidDiagram.XyChart),
        ("radar-beta", MermaidDiagram.Radar),
        ("ishikawa-beta", MermaidDiagram.Ishikawa),
        ("sankey-beta", MermaidDiagram.Sankey),
        ("erDiagram", MermaidDiagram.Er),
        ("venn-beta", MermaidDiagram.Venn),
        ("cynefin-beta", MermaidDiagram.Cynefin),
        ("architecture-beta", MermaidDiagram.Architecture),
        ("swimlane-beta", MermaidDiagram.Swimlane),
        ("timeline", MermaidDiagram.Timeline),
        ("journey", MermaidDiagram.Journey),
        ("block-beta", MermaidDiagram.Block),
        ("C4Context", MermaidDiagram.C4),
        ("C4Container", MermaidDiagram.C4),
        ("C4Component", MermaidDiagram.C4),
        ("C4Dynamic", MermaidDiagram.C4),
        ("C4Deployment", MermaidDiagram.C4),
        ("C4Sequence", MermaidDiagram.C4Sequence),
        ("PIE", MermaidDiagram.Pie),
        ("flowchart-elk", MermaidDiagram.Unknown),
        ("piechart", MermaidDiagram.Unknown),
    ];

    [TestMethod]
    public void EveryKeywordNamesItsDiagram()
    {
        foreach (var (keyword, diagram) in Keywords)
            Assert.AreEqual(diagram, MermaidBlock.Read($"{keyword} rest\n  a").Diagram, keyword);
    }

    [TestMethod]
    public void TheHeaderIsFoundPastFrontMatterCommentsAndDirectives()
    {
        const string source = "---\ntitle: T\n---\n\n%% a note\n%%{\n  init: {}\n}%%\nsequenceDiagram\n  A->>B: hi";

        var block = MermaidBlock.Read(source);
        Assert.AreEqual(MermaidDiagram.Sequence, block.Diagram);
        Assert.AreEqual("sequenceDiagram", block.Keyword?.Text);
        Assert.AreEqual(source.IndexOf("sequenceDiagram", StringComparison.Ordinal), block.Keyword!.Start);
    }

    [TestMethod]
    public void NoHeaderIsAFlowchart_AndAHeaderNamingNothingIsUnknown()
    {
        Assert.AreEqual(MermaidDiagram.Flowchart, MermaidBlock.Read("").Diagram);
        Assert.AreEqual(MermaidDiagram.Flowchart, MermaidBlock.Read("%% only a comment\n\n").Diagram);
        Assert.AreEqual(MermaidDiagram.Unknown, MermaidBlock.Read("wibble\n  a").Diagram);
        Assert.AreEqual(MermaidDiagram.Unknown, MermaidBlock.Read("123\n  a").Diagram);
        Assert.AreEqual(MermaidDiagram.Unknown, MermaidBlock.Read("---\nconfig:\npie\n").Diagram, "a fence never closed is in the header's place");
    }

    [TestMethod]
    public void TheBodyIsWhatFollowsTheFrontMatter()
    {
        var block = MermaidBlock.Read("---\nconfig:\n  theme: forest\n---\npie\n  \"A\" : 1\n");

        Assert.AreEqual("pie\n  \"A\" : 1\n", block.Body);
        Assert.AreEqual(block.Source.Length - block.Body.Length, block.BodyStart);
    }

    [TestMethod]
    public void WithNoFrontMatterTheBodyIsTheBlock()
    {
        foreach (var source in new[] { "pie\n  \"A\" : 1\n", "---\nconfig:\npie\n", "" })
        {
            var block = MermaidBlock.Read(source);
            Assert.AreEqual(source, block.Body, "a fence never closed is not front matter");
            Assert.AreEqual(0, block.BodyStart);
            Assert.IsNull(block.Config);
        }
    }

    [TestMethod]
    public void TheTitleIsTheTopLevelOne_WithoutItsQuotes()
    {
        Assert.AreEqual("My Chart", MermaidBlock.Read("---\ntitle: My Chart\nconfig:\n  theme: dark\n---\npie\n").TitleText);
        Assert.AreEqual("Quoted", MermaidBlock.Read("\n---\ntitle: \"Quoted\"\n---\npie").TitleText);
        Assert.AreEqual("Single", MermaidBlock.Read("---\nTitle: 'Single'\n---\npie").TitleText);
        Assert.IsNull(MermaidBlock.Read("---\nconfig:\n  title: nested\n---\npie\n").TitleText);
        Assert.IsNull(MermaidBlock.Read("---\ntitle: \"\"\n---\npie").TitleText);
        Assert.IsNull(MermaidBlock.Read("---\ntitle:\n---\npie").TitleText);
        Assert.IsNull(MermaidBlock.Read("pie title Inline").TitleText, "a title on the header is the diagram's own");
    }

    [TestMethod]
    public void TheTitleIsAPartOfTheSource()
    {
        const string source = "---\ntitle: \"My Chart\"\n---\npie";
        var title = MermaidBlock.Read(source).Title!;

        Assert.AreEqual("\"My Chart\"", source.Substring(title.Start, title.Length));
    }

    [TestMethod]
    public void TheConfigIsEverythingBetweenTheFences()
    {
        Assert.AreEqual("title: T\nconfig:\n  theme: dark",
                        MermaidBlock.Read("---\ntitle: T\nconfig:\n  theme: dark\n---\npie").Config);
        Assert.AreEqual("", MermaidBlock.Read("---\n---\npie").Config);
        Assert.AreEqual("a: 1\r", MermaidBlock.Read("---\r\na: 1\r\n---\r\npie").Config,
                        "only the last line's newline goes; every other character of what was written stays");
    }

    [TestMethod]
    public void TheAccessibleTitleAndDescriptionAreRead()
    {
        var block = MermaidBlock.Read("graph LR\n  accTitle: Decisions\n  accDescr {\n    Bob's stand\n  }\n  a --> b");

        Assert.AreEqual("Decisions", block.AccessibleTitle?.Text);
        Assert.AreEqual("Bob's stand", block.AccessibleDescription?.Text);
        CollectionAssert.AreEqual(new[] { "a --> b" }, block.Statements.Select(part => part.Text).ToArray());
    }
}
