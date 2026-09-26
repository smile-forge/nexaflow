using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Git;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Git;

/// <summary>
/// What a <c>gitGraph</c> block's stage writes into its tree: the lane each branch takes, and each commit's lane, where it
/// stands along the history and what it follows.
/// </summary>
[TestClass]
[CoversNode("gitgraph-ast")]
public class GitStagesTests
{
    private static List<GitCommitNode> Commits(string source) => [.. MermaidStaged.Read(source).SelfAndDescendants().OfType<GitCommitNode>()];

    /// <summary>Each branch's lane, by its name — the branch everything starts on under the name the front matter gives it.</summary>
    private static Dictionary<string, int> Lanes(string source)
    {
        var tree = (GitBlockNode)MermaidStaged.Read(source);
        var lanes = new Dictionary<string, int> { [tree.Config.MainBranchName] = tree.Main };

        foreach (var made in tree.SelfAndDescendants().OfType<GitBranchNode>())
            lanes[made.Inner(MermaidKinds.Words)!.Text] = made.Lane;

        return lanes;
    }

    [TestMethod]
    public void EachCommitFollowsTheOneBeforeItOnItsBranch()
    {
        var commits = Commits(GitGrammarTests.History);

        Assert.AreEqual(7, commits.Count, "six commits and the merge");
        Assert.AreEqual(0, commits[0].Follows.Count, "nothing comes before the first");
        CollectionAssert.AreEqual(new[] { 0 }, commits[1].Follows.ToArray());
        CollectionAssert.AreEqual(new[] { 1 }, commits[2].Follows.ToArray(), "a branch starts where it is made");
    }

    [TestMethod]
    public void EachCommitIsOnTheLaneOfTheBranchCheckedOutAboveIt()
    {
        var lanes = Lanes(GitGrammarTests.History);

        CollectionAssert.AreEqual(new[] { "main", "main", "develop", "develop", "main", "main", "main" },
                                  Commits(GitGrammarTests.History).Select(commit => lanes.Single(lane => lane.Value == commit.Lane).Key).ToArray());
    }

    [TestMethod]
    public void AMergeFollowsBothTheBranchItIsOnAndTheOneMergedIn()
    {
        var merge = MermaidStaged.Read(GitGrammarTests.History).SelfAndDescendants().OfType<GitCommitNode>().Single(commit => commit.Kind == GitKinds.Merge);

        CollectionAssert.AreEqual(new[] { 1, 3 }, merge.Follows.ToArray(), "the last commit on main, and the last on develop");
        Assert.AreEqual(Lanes(GitGrammarTests.History)["main"], merge.Lane);
    }

    [TestMethod]
    public void ACherryPickFollowsTheCommitItTakes()
    {
        const string source = "gitGraph\n  commit id: \"A\"\n  branch develop\n  commit id: \"B\"\n  checkout main\n  cherry-pick id: \"B\"";
        var picked = Commits(source)[2];

        CollectionAssert.AreEqual(new[] { 0, 1 }, picked.Follows.ToArray());
        Assert.AreEqual(1, picked.Takes);
        Assert.AreEqual(Lanes(source)["main"], picked.Lane);
    }

    [TestMethod]
    public void ABranchTakesTheLaneItAsksFor_AndOtherwiseTheOneItIsMadeIn()
    {
        var made = Lanes("gitGraph\n  commit\n  branch develop\n  branch release\n  commit");
        var asked = Lanes("gitGraph\n  commit\n  branch develop order: 5\n  branch release order: 1\n  commit");

        CollectionAssert.AreEqual(new[] { ("main", 0), ("develop", 1), ("release", 2) }, made.Select(lane => (lane.Key, lane.Value)).ToArray());
        Assert.AreEqual(2, asked["develop"], "the lane it asked to be last");
        Assert.AreEqual(1, asked["release"]);
    }

    [TestMethod]
    public void TheFrontMatterNamesTheBranchEverythingStartsOn_AndMaySetItsLane()
    {
        const string source = "---\nconfig:\n  gitGraph:\n    mainBranchName: trunk\n    mainBranchOrder: 2\n---\ngitGraph\n  commit\n  branch develop\n  commit";
        var block = (GitBlockNode)MermaidStaged.Read(source);

        Assert.AreEqual(1, Lanes(source)["trunk"], "after the branch that asks for nothing");
        Assert.AreEqual(block.Main, Commits(source)[0].Lane);
        Assert.IsTrue(block.Config.ShowBranches);
        Assert.IsTrue(block.Config.RotateCommitLabel);
    }

    [TestMethod]
    public void CommitsRunOneAfterAnother_OrSideBySideWhereTheFrontMatterAsks()
    {
        const string source = "gitGraph\n  commit\n  branch develop\n  commit\n  commit\n  checkout main\n  commit\n  commit";

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, Commits(source).Select(commit => commit.Position).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 1, 2 }, Commits("---\nconfig:\n  gitGraph:\n    parallelCommits: true\n---\n" + source).Select(commit => commit.Position).ToArray(),
                                  "each one past what it follows, as Mermaid places them");
    }

    [TestMethod]
    public void ABranchAskingForNoLaneComesBeforeOneAskingForAny_AsMermaidOrdersThem()
    {
        // Mermaid's own example: main asks for 2, test1 for 3 and test4 for 1; test2 and test3 ask nothing.
        var lanes = Lanes("---\nconfig:\n  gitGraph:\n    mainBranchOrder: 2\n---\ngitGraph\n  commit\n  branch test1 order: 3\n  branch test2\n  branch test3\n  branch test4 order: 1");

        CollectionAssert.AreEqual(new[] { "test2", "test3", "test4", "main", "test1" }, lanes.OrderBy(lane => lane.Value).Select(lane => lane.Key).ToArray());
    }

    [TestMethod]
    public void AnIdWrittenTwiceIsWrong_ButNamesTheNewestCommitGivenIt()
    {
        const string source = "gitGraph\n  commit id: \"A\"\n  branch side\n  commit id: \"B\"\n  checkout main\n  commit id: \"B\"\n  cherry-pick id: \"B\"";
        var commits = Commits(source);
        var lanes = Lanes(source);

        Assert.AreEqual(2, MermaidStaged.Read(source).SelfAndDescendants().Count(node => node.Trouble is not null),
                        "no two commits in git share an id, and so the newest B is on the branch picking it");
        Assert.AreEqual(2, commits[3].Takes, "the id names the newest commit given it");
        Assert.AreEqual(lanes["side"], commits[1].Lane);
        Assert.AreEqual(lanes["main"], commits[2].Lane);
    }

    [TestMethod]
    public void APickOfAMergeNamingOneOfItsParentsIsNoComplaint()
    {
        const string source = "gitGraph\n  commit id: \"A\"\n  branch develop\n  commit id: \"B\"\n  checkout main\n  merge develop id: \"M\"\n  branch release\n  cherry-pick id: \"M\" parent: \"A\"";

        Assert.AreEqual(0, MermaidStaged.Read(source).SelfAndDescendants().Count(node => node.Trouble is not null));
        Assert.AreEqual(2, Commits(source)[3].Takes, "it takes the merge");
    }

    [TestMethod]
    public void EveryCommitPrintsAsItWasWritten() =>
        Assert.AreEqual(GitGrammarTests.History, MermaidStaged.Read(GitGrammarTests.History).Print());
}
