using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Treemap;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Treemap;

/// <summary>
/// What a change to a treemap's boxes is written as: a box split, boxes merged, grouped, let out of a group or moved into one keep
/// what the map adds up to wherever they can, an item goes with everything under it and keeps every character it says but the value a
/// change gives it, and only its indentation is written again — so the map still nests as it is drawn.
/// </summary>
[TestClass]
[CoversNode("treemap-writing")]
public class TreemapWritingTests
{
    private const string Map = "treemap-beta\n\"S\"\n    \"A\": 10\n    \"B\": 20\n\"T\"\n    \"C\": 30";

    [TestMethod, TestCategory("Unit")]
    public void ABoxSplitsIntoTwoHalvesAfterIt_SoWhatItIsInAddsUpToWhatItDid()
    {
        var split = Change(Map, root => TreemapWriting.Split(root, Item(root, "A"))!);

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 5\n    \"\": 5\n    \"B\": 20\n\"T\"\n    \"C\": 30", split.Source);
        Assert.AreEqual(split.Source.IndexOf("\"\": 5", StringComparison.Ordinal) + 1, split.Caret, "between the new box's quotes");

        Assert.AreEqual("treemap-beta\n\"X\": 3\n\"\": 2", Changed("treemap-beta\n\"X\": 5", root => TreemapWriting.Split(root, Item(root, "X"))!),
                        "a whole number into whole numbers, the larger half kept");
        Assert.AreEqual("treemap-beta\n\"X\": 0.5\n\"\": 0.5", Changed("treemap-beta\n\"X\": 1", root => TreemapWriting.Split(root, Item(root, "X"))!),
                        "one in two halves, so neither is drawn as nothing");
        Assert.AreEqual("treemap-beta\n\"X\": 1,501\n\"\": 1,500", Changed("treemap-beta\n\"X\": 3,001", root => TreemapWriting.Split(root, Item(root, "X"))!),
                        "written as the value was, with commas");
        Assert.IsNull(Root(Map) is var root ? TreemapWriting.Split(root, Item(root, "S")) : null, "a group is not split");
    }

    [TestMethod, TestCategory("Unit")]
    public void BoxesMergeIntoTheOneNamed_WorthThemAll()
    {
        var merged = Change(Map, root => TreemapWriting.Merged(root, [Item(root, "A"), Item(root, "B")], Item(root, "B"))!);

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"B\": 30\n\"T\"\n    \"C\": 30", merged.Source);
        Assert.AreEqual(merged.Source.IndexOf("\"B\"", StringComparison.Ordinal) + 2, merged.Caret, "at the end of its label");

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 40\n    \"B\": 20\n\"T\"",
                        Changed(Map, root => TreemapWriting.Merged(root, [Item(root, "A"), Item(root, "C")], Item(root, "A"))!),
                        "from another group too, into the group of the one named");

        var root = Root(Map);
        Assert.IsNull(TreemapWriting.Merged(root, [Item(root, "A"), Item(root, "T")], Item(root, "A")), "a group is not merged");
        Assert.IsNull(TreemapWriting.Merged(root, [Item(root, "A")], Item(root, "A")), "one box is nothing to merge");
    }

    [TestMethod, TestCategory("Unit")]
    public void BoxesPutInAGroupGoInANewOneWhereTheFirstStood_AndABoxOnItsOwnBecomesOne()
    {
        var grouped = Change(Map, root => TreemapWriting.Grouped(root, [Item(root, "A"), Item(root, "B")])!);

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"\"\n        \"A\": 10\n        \"B\": 20\n\"T\"\n    \"C\": 30", grouped.Source);
        Assert.AreEqual(grouped.Source.IndexOf("\"\"", StringComparison.Ordinal) + 1, grouped.Caret, "between the new group's quotes");

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 10\n    \"B\": 20\n\"T\"\n    \"C\"\n        \"\": 30",
                        Changed(Map, root => TreemapWriting.Grouped(root, [Item(root, "C")])!),
                        "called what it was and handing its value to a box in it, so it is drawn the size it was");

        Assert.AreEqual("treemap-beta\n\"\"\n    \"S\"\n        \"A\": 10\n        \"B\": 20\n    \"T\"\n        \"C\": 30",
                        Changed(Map, root => TreemapWriting.Grouped(root, [Item(root, "S"), Item(root, "T")])!), "groups go in with all they hold");
    }

    [TestMethod, TestCategory("Unit")]
    public void AGroupLetsItsItemsOutWhereItStood()
    {
        Assert.AreEqual("treemap-beta\n\"A\": 10\n\"B\": 20\n\"T\"\n    \"C\": 30", Changed(Map, root => TreemapWriting.Ungrouped(root, Item(root, "S"))!));
        Assert.AreEqual("treemap-beta\n%% spend\n\"A\": 1\n\"T\"\n    \"C\": 3",
                        Changed("treemap-beta\n%% spend\n\"S\"\n    \"A\": 1\n\"T\"\n    \"C\": 3", root => TreemapWriting.Ungrouped(root, Item(root, "S"))!),
                        "what was written in front of the group in front of the first of them");
        Assert.IsNull(Root(Map) is var root ? TreemapWriting.Ungrouped(root, Item(root, "A")) : null, "a box holds nothing to let out");
    }

    [TestMethod, TestCategory("Unit")]
    public void ABoxMergedBackJoinsTheOneBeforeIt_OrAfterIt_OrGivesItsGroupItsValue()
    {
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 10\n    \"B\": 20",
                        Changed("treemap-beta\n\"S\"\n    \"A\": 5\n    \"\": 5\n    \"B\": 20", root => TreemapWriting.MergedBack(root, Unnamed(root))!),
                        "a split taken back is the box it was");
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 10",
                        Changed("treemap-beta\n\"S\"\n    \"\": 5\n    \"A\": 5", root => TreemapWriting.MergedBack(root, Unnamed(root))!),
                        "the first in its group joins the one after it");

        var alone = Change("treemap-beta\n\"T\"\n    \"\": 30", root => TreemapWriting.MergedBack(root, Unnamed(root))!);
        Assert.AreEqual("treemap-beta\n\"T\": 30", alone.Source, "a group grouped from one box is that box again");
        Assert.AreEqual(alone.Source.IndexOf("\"T\"", StringComparison.Ordinal) + 2, alone.Caret);

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 1",
                        Changed("treemap-beta\n\"\": 4\n\"S\"\n    \"A\": 1", root => TreemapWriting.MergedBack(root, Unnamed(root))!),
                        "with no box beside it, it is taken out");
    }

    [TestMethod, TestCategory("Unit")]
    public void ItemsMovedIntoAGroupGoAfterWhatItHolds_AndIntoTheTreemapAfterItsTop()
    {
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"B\": 20\n\"T\"\n    \"C\": 30\n    \"A\": 10",
                        Changed(Map, root => TreemapWriting.MovedInto(root, [Item(root, "A")], Item(root, "T"), 0)!));
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"B\": 20\n\"T\"\n    \"C\": 30\n\"A\": 10",
                        Changed(Map, root => TreemapWriting.MovedInto(root, [Item(root, "A")], root, 0)!));
        Assert.AreEqual("treemap-beta\n\"T\"\n    \"C\": 30\n    \"S\"\n        \"A\": 10\n        \"B\": 20",
                        Changed(Map, root => TreemapWriting.MovedInto(root, [Item(root, "S")], Item(root, "T"), 0)!), "a group goes with all it holds");

        var root = Root(Map);
        Assert.IsNull(TreemapWriting.MovedInto(root, [Item(root, "A")], Item(root, "S"), 0), "already in it");
        Assert.IsNull(TreemapWriting.MovedInto(root, [Item(root, "S")], Item(root, "S"), 0), "never into itself");
    }

    [TestMethod, TestCategory("Unit")]
    public void ABoxPutInIsWorthWhatTheFirstBoxBesideItIs_AndTheCaretIsInItsLabel()
    {
        var added = Change(Map, root => TreemapWriting.AddedInside(root, Item(root, "T")));

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 10\n    \"B\": 20\n\"T\"\n    \"C\": 30\n    \"\": 30", added.Source);
        Assert.AreEqual(added.Source.LastIndexOf("\"\"", StringComparison.Ordinal) + 1, added.Caret);

        Assert.AreEqual(Map + "\n\"\": 10", Changed(Map, TreemapWriting.Added), "at the top, worth the first box written");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFirstItemGoesAfterEverythingTheBlockHolds()
    {
        Assert.AreEqual("treemap-beta\n\"\": 1", Changed("treemap-beta", TreemapWriting.Added));
        Assert.AreEqual("treemap-beta\ntitle Spend\n\"\": 1\n", Changed("treemap-beta\ntitle Spend\n", TreemapWriting.Added),
                        "keeping the line ending the block closes with");
    }

    [TestMethod, TestCategory("Unit")]
    public void AnItemTakenOutTakesEverythingUnderIt_AndLeavesNoEmptyLine()
    {
        Assert.AreEqual("treemap-beta\n\"T\"\n    \"C\": 30", Changed(Map, root => TreemapWriting.Removed(root, [Item(root, "S")])!));
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 10\n    \"B\": 20\n\"T\"", Changed(Map, root => TreemapWriting.Removed(root, [Item(root, "C")])!));
        Assert.AreEqual("treemap-beta", Changed(Map, root => TreemapWriting.Removed(root, [Item(root, "S"), Item(root, "T")])!), "nothing left at all");
    }

    [TestMethod, TestCategory("Unit")]
    public void AnItemKeepsEveryCharacterItSaysButItsValue_AndCommentsGoWithTheItemAfterThem()
    {
        const string source = "treemap-beta\n\"S\"\n  %% the big one\n  \"A\": 1,000:::hot %% why\n  \"B\": 2\nclassDef hot fill:#f00";

        Assert.AreEqual("treemap-beta\n\"S\"\n  %% the big one\n  \"A\": 500:::hot %% why\n  \"\": 500\n  \"B\": 2\nclassDef hot fill:#f00",
                        Changed(source, root => TreemapWriting.Split(root, Item(root, "A"))!));
        Assert.AreEqual("treemap-beta\n\"S\"\n  %% the big one\n  \"A\": 1,002:::hot %% why\nclassDef hot fill:#f00",
                        Changed(source, root => TreemapWriting.Merged(root, [Item(root, "A"), Item(root, "B")], Item(root, "A"))!));
    }

    [TestMethod, TestCategory("Unit")]
    public void OnlyWhatChangedIsWrittenOver()
    {
        var root = Root(Map);
        var write = TreemapWriting.Split(root, Item(root, "B"))!.Writes.Single();

        Assert.IsTrue(write.Start > Map.IndexOf("\"A\"", StringComparison.Ordinal), "nothing in front of what changed");
        Assert.IsTrue(write.End <= Map.IndexOf("\"T\"", StringComparison.Ordinal), "nor after it");
    }

    private static ContentPart Root(string source) => ContentPart.Of(TreemapParserTests.Staged(source));

    private static ContentPart Item(ContentPart root, string label) =>
        TreemapWriting.Items(root).Single(item => item.Children[0].Stated()!.Inner(TreemapKinds.Label)!.Words()?.Text == label);

    /// <summary>The one item with nothing written in its label.</summary>
    private static ContentPart Unnamed(ContentPart root) =>
        TreemapWriting.Items(root).Single(item => item.Children[0].Stated()!.Inner(TreemapKinds.Label)!.Words() is not { Length: > 0 });

    private static (string Source, int Caret) Change(string source, Func<ContentPart, ContentChange> change)
    {
        var made = change(Root(source));
        var written = source;

        foreach (var write in made.Writes.OrderByDescending(write => write.Start))
            written = written[..write.Start] + write.Text + written[write.End..];

        return (written, made.Caret);
    }

    private static string Changed(string source, Func<ContentPart, ContentChange> change) => Change(source, change).Source;
}
