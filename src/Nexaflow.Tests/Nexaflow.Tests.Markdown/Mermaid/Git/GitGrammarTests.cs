using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Git;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Git;

/// <summary>
/// What a <c>gitGraph</c> block is read into: its commits, branches, checkouts, merges and cherry-picks with the options
/// each writes — and what is written instead of any of that, held with the reason.
/// </summary>
[TestClass]
[CoversNode("gitgraph-ast")]
public class GitGrammarTests : MermaidGrammarContract
{
    /// <summary>The graph the Mermaid documentation opens with.</summary>
    public const string History =
        """
        gitGraph
           commit
           commit
           branch develop
           checkout develop
           commit
           commit
           checkout main
           merge develop
           commit
           commit
        """;

    /// <summary>The documentation's commits with ids, tags and types of their own.</summary>
    public const string Kept =
        """
        gitGraph
           commit id: "Alpha"
           commit id: "Normal" tag: "v1.0.0"
           commit id: "Reverse" type: REVERSE
           commit id: "Highlight" type: HIGHLIGHT tag: "8.8.4"
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.GitGraph;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        History,
        Kept,
        "gitGraph LR:\n   commit\n   branch develop order: 2\n   commit\n   checkout main\n   merge develop tag: \"release\"",
        "gitGraph\n   commit id: \"Alpha\"\n   branch develop\n   commit\n   checkout main\n   cherry-pick id: \"Alpha\"",
        "---\nconfig:\n  gitGraph:\n    mainBranchName: trunk\n    showCommitLabel: false\n    rotateCommitLabel: false\n---\ngitGraph\n   commit\n   branch develop\n   commit",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the way it runs, with the colon Mermaid closes its header with", "gitGraph TB:\n   commit"),
        ("nothing but the colon", "gitGraph:\n   commit"),
        ("switch instead of checkout", "gitGraph\n   branch develop\n   switch main\n   commit"),
        ("a branch saying where its lane goes", "gitGraph\n   commit\n   branch develop order: 3\n   commit"),
        ("a branch named as a path", "gitGraph\n   commit\n   branch feature/login\n   commit"),
        ("a merge with an id and a tag of its own", "gitGraph\n   commit\n   branch develop\n   commit\n   checkout main\n   merge develop id: \"joined\" tag: \"v2\""),
        ("a cherry-pick with a parent and a tag", "gitGraph\n   commit id: \"A\"\n   branch develop\n   commit id: \"B\"\n   checkout main\n   cherry-pick id: \"B\" parent: \"A\" tag: \"picked\""),
        ("a comment and a blank line", "gitGraph\n\n   %% the first commit\n   commit %% one"),
        ("accessibility lines", "gitGraph\n   accTitle: A history\n   accDescr: By branch\n   commit"),
        ("written on Windows", "gitGraph\r\n   commit  \r\n   branch develop\r\n"),
        // Half written.
        ("a branch still to name", "gitGraph\n   branch "),
        ("a checkout still to name", "gitGraph\n   checkout "),
        ("a commit still to say anything about itself", "gitGraph\n   commit "),
        ("nothing but the keyword", "gitGraph"),
        // What nobody means to write.
        ("a branch checked out before it is made", "gitGraph\n   commit\n   checkout develop"),
        ("a branch made twice", "gitGraph\n   branch develop\n   branch develop"),
        ("a branch merged into itself", "gitGraph\n   commit\n   branch develop\n   merge develop"),
        ("a commit kept as nothing a git graph keeps", "gitGraph\n   commit type: SIDEWAYS"),
        ("an id given twice", "gitGraph\n   commit id: \"A\"\n   commit id: \"A\""),
        ("a cherry-pick of nothing", "gitGraph\n   commit\n   cherry-pick"),
        ("a cherry-pick of a commit nothing writes", "gitGraph\n   commit\n   cherry-pick id: \"nope\""),
        ("an option a commit does not set", "gitGraph\n   commit colour: red"),
        ("a line that is no git graph line", "gitGraph\n   rebase develop"),
    ];

    [TestMethod]
    public void TheDocumentedGraphsLinesAreEachRead()
    {
        var tree = MermaidParser.Read(History);

        Assert.AreEqual(6, Nodes(tree, GitKinds.Commit).Count);
        Assert.AreEqual(1, Nodes(tree, GitKinds.Branch).Count);
        Assert.AreEqual(2, Nodes(tree, GitKinds.Checkout).Count);
        Assert.AreEqual(1, Nodes(tree, GitKinds.Merge).Count);
    }

    [TestMethod]
    public void EveryOptionWrittenAfterACommitIsReadAsItsOwn()
    {
        var properties = MermaidParser.Read(Kept).SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Property).ToList();

        Assert.AreEqual(8, properties.Count, "four ids, two tags and two types");
        Assert.AreEqual(0, Trouble(Kept).Count);
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[] { "gitGraph\n   branch ", "gitGraph\n   checkout ", "gitGraph\n   commit ", "gitGraph\n   commit", "gitGraph" })
            Assert.AreEqual(0, Trouble(source).Count, $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("gitGraph\n   commit\n   checkout develop", "No branch develop is made above this to check out"),
                     ("gitGraph\n   branch develop\n   branch develop", "A branch is made once"),
                     ("gitGraph\n   commit\n   branch develop\n   merge develop", "merged into another"),
                     ("gitGraph\n   commit type: SIDEWAYS", "NORMAL, REVERSE or HIGHLIGHT"),
                     ("gitGraph\n   commit id: \"A\"\n   commit id: \"A\"", "already taken"),
                     ("gitGraph\n   commit\n   cherry-pick", "names the commit it takes"),
                     ("gitGraph\n   commit\n   cherry-pick id: \"nope\"", "No commit above this has the id nope"),
                     ("gitGraph\n   commit colour: red", "A commit sets"),
                     ("gitGraph\n   rebase develop", "A git graph is commits"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void EachCommitSaysWhichBranchItIsMadeOn()
    {
        var read = ContentReading.Of(MermaidParser.Read(History)).Root;

        CollectionAssert.AreEqual(
            new[] { "main", "main", "develop", "develop", "main", "main" },
            read.SelfAndDescendants().Where(part => part.Kind == GitKinds.Commit).Select(commit => commit.Fact(GitRoles.On)).ToArray());
        Assert.AreEqual("main", read.SelfAndDescendants().Single(part => part.Kind == GitKinds.Merge).Fact(GitRoles.On));
    }

    [TestMethod]
    public void ANewLineIsAnotherCommit()
    {
        var grammar = new GitGrammar();
        var commit = Nodes(MermaidParser.Parse("gitGraph\n   commit"), GitKinds.Commit).Single();

        Assert.AreEqual(("commit", 6), grammar.Blank(commit));
        Assert.AreEqual(("commit", 6), grammar.Blank(null));
    }

    [TestMethod]
    public void ABranchRenamedWhereItIsMadeIsRenamedWhereverItIsCheckedOutOrMerged()
    {
        var branches = new GitGrammar().Names(ContentReading.Of(MermaidParser.Read(History)).Root);

        Assert.AreEqual(1, branches.Count);
        Assert.AreEqual("develop", branches[0].Name);
        Assert.AreEqual(2, branches[0].Uses.Count, "checked out once and merged once");
    }

    [TestMethod]
    public void AQuoteTypedIntoAnIdIsWrittenAsTheEntityCodeForIt()
    {
        const string source = "gitGraph\n   commit id: \"Alpha\"";
        var setting = ContentReading.Of(MermaidParser.Read(source)).Root.SelfAndDescendants()
            .First(part => part.Kind == MermaidKinds.Setting);
        var writing = new GitGrammar().Escaping(setting, setting.End - 1, "\"")!.Value;

        Assert.AreEqual("gitGraph\n   commit id: \"Alpha#quot;\"", source[..writing.Start] + writing.Text + source[writing.End..]);
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. MermaidParser.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
