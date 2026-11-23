using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Cynefin;
using Nexaflow.Markdown.Mermaid.Cynefin.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Cynefin;

/// <summary>
/// What <see cref="CynefinParser"/> reads a <c>cynefin-beta</c> block into: the domains it opens, the items sitting in each, and the transitions
/// between them — and what is written instead of any of that, held with the reason. The header is optional, since a
/// <c>cynefin</c> fence already says what the block is.
/// </summary>
[TestClass]
[CoversNode("cynefin-ast")]
public class CynefinParserTests : ContentParserContract
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

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => CynefinParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Sense,
        "cynefin-beta\n  complex \"Probe\" \"Sense\"\n  clear\n    \"One\" \"Two\"",
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
        ("the header with its colon", "cynefin-beta:\n  complex\n    \"One\""),
        ("items in single quotes", "cynefin-beta\n  complex\n    'Run an experiment'"),
        ("quotes a backslash keeps", "cynefin-beta\n  complex\n    \"say \\\"hi\\\"\"\n    'it\\'s'"),
        ("an item across a line ending", "cynefin-beta\n  complex\n    \"Two\nlines\"\n    \"Three\""),
        ("a label across a line ending", "cynefin-beta\n  complex --> clear : \"Pattern\nfound\"\n  complex"),
        ("a comment written inside an item", "cynefin-beta\n  complex\n    \"50%% done\""),
        ("an apostrophe in a bare item", "cynefin-beta\n  complex\n    Don't panic\n    Can't stop"),
        ("no header, as a cynefin fence holds it", "complex\n  \"One\"\nclear\n  \"Two\""),
        ("a binding supplying items", "cynefin-beta\n  complex\n    {{Items}}\n"),
    ];

    /// <summary>
    /// The stages a Cynefin framework's language offers the engine (<c>Shipped.Cynefin</c>), named again here — what the engine adds
    /// for every language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes) =>
    [
        new ResolveDomains(),
        new WithConfig<CynefinConfig>(CynefinConfig.Read(MermaidBlock.Of(tree).Config)),
        .. holes ? [new WithHoles(CynefinParser.Holds)] : Array.Empty<IAstStage>(),
    ];

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = CynefinParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceACynefinFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("complex\n  \"One\"\nclear\n  \"Two\"");

        Assert.AreEqual(CynefinParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(2, Nodes(tree, CynefinKinds.Domain).Count, "the first line opens a domain, not a header gone wrong");
        Assert.AreEqual(0, Trouble("complex\n  \"One\"\nclear\n  \"Two\"").Count);
    }

    [TestMethod]
    public void AStringIsReadInEitherQuote_ABackslashKeepingWhatWouldEndIt()
    {
        var items = Nodes(Staged("cynefin-beta\n  complex\n    \"say \\\"hi\\\"\"\n    'it\\'s'"), CynefinKinds.Item);

        CollectionAssert.AreEqual(new[] { "say \"hi\"", "it's" }, items.Select(Says).ToArray());
        Assert.AreEqual(2, items[0].SelfAndDescendants().Count(node => node.Kind == CynefinKinds.Escape), "each kept quote a piece of its own");
    }

    [TestMethod]
    public void AStringRunsAcrossALineEndingToItsClosingQuote_AndTheItemIsOneLineOfTheTree()
    {
        const string source = "cynefin-beta\n  complex\n    \"Two\nlines\"\n    \"Three\"";

        Assert.AreEqual(2, Nodes(Staged(source), CynefinKinds.Item).Count);
        Assert.AreEqual("Two lines", Says(Nodes(Staged(source), CynefinKinds.Item)[0]), "a line ending is drawn as a space");
        Assert.AreEqual(0, Trouble(source).Count, string.Join(" | ", Trouble(source)));
    }

    [TestMethod]
    public void AnApostropheInABareItemIsACharacterLikeAnyOther()
    {
        // A string opens only where Mermaid's grammar puts one, so a bare item's apostrophe carries it no further than its line.
        const string source = "cynefin-beta\n  complex\n    Don't panic\n    Can't stop";

        CollectionAssert.AreEqual(new[] { "Don't panic", "Can't stop" }, Nodes(Staged(source), CynefinKinds.Item).Select(item => item.Print().Trim()).ToArray());
    }

    [TestMethod]
    public void WrittenIntoAString_WhatWouldEndItIsKept()
    {
        Assert.AreEqual("say #quot;hi#quot; a\\\\b", CynefinParser.Escaped("say \"hi\" a\\b", '"'), "a quote as its entity code, as in any diagram, and a backslash by another");
        Assert.AreEqual("it\\'s \"so\"", CynefinParser.Escaped("it's \"so\"", '\''), "in single quotes, that quote by a backslash");
    }

    /// <summary>What an item says, each escape and line ending in it as what it stands for.</summary>
    private static string Says(ContentNode item)
    {
        var said = item.Inner(Kinds.Words)!;
        return said.IsLeaf ? said.Text : string.Concat(said.Children.Select(piece => CynefinStandIns.Decoded(piece) ?? piece.Text));
    }

    [TestMethod]
    public void TheDocumentedDiagramsLinesAreEachRead()
    {
        var tree = Staged(Sense);

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
        var item = Nodes(Staged("cynefin-beta\n  complex\n    complex enough to probe"), CynefinKinds.Item).Single();

        Assert.AreEqual("complex enough to probe", item.Print());
        Assert.AreEqual(0, Trouble("cynefin-beta\n  complex\n    complex enough to probe").Count);
    }

    [TestMethod]
    public void AnArrowHardAgainstTheDomainsWordIsStillATransition()
    {
        const string source = "cynefin-beta\n  complex-->clear:\"Found\"";
        var move = Nodes(Staged(source), CynefinKinds.Move).Single();

        Assert.AreEqual("complex-->clear:\"Found\"", move.Print());
        Assert.AreEqual(0, Trouble(source).Count);
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];

    [TestMethod]
    public void ItemsMayStandOnTheirDomainsLine_AndSeveralOnOneLine()
    {
        const string Source = "cynefin-beta\n  complex \"Probe\" \"Sense\"\n  clear\n    \"One\" 'Two' %% both";
        var tree = Staged(Source);

        Assert.AreEqual(2, Nodes(tree, CynefinKinds.Domain).Count);
        CollectionAssert.AreEqual(new[] { "Probe", "Sense", "One", "Two" }, Nodes(tree, CynefinKinds.Item).Select(Says).ToArray());
        Assert.AreEqual(0, Trouble(Source).Count);
    }
}

