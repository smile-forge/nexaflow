using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Git;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>gitGraph</c> block drawn on the shared layout tree: a lane per branch with its label standing for the line making
/// it, a commit standing for its own line, and a line from every commit to what it follows.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("gitgraph")]
public class GitBuilderTests : MermaidBuilderContract
{
    private const string History =
        "gitGraph\n  commit\n  commit\n  branch develop\n  checkout develop\n  commit\n  checkout main\n  merge develop\n  commit";

    private const string Kept =
        "gitGraph\n  commit id: \"Alpha\"\n  commit id: \"Normal\" tag: \"v1.0.0\"\n  commit id: \"Reverse\" type: REVERSE\n  commit id: \"Highlight\" type: HIGHLIGHT";

    public override MermaidDiagram Diagram => MermaidDiagram.GitGraph;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documented history", History),
        ("commits kept every way", Kept),
        ("a cherry-pick", "gitGraph\n  commit id: \"A\"\n  branch develop\n  commit id: \"B\"\n  checkout main\n  cherry-pick id: \"B\" tag: \"taken\""),
        ("down the page", "gitGraph TB:\n  commit\n  branch develop\n  commit"),
        ("up the page", "gitGraph BT:\n  commit\n  branch develop\n  commit"),
        ("side by side", "---\nconfig:\n  gitGraph:\n    parallelCommits: true\n---\ngitGraph\n  commit\n  branch develop\n  commit\n  commit"),
        ("a history of its own",
            "---\nconfig:\n  gitGraph:\n    mainBranchName: trunk\n    rotateCommitLabel: false\n  themeVariables:\n    git0: \"#4e79a7\"\n    tagLabelColor: \"#ffffff\"\n---\n"
            + "gitGraph\n  commit id: \"Alpha\" tag: \"v1\"\n  branch develop\n  commit"),
        ("nothing shown of its branches or its ids", "---\nconfig:\n  gitGraph:\n    showBranches: false\n    showCommitLabel: false\n---\ngitGraph\n  commit id: \"Alpha\"\n  commit"),
        ("still being written", "gitGraph\n  commit\n  branch \n  checkout "),
        ("what nobody means to write", "gitGraph\n  commit type: SIDEWAYS\n  checkout nowhere\n  cherry-pick id: \"nope\""),
        ("nothing to draw", "gitGraph"),
    ];

    private static Laid Build(string source, double room = 900) =>
        GitBuilder.Build(EditState.For(source), new DiagramLaying(MarkdownPalette.Dark, 1.0, room));

    [TestMethod]
    public void EveryCommitIsDrawnAlongTheLaneOfTheBranchItIsMadeOn() => UiThread.Run(() =>
    {
        var laid = Build(History);
        var commits = Pieces(laid, GitPiece.Commit).Select(commit => Middle(commit.Bounds)).ToList();

        Assert.AreEqual(5, commits.Count, "four commits and the merge");
        Assert.IsTrue(commits[0].X < commits[1].X, "one commit after another");
        Assert.AreEqual(commits[0].Y, commits[1].Y, 0.5, "both on main's lane");
        Assert.IsTrue(commits[2].Y > commits[1].Y, "and develop on a lane of its own");
        Assert.AreEqual(commits[0].Y, commits[3].Y, 0.5, "the merge back on main");
    });

    [TestMethod]
    public void ACommitStandsForItsLine_AndABranchsLabelForTheLineMakingIt() => UiThread.Run(() =>
    {
        var laid = Build(History);

        Assert.AreEqual("branch develop", Written(History, Pieces(laid, GitPiece.Branch)[1].Part));
        Assert.AreEqual("merge develop", Written(History, Pieces(laid, GitPiece.Commit)[3].Part));
        Assert.IsTrue(Pieces(laid, GitPiece.Name).Any(name => name.Words is { Maps: true }), "a branch made is typed into where it is labelled");
    });

    [TestMethod]
    public void EachCommitIsJoinedToWhatItFollows() => UiThread.Run(() =>
    {
        var follows = Pieces(Build(History), GitPiece.Follow);

        // Each commit follows one thing, less the first, which follows nothing; the merge follows two.
        Assert.AreEqual(5, follows.Count);
        Assert.AreEqual("merge develop", Written(History, follows[^2].Part));
    });

    [TestMethod]
    public void RunningDownThePageTheLanesGoAcross_AndUpItTheHistoryTurnsOver() => UiThread.Run(() =>
    {
        var down = Pieces(Build("gitGraph TB:\n  commit\n  commit"), GitPiece.Commit).Select(commit => Middle(commit.Bounds)).ToList();
        var up = Pieces(Build("gitGraph BT:\n  commit\n  commit"), GitPiece.Commit).Select(commit => Middle(commit.Bounds)).ToList();

        Assert.IsTrue(down[0].Y < down[1].Y, "one commit under another");
        Assert.AreEqual(down[0].X, down[1].X, 0.5, "both on the one lane");
        Assert.IsTrue(up[0].Y > up[1].Y, "and the other way up, the first is at the foot");
    });

    [TestMethod]
    public void ATagAndAnIdStandForWhatWritesThem() => UiThread.Run(() =>
    {
        var laid = Build(Kept);

        Assert.AreEqual("\"v1.0.0\"", Written(Kept, Pieces(laid, GitPiece.Tag)[0].Part));
        Assert.AreEqual("\"Alpha\"", Written(Kept, Pieces(laid, GitPiece.Id)[0].Part));
        Assert.AreEqual(4, Pieces(laid, GitPiece.Id).Count, "one under each commit that is named");
    });

    [TestMethod]
    public void AnIdIsTurnedWhereTheFrontMatterAsksForIt_AndReadStraightWhereItDoesNot() => UiThread.Run(() =>
    {
        var turned = Pieces(Build("gitGraph\n  commit id: \"Alpha\""), GitPiece.Id).Single();
        var straight = Pieces(Build("---\nconfig:\n  gitGraph:\n    rotateCommitLabel: false\n---\ngitGraph\n  commit id: \"Alpha\""), GitPiece.Id).Single();

        Assert.IsTrue(turned.SelfAndDescendants().Any(piece => piece.Turned is not null), "turned as Mermaid turns it");
        Assert.IsTrue(straight.SelfAndDescendants().All(piece => piece.Turned is null), "and read straight where nothing asks");
    });

    [TestMethod]
    public void AHighlightedCommitIsSquaredOff_AndAMergeIsRinged() => UiThread.Run(() =>
    {
        var laid = Build(Kept);
        var commits = Pieces(laid, GitPiece.Commit);
        var merge = Pieces(Build(History), GitPiece.Commit)[3];

        Assert.IsTrue(commits[3].Bounds.Width > commits[0].Bounds.Width, "a highlighted commit is drawn bigger");
        Assert.AreEqual(2, merge.Marks.ToArray().OfType<GeometryMark>().Count(), "a merge is a circle and the ring taken out of it");
    });

    [TestMethod]
    public void NothingIsDrawnOfTheBranchesOrTheIdsWhereTheFrontMatterSaysNot() => UiThread.Run(() =>
    {
        var laid = Build("---\nconfig:\n  gitGraph:\n    showBranches: false\n    showCommitLabel: false\n---\ngitGraph\n  commit id: \"Alpha\"");

        Assert.AreEqual(0, Pieces(laid, GitPiece.Branch).Count);
        Assert.AreEqual(0, Pieces(laid, GitPiece.Id).Count);
        Assert.AreEqual(1, Pieces(laid, GitPiece.Commit).Count, "the commit itself is still drawn");
    });

    [TestMethod]
    public void TheColourWrittenForALaneIsWhatItIsDrawnIn() => UiThread.Run(() =>
    {
        var laid = Build("---\nconfig:\n  themeVariables:\n    git0: \"#4e79a7\"\n---\ngitGraph\n  commit");

        Assert.AreEqual(Color.FromRgb(0x4E, 0x79, 0xA7), Fill(Pieces(laid, GitPiece.Commit).Single()));
    });

    [TestMethod]
    public void ACherryPickIsTaggedWithTheCommitItTook_UnlessItIsTaggedItself() => UiThread.Run(() =>
    {
        const string source = "gitGraph\n  commit id: \"A\"\n  branch develop\n  commit id: \"B\"\n  checkout main\n  cherry-pick id: \"B\"";
        var tagged = "gitGraph\n  commit id: \"A\"\n  branch develop\n  commit id: \"B\"\n  checkout main\n  cherry-pick id: \"B\" tag: \"taken\"";

        var tags = Pieces(Build(source), GitPiece.Tag);

        Assert.AreEqual(1, tags.Count);
        Assert.AreEqual("\"B\"", Written(source, tags[0].Part), "the tag stands for the id it took");
        Assert.AreEqual("\"taken\"", Written(tagged, Pieces(Build(tagged), GitPiece.Tag)[0].Part), "and its own tag where it has one");
    });
}
