using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Git;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Git;

/// <summary>
/// A <c>gitGraph</c> block read into the history it writes: the branches in their lanes, and the commits with what each
/// one follows.
/// </summary>
[TestClass]
[CoversNode("gitgraph-ast")]
public class GitGraphTests
{
    [TestMethod]
    public void EachCommitFollowsTheOneBeforeItOnItsBranch()
    {
        var graph = GitGraph.Read(GitGrammarTests.History);
        var commits = graph.Commits;

        Assert.AreEqual(7, commits.Count, "six commits and the merge");
        Assert.AreEqual(0, commits[0].Parents.Count, "nothing comes before the first");
        CollectionAssert.AreEqual(new[] { commits[0].Id }, commits[1].Parents.ToArray());
        CollectionAssert.AreEqual(new[] { commits[1].Id }, commits[2].Parents.ToArray(), "a branch starts where it is made");
    }

    [TestMethod]
    public void AMergeFollowsBothTheBranchItIsOnAndTheOneMergedIn()
    {
        var graph = GitGraph.Read(GitGrammarTests.History);
        var merge = graph.Commits.Single(commit => commit.Merge);

        Assert.AreEqual(2, merge.Parents.Count);
        Assert.AreEqual("main", merge.Branch.Name);
        Assert.AreEqual(graph.Commits[1].Id, merge.Parents[0], "the last commit on main");
        Assert.AreEqual(graph.Commits[3].Id, merge.Parents[1], "and the last on develop");
    }

    [TestMethod]
    public void ACherryPickFollowsTheCommitItTakes()
    {
        var graph = GitGraph.Read("gitGraph\n  commit id: \"A\"\n  branch develop\n  commit id: \"B\"\n  checkout main\n  cherry-pick id: \"B\"");
        var picked = graph.Commits.Single(commit => commit.Picked);

        Assert.AreEqual("main", picked.Branch.Name);
        CollectionAssert.AreEqual(new[] { "A", "B" }, picked.Parents.ToArray());
        Assert.AreNotEqual("B", picked.Id, "the picked commit is one of its own");
    }

    [TestMethod]
    public void ACommitSaysWhatIsWrittenForIt()
    {
        var commits = GitGraph.Read(GitGrammarTests.Kept).Commits;

        Assert.AreEqual("Alpha", commits[0].Id);
        Assert.AreEqual("Alpha", commits[0].Said!.Says);
        Assert.AreEqual("v1.0.0", commits[1].Tag!.Says);
        Assert.AreEqual(GitKept.Reverse, commits[2].Kept);
        Assert.AreEqual(GitKept.Highlight, commits[3].Kept);
        Assert.IsNull(GitGraph.Read("gitGraph\n  commit").Commits[0].Said, "a commit nothing names says nothing under itself");
    }

    [TestMethod]
    public void ABranchTakesTheLaneItAsksFor_AndOtherwiseTheOneItIsMadeIn()
    {
        var made = GitGraph.Read("gitGraph\n  commit\n  branch develop\n  branch release\n  commit").Branches;
        var asked = GitGraph.Read("gitGraph\n  commit\n  branch develop order: 5\n  branch release order: 1\n  commit").Branches;

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, made.Select(branch => branch.Lane).ToArray());
        CollectionAssert.AreEqual(new[] { "main", "develop", "release" }, made.Select(branch => branch.Name).ToArray());
        Assert.AreEqual(2, asked.Single(branch => branch.Name == "develop").Lane, "the lane it asked to be last");
        Assert.AreEqual(1, asked.Single(branch => branch.Name == "release").Lane);
    }

    [TestMethod]
    public void TheFrontMatterNamesTheBranchEverythingStartsOn_AndMaySetItsLane()
    {
        var graph = GitGraph.Read("---\nconfig:\n  gitGraph:\n    mainBranchName: trunk\n    mainBranchOrder: 2\n---\ngitGraph\n  commit\n  branch develop\n  commit");

        Assert.AreEqual("trunk", graph.Commits[0].Branch.Name);
        Assert.AreEqual(1, graph.Branches.Single(branch => branch.Name == "trunk").Lane, "after the branch that asks for nothing");
        Assert.IsTrue(graph.Config.ShowBranches);
        Assert.IsTrue(graph.Config.RotateCommitLabel);
    }

    [TestMethod]
    public void CommitsRunOneAfterAnother_OrSideBySideWhereTheFrontMatterAsks()
    {
        const string source = "gitGraph\n  commit\n  branch develop\n  commit\n  commit";
        var along = GitGraph.Read(source).Commits.Select(commit => commit.Position).ToArray();
        var beside = GitGraph.Read("---\nconfig:\n  gitGraph:\n    parallelCommits: true\n---\n" + source).Commits.Select(commit => commit.Position).ToArray();

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, along);
        CollectionAssert.AreEqual(new[] { 0, 0, 1 }, beside, "every branch keeping its own count");
    }

    [TestMethod]
    public void WhichWayItRunsIsWhatTheHeaderAsks()
    {
        Assert.AreEqual(GitWay.LeftRight, GitGraph.Read("gitGraph\n  commit").Way);
        Assert.AreEqual(GitWay.LeftRight, GitGraph.Read("gitGraph LR:\n  commit").Way);
        Assert.AreEqual(GitWay.TopBottom, GitGraph.Read("gitGraph TB:\n  commit").Way);
        Assert.AreEqual(GitWay.BottomTop, GitGraph.Read("gitGraph BT:\n  commit").Way);
    }

    [TestMethod]
    public void ABlockWithNoCommitsHasNothingToDraw()
    {
        Assert.IsTrue(GitGraph.Read("gitGraph").Empty);
        Assert.IsTrue(GitGraph.Read("gitGraph\n  branch develop").Empty);
        Assert.IsFalse(GitGraph.Read("gitGraph\n  commit").Empty);
    }

    [TestMethod]
    public void ACherryPickSaysWhichCommitItTook()
    {
        var picked = GitGraph.Read("gitGraph\n  commit id: \"A\"\n  branch develop\n  commit id: \"B\"\n  checkout main\n  cherry-pick id: \"B\"")
            .Commits.Single(commit => commit.Picked);

        Assert.AreEqual("B", picked.Taken!.Says, "which is what it is tagged with where nothing else tags it");
        Assert.IsNull(picked.Said, "and it says nothing of its own under itself");
    }

    [TestMethod]
    public void APickOfAMergeNamesWhichParentItTakes()
    {
        const string source = "gitGraph\n  commit id: \"A\"\n  branch develop\n  commit id: \"B\"\n  checkout main\n  merge develop id: \"M\"\n  branch release\n  cherry-pick id: \"M\" parent: \"A\"";

        Assert.AreEqual(0, MermaidParser.Read(source).SelfAndDescendants().Count(node => node.Trouble is not null), "a parent of the merge is no complaint");
        Assert.AreEqual("M", GitGraph.Read(source).Commits.Single(commit => commit.Picked).Taken!.Says);
    }
}
