using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Cynefin;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Cynefin;

/// <summary>
/// What a <c>cynefin-beta</c> block is read into: the domains it opens, the items sitting in each, and the transitions
/// between them — and what is written instead of any of that, held with the reason.
/// </summary>
[TestClass]
[CoversNode("cynefin-ast")]
public class CynefinGrammarTests : MermaidGrammarContract
{
    /// <summary>The diagram the documentation opens with.</summary>
    public const string Sense =
        """
        cynefin-beta
            title Making sense of the work
            complex
                "Investigate root cause"
                "Run a safe-to-fail experiment"
            complicated
                "Consult an expert"
                "Analyse the trade-offs"
            clear
                "Apply the standard runbook"
            chaotic
                "Stop the bleeding"
            confusion
                "Unclassified incident A"
                "Unclassified incident B"
                "Unclassified incident C"
                "Unclassified incident D"
            chaotic --> complex : "Stabilised"
            complex --> complicated : "Pattern found"
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Cynefin;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Sense,
        "---\nconfig:\n  cynefin:\n    showDomainDescriptions: true\n  themeVariables:\n    cynefin:\n      complexBg: \"#4e79a7\"\n      clearBg: \"#59a14f\"\n---\ncynefin-beta\n    complex\n        \"Emergent practice\"\n    clear\n        \"Best practice\"",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("items bare and in quotes", "cynefin-beta\n  complex\n    Investigate root cause\n    \"Run an experiment\""),
        ("a transition with no label", "cynefin-beta\n  complex --> clear"),
        ("a transition labelled bare", "cynefin-beta\n  complex --> clear : Pattern found"),
        ("no space anywhere", "cynefin-beta\n  complex-->clear:\"Found\""),
        ("a domain opened twice", "cynefin-beta\n  complex\n    \"One\"\n  clear\n    \"Two\"\n  complex\n    \"Three\""),
        ("a line starting with a domain's word and saying more", "cynefin-beta\n  complex\n    complex enough to probe"),
        ("a comment and a blank line", "cynefin-beta\n\n  %% the domains\n  complex\n    \"One\" %% first"),
        ("accessibility lines", "cynefin-beta\n  accTitle: Sense making\n  accDescr: By domain\n  complex\n    \"One\""),
        ("written on Windows", "cynefin-beta\r\n  complex  \r\n    \"One\"\r\n"),
        // Half written.
        ("a transition still to name where it goes", "cynefin-beta\n  complex --> "),
        ("a transition still to say what it is", "cynefin-beta\n  complex --> clear : "),
        ("an item still to say anything", "cynefin-beta\n  complex\n    \"\""),
        ("nothing but a domain", "cynefin-beta\n  confusion"),
        // What nobody means to write.
        ("a transition to no domain", "cynefin-beta\n  complex --> nowhere"),
        ("a transition saying more than its label", "cynefin-beta\n  complex --> clear \"Found\""),
        ("an item never closed", "cynefin-beta\n  complex\n    \"Unclosed"),
        ("an item in no domain", "cynefin-beta\n  \"Stranded\""),
        ("nothing but the keyword", "cynefin-beta"),
    ];

    [TestMethod]
    public void TheDocumentedDiagramsLinesAreEachRead()
    {
        var tree = MermaidStaged.Read(Sense);

        Assert.AreEqual(5, Nodes(tree, CynefinKinds.Domain).Count);
        Assert.AreEqual(10, Nodes(tree, CynefinKinds.Item).Count);
        Assert.AreEqual(2, Nodes(tree, CynefinKinds.Move).Count);
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[] { "cynefin-beta\n  complex", "cynefin-beta\n  complex --> ", "cynefin-beta\n  complex --> clear : ", "cynefin-beta\n  complex\n    \"\"" })
            Assert.AreEqual(0, Trouble(source).Count, $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("cynefin-beta\n  complex --> nowhere", "A transition goes"),
                     ("cynefin-beta\n  complex --> clear \"Found\"", "A transition goes"),
                     ("cynefin-beta\n  complex\n    \"Unclosed", "never closed"),
                     ("cynefin-beta\n  \"Stranded\"", "sits in the domain opened above it"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void ALineStartingWithADomainsWordAndSayingMoreIsAnItem()
    {
        var item = Nodes(MermaidStaged.Read("cynefin-beta\n  complex\n    complex enough to probe"), CynefinKinds.Item).Single();

        Assert.AreEqual("complex enough to probe", item.Print());
        Assert.AreEqual(0, Trouble("cynefin-beta\n  complex\n    complex enough to probe").Count);
    }

    [TestMethod]
    public void AnArrowHardAgainstTheDomainsWordIsStillATransition()
    {
        const string source = "cynefin-beta\n  complex-->clear:\"Found\"";
        var move = Nodes(MermaidStaged.Read(source), CynefinKinds.Move).Single();

        Assert.AreEqual("complex-->clear:\"Found\"", move.Print());
        Assert.AreEqual(0, Trouble(source).Count);
    }

    [TestMethod]
    public void ANewLineUnderADomainIsAnItem_AndElsewhereTheDomainToOpen()
    {
        var grammar = new CynefinGrammar();
        var domain = Nodes(MermaidParser.Parse("cynefin-beta\n  complex"), CynefinKinds.Domain).Single();

        Assert.AreEqual(("\"\"", 1), grammar.Blank(domain));
        Assert.AreEqual(("complex", 7), grammar.Blank(null));
    }

    [TestMethod]
    public void AnArrowTypedIntoABareItemPutsItInQuotes()
    {
        const string source = "cynefin-beta\n  complex\n    Investigate";
        var says = ContentReading.Of(MermaidStaged.Read(source)).Root.SelfAndDescendants()
            .First(part => part.Kind == MermaidKinds.Words && part.Text == "Investigate");
        var writing = new CynefinGrammar().Escaping(says, says.End, " --> clear")!.Value;

        Assert.AreEqual("cynefin-beta\n  complex\n    \"Investigate --> clear\"", source[..writing.Start] + writing.Text + source[writing.End..]);
    }

    [TestMethod]
    public void AnItemSaysWhichDomainItSitsIn()
    {
        var items = ContentReading.Of(MermaidStaged.Read(Sense)).Root.SelfAndDescendants()
            .Where(part => part.Kind == CynefinKinds.Item)
            .Select(item => item.Fact(CynefinRoles.In))
            .ToList();

        Assert.AreEqual("complex", items[0]);
        Assert.AreEqual("confusion", items[^1]);
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
