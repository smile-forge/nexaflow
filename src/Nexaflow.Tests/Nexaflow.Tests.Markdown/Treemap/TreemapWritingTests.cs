using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Treemap;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Treemap;

/// <summary>
/// What a change to a treemap's items is written as: an item put in, taken out or moved goes with everything under it, keeps every
/// character it says, and only its indentation is written again — so the map still nests as it is drawn.
/// </summary>
[TestClass]
[CoversNode("treemap-writing")]
public class TreemapWritingTests
{
    private const string Map = "treemap-beta\n\"S\"\n    \"A\": 10\n    \"B\": 20\n\"T\"\n    \"C\": 30";

    [TestMethod, TestCategory("Unit")]
    public void ALeafPutInIsWorthWhatTheLeafBesideItIs_AndTheCaretIsInItsLabel()
    {
        var after = Change(Map, root => TreemapWriting.Added(root, Item(root, "A"), OutlineSide.After));

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 10\n    \"\": 10\n    \"B\": 20\n\"T\"\n    \"C\": 30", after.Source);
        Assert.AreEqual(after.Source.IndexOf("\"\": 10", StringComparison.Ordinal) + 1, after.Caret, "between its quotes");

        Assert.AreEqual("treemap-beta\n\"\": 10\n\"S\"\n    \"A\": 10\n    \"B\": 20\n\"T\"\n    \"C\": 30",
                        Changed(Map, root => TreemapWriting.Added(root, Item(root, "S"), OutlineSide.Before)),
                        "beside a section, worth what the first leaf in it is");
    }

    [TestMethod, TestCategory("Unit")]
    public void ALeafPutInAfterASectionGoesAfterEverythingInIt()
    {
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 10\n    \"B\": 20\n\"\": 10\n\"T\"\n    \"C\": 30",
                        Changed(Map, root => TreemapWriting.Added(root, Item(root, "S"), OutlineSide.After)));
    }

    [TestMethod, TestCategory("Unit")]
    public void ALeafPutInsideASectionGoesAfterWhatItHolds_AndALeafGivenOneHandsItItsValue()
    {
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 10\n    \"B\": 20\n    \"\": 10\n\"T\"\n    \"C\": 30",
                        Changed(Map, root => TreemapWriting.AddedInside(root, Item(root, "S"))));

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\"\n        \"\": 10\n    \"B\": 20\n\"T\"\n    \"C\": 30",
                        Changed(Map, root => TreemapWriting.AddedInside(root, Item(root, "A"))),
                        "the leaf becomes a section, so the map adds up to what it did");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFirstItemGoesAfterEverythingTheBlockHolds()
    {
        Assert.AreEqual("treemap-beta\n\"\": 1", Changed("treemap-beta", root => TreemapWriting.Added(root, null, OutlineSide.After)));
        Assert.AreEqual("treemap-beta\ntitle Spend\n\"\": 1\n", Changed("treemap-beta\ntitle Spend\n", root => TreemapWriting.Added(root, null, OutlineSide.After)),
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
    public void AnItemLetGoOnASectionGoesIntoIt_AndOnALeafBesideIt()
    {
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"B\": 20\n\"T\"\n    \"C\": 30\n    \"A\": 10",
                        Changed(Map, root => TreemapWriting.Moved(root, [Item(root, "A")], Item(root, "T"), 0)!));

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"B\": 20\n\"T\"\n    \"C\": 30\n    \"A\": 10",
                        Changed(Map, root => TreemapWriting.Moved(root, [Item(root, "A")], Item(root, "C"), 0)!));

        Assert.AreEqual("treemap-beta\n\"T\"\n    \"C\": 30\n    \"S\"\n        \"A\": 10\n        \"B\": 20",
                        Changed(Map, root => TreemapWriting.Moved(root, [Item(root, "S")], Item(root, "T"), 0)!),
                        "a section goes with everything in it, indented on");

        Assert.IsNull(Root(Map) is var root ? TreemapWriting.Moved(root, [Item(root, "S")], Item(root, "A"), 0) : null, "never into itself");
    }

    [TestMethod, TestCategory("Unit")]
    public void TabTakesAnItemIntoTheSectionBeforeIt_AndShiftTabTakesItOutAfterTheSectionItIsIn()
    {
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"A\": 10\n    \"B\": 20\n    \"T\"\n        \"C\": 30",
                        Changed(Map, root => TreemapWriting.Deeper(root, [Item(root, "T")], 0)!));

        Assert.AreEqual("treemap-beta\n\"S\"\n    \"B\": 20\n\"A\": 10\n\"T\"\n    \"C\": 30",
                        Changed(Map, root => TreemapWriting.Shallower(root, [Item(root, "A")], 0)!),
                        "after the section it was in, the leaves after it staying where they are");

        Assert.IsNull(Root(Map) is var root ? TreemapWriting.Deeper(root, [Item(root, "A")], 0) : null, "nothing before it to go into");
        Assert.IsNull(Root(Map) is var top ? TreemapWriting.Shallower(top, [Item(top, "S")], 0) : null, "nothing to come out of");
    }

    [TestMethod, TestCategory("Unit")]
    public void PageUpAndDownMoveAnItemAmongItsSiblings()
    {
        Assert.AreEqual("treemap-beta\n\"S\"\n    \"B\": 20\n    \"A\": 10\n\"T\"\n    \"C\": 30", Changed(Map, root => TreemapWriting.Raised(root, [Item(root, "B")], 0)!));
        Assert.AreEqual("treemap-beta\n\"T\"\n    \"C\": 30\n\"S\"\n    \"A\": 10\n    \"B\": 20", Changed(Map, root => TreemapWriting.Lowered(root, [Item(root, "S")], 0)!));
        Assert.IsNull(Root(Map) is var root ? TreemapWriting.Raised(root, [Item(root, "A")], 0) : null, "already the first");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheCaretStaysInWhatItWasIn()
    {
        var caret = Map.IndexOf("\"C\"", StringComparison.Ordinal) + 2;
        var moved = Change(Map, root => TreemapWriting.Deeper(root, [Item(root, "T")], caret)!);

        Assert.AreEqual('"', moved.Source[moved.Caret], "still just before the closing quote of C's label");
        Assert.AreEqual('C', moved.Source[moved.Caret - 1]);
    }

    [TestMethod, TestCategory("Unit")]
    public void AnItemKeepsEveryCharacterItSays_AndCommentsGoWithTheItemAfterThem()
    {
        const string source = "treemap-beta\n\"S\"\n  %% the big one\n  \"A\": 1,000:::hot %% why\n  \"B\": 2\nclassDef hot fill:#f00";

        Assert.AreEqual("treemap-beta\n\"S\"\n  \"B\": 2\n  %% the big one\n  \"A\": 1,000:::hot %% why\nclassDef hot fill:#f00",
                        Changed(source, root => TreemapWriting.Lowered(root, [Item(root, "A")], 0)!));
    }

    [TestMethod, TestCategory("Unit")]
    public void OnlyWhatChangedIsWrittenOver()
    {
        var root = Root(Map);
        var change = TreemapWriting.Raised(root, [Item(root, "B")], 0)!;
        var write = change.Writes.Single();

        Assert.IsTrue(write.Start > Map.IndexOf("\"S\"", StringComparison.Ordinal), "nothing in front of what moved");
        Assert.IsTrue(write.End <= Map.IndexOf("\"T\"", StringComparison.Ordinal), "nor after it");
    }

    private static ContentPart Root(string source) => ContentPart.Of(TreemapParserTests.Staged(source));

    private static ContentPart Item(ContentPart root, string label) =>
        TreemapWriting.Items(root).Single(item => item.Children[0].Stated()!.Inner(TreemapKinds.Label)!.Words()!.Text == label);

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
