using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.TreeView;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.TreeView;

/// <summary>
/// What a change to a tree view's nodes is written as: a node goes with everything under it and keeps every character it says,
/// and what says where it stands — its indentation, or every branch and bar of a box drawing — is written again round it.
/// </summary>
[TestClass]
[CoversNode("treeview-writing")]
public class TreeViewWritingTests
{
    private const string Indented = "treeView-beta\nsrc/\n    a.ts\n    b.ts\nREADME.md";

    private const string Drawn = "treeView-beta\n├── src/\n│   ├── a.ts\n│   └── b.ts\n└── README.md";

    [TestMethod, TestCategory("Unit")]
    public void ANodePutInIsALabelStillToWrite_TheCaretInIt()
    {
        var after = Change(Indented, root => TreeViewWriting.Added(root, Item(root, "a.ts"), OutlineSide.After));

        Assert.AreEqual("treeView-beta\nsrc/\n    a.ts\n    \"\"\n    b.ts\nREADME.md", after.Source);
        Assert.AreEqual(after.Source.IndexOf("\"\"", StringComparison.Ordinal) + 1, after.Caret);
    }

    [TestMethod, TestCategory("Unit")]
    public void InADrawnTreeEveryBranchAndBarIsDrawnAgainRoundWhatChanged()
    {
        Assert.AreEqual("treeView-beta\n├── src/\n│   ├── a.ts\n│   ├── b.ts\n│   └── \"\"\n└── README.md",
                        Changed(Drawn, root => TreeViewWriting.Added(root, Item(root, "b.ts"), OutlineSide.After)),
                        "the branch before the new last child is no longer the last");

        Assert.AreEqual("treeView-beta\n├── src/\n│   └── a.ts\n└── README.md",
                        Changed(Drawn, root => TreeViewWriting.Removed(root, [Item(root, "b.ts")])!));

        Assert.AreEqual("treeView-beta\n├── src/\n│   └── b.ts\n└── README.md\n    └── a.ts",
                        Changed(Drawn, root => TreeViewWriting.Moved(root, [Item(root, "a.ts")], Item(root, "README.md"), OutlineSide.Inside, 0)!),
                        "under the last node at the top no bar runs down");
    }

    [TestMethod, TestCategory("Unit")]
    public void ADrawnTreeWithARootWrittenBareKeepsItBare()
    {
        const string source = "treeView-beta\nproject/\n├── src/\n└── README.md";

        Assert.AreEqual("treeView-beta\nproject/\n├── src/\n├── README.md\n└── \"\"",
                        Changed(source, root => TreeViewWriting.Added(root, Item(root, "README.md"), OutlineSide.After)));
    }

    [TestMethod, TestCategory("Unit")]
    public void TheHeavyDrawingIsDrawnAgainHeavy()
    {
        Assert.AreEqual("treeView-beta\n┣━━ src/\n┃   ┗━━ b.ts\n┗━━ a.ts",
                        Changed("treeView-beta\n┣━━ src/\n┃   ┣━━ a.ts\n┃   ┗━━ b.ts", root => TreeViewWriting.Shallower(root, [Item(root, "a.ts")], 0)!));
    }

    [TestMethod, TestCategory("Unit")]
    public void TabAndShiftTabMoveANodeALevelInAndOut()
    {
        Assert.AreEqual("treeView-beta\nsrc/\n    a.ts\n    b.ts\n    README.md", Changed(Indented, root => TreeViewWriting.Deeper(root, [Item(root, "README.md")], 0)!));
        Assert.AreEqual("treeView-beta\nsrc/\n    b.ts\na.ts\nREADME.md", Changed(Indented, root => TreeViewWriting.Shallower(root, [Item(root, "a.ts")], 0)!));
    }

    [TestMethod, TestCategory("Unit")]
    public void ANodeDroppedGoesInFrontOfAfterOrInsideAnother()
    {
        Assert.AreEqual("treeView-beta\nsrc/\n    b.ts\nREADME.md\na.ts", Changed(Indented, root => TreeViewWriting.Moved(root, [Item(root, "a.ts")], Item(root, "README.md"), OutlineSide.After, 0)!));
        Assert.AreEqual("treeView-beta\nsrc/\n    b.ts\na.ts\nREADME.md", Changed(Indented, root => TreeViewWriting.Moved(root, [Item(root, "a.ts")], Item(root, "README.md"), OutlineSide.Before, 0)!));
        Assert.AreEqual("treeView-beta\nsrc/\n    a.ts\n    b.ts\n        README.md", Changed(Indented, root => TreeViewWriting.Moved(root, [Item(root, "README.md")], Item(root, "b.ts"), OutlineSide.Inside, 0)!));
        Assert.IsNull(Root(Indented) is var root ? TreeViewWriting.Moved(root, [Item(root, "src")], Item(root, "a.ts"), OutlineSide.Inside, 0) : null, "never into itself");
    }

    [TestMethod, TestCategory("Unit")]
    public void ANodeGivenOneInsideBecomesAFolder_WhereTheTreeMarksItsFolders()
    {
        Assert.AreEqual("treeView-beta\nsrc/\n    a.ts/\n        \"\"\n    b.ts\nREADME.md", Changed(Indented, root => TreeViewWriting.AddedInside(root, Item(root, "a.ts"))));
        Assert.AreEqual("treeView-beta\nTopic\n    Idea\n        \"\"", Changed("treeView-beta\nTopic\n    Idea", root => TreeViewWriting.AddedInside(root, Item(root, "Idea"))),
                        "a tree marking no folders is left as it is");
    }

    [TestMethod, TestCategory("Unit")]
    public void ANodeIsMadeAFolderOrAFileByItsSlash()
    {
        Assert.AreEqual("treeView-beta\nsrc\n    a.ts\n    b.ts\nREADME.md", Changed(Indented, root => TreeViewWriting.Foldered(Item(root, "src"), folder: false, 0)!));
        Assert.AreEqual("treeView-beta\n\"my docs/\" ## all of them", Changed("treeView-beta\n\"my docs\" ## all of them", root => TreeViewWriting.Foldered(Item(root, "my docs"), folder: true, 0)!));
        Assert.IsNull(Root(Indented) is var root ? TreeViewWriting.Foldered(Item(root, "src"), folder: true, 0) : null, "a folder already");
    }

    [TestMethod, TestCategory("Unit")]
    public void WordsBesideANodeGoAfterWhatItSays_TheCaretWhereTheyGo()
    {
        var described = Change("treeView-beta\n\"a b\" :::highlight", root => TreeViewWriting.Described(Item(root, "a b"))!);

        Assert.AreEqual("treeView-beta\n\"a b\" :::highlight ## ", described.Source);
        Assert.AreEqual(described.Source.Length, described.Caret);

        Assert.IsNull(TreeViewWriting.Described(Item(Root("treeView-beta\n\"a b\" %% why"), "a b")),
                      "words beside a node run to the end of its line, so a comment there would be taken into them");
    }

    private static ContentPart Root(string source) => ContentPart.Of(TreeViewParserTests.Staged(source));

    private static ContentPart Item(ContentPart root, string label) =>
        TreeViewWriting.Items(root).Single(item => TreeViewWriting.Label(item)!.Words()!.Text == label);

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
