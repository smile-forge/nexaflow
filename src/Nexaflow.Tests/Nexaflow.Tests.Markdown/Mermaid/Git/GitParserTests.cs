using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Git;
using Nexaflow.Markdown.Mermaid.Git.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Git;

/// <summary>
/// What <see cref="GitParser"/> reads a <c>gitGraph</c> block into: its commits, branches, checkouts, merges and cherry-picks with the options
/// each writes — and what is written instead of any of that, held with the reason.
/// The header is optional, since a <c>gitGraph</c> fence already says what the block is.
/// </summary>
[TestClass]
[CoversNode("gitgraph-ast")]
public class GitParserTests : ContentParserContract
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

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => GitParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);


    protected override IEnumerable<string> DocumentedBlocks =>
    [
        "gitGraph\n  commit id: 'A' tag: 'v1.0'\n  branch 'feature one'\n  cherry-pick id: 'A'",
        "gitGraph\n  commit id: \"a\\\"b\" msg: \"two\nlines\"\n  branch \"say \\\"hi\\\"\"\n  checkout 'say \"hi\"'\n  commit",
        "gitGraph\n  commit \"Initial commit\"\n  commit msg: \"fix\"",
        History,
        Kept,
        "gitGraph LR:\n   commit\n   branch develop order: 2\n   commit\n   checkout main\n   merge develop tag: \"release\"",
        "gitGraph\n   commit id: \"Alpha\"\n   branch develop\n   commit id: \"Beta\"\n   checkout main\n   cherry-pick id: \"Beta\"",
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
        ("a cherry-pick of a commit on this branch", "gitGraph\n   commit id: \"A\"\n   cherry-pick id: \"A\""),
        ("a cherry-pick with nothing committed to pick onto", "gitGraph\n   branch develop\n   commit id: \"A\"\n   checkout main\n   cherry-pick id: \"A\""),
        ("a cherry-pick of a merge saying nothing of its parents", "gitGraph\n   commit id: \"A\"\n   branch develop\n   commit id: \"B\"\n   checkout main\n   merge develop id: \"M\"\n   branch release\n   cherry-pick id: \"M\""),
        ("an option a commit does not set", "gitGraph\n   commit colour: red"),
        ("a line that is no git graph line", "gitGraph\n   rebase develop"),
        // A gitGraph fence.
        ("no header, as a gitGraph fence holds it", "commit\nbranch develop\ncommit"),
        ("a binding supplying lines", "gitGraph\n  commit\n  {{More}}\n"),
    ];

    /// <summary>
    /// The stages a git graph's language offers the engine (<c>Shipped.GitGraph</c>), named again here — what the engine adds for every
    /// language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes) =>
    [
        new ResolveGraph(GitConfig.Read(MermaidBlock.Of(tree).Config)),
        .. holes ? [new WithHoles(GitParser.Holds)] : Array.Empty<IAstStage>(),
    ];
    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = GitParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }
    [TestMethod]
    public void TheHeaderIsOptional_SinceAGitGraphFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("commit\nbranch develop\ncommit");

        Assert.AreEqual(GitParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(2, tree.SelfAndDescendants().OfType<GitCommitNode>().Count(), "the first line is a commit, not a header gone wrong");
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
    }
    [TestMethod]
    public void TheDocumentedGraphsLinesAreEachRead()
    {
        var tree = Staged(History);

        Assert.AreEqual(6, Nodes(tree, GitKinds.Commit).Count);
        Assert.AreEqual(1, Nodes(tree, GitKinds.Branch).Count);
        Assert.AreEqual(2, Nodes(tree, GitKinds.Checkout).Count);
        Assert.AreEqual(1, Nodes(tree, GitKinds.Merge).Count);
    }

    [TestMethod]
    public void EveryOptionWrittenAfterACommitIsReadAsItsOwn()
    {
        var properties = Staged(Kept).SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Property).ToList();

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
                     ("gitGraph\n   commit id: \"A\"\n   cherry-pick id: \"A\"", "takes a commit from another branch"),
                     ("gitGraph\n   branch develop\n   commit id: \"A\"\n   checkout main\n   cherry-pick id: \"A\"", "with a commit on it already"),
                     ("gitGraph\n   commit id: \"A\"\n   branch develop\n   commit id: \"B\"\n   checkout main\n   merge develop id: \"M\"\n   branch release\n   cherry-pick id: \"M\"", "which of its parents"),
                     ("gitGraph\n   commit id: \"A\"\n   branch develop\n   commit id: \"B\"\n   checkout main\n   merge develop id: \"M\"\n   branch release\n   cherry-pick id: \"M\" parent: \"Z\"", "is no parent of M"),
                     ("gitGraph\n   rebase develop", "A git graph is commits"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
