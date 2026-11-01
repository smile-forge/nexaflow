using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.TreeView;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.TreeView;

/// <summary>
/// What a <c>treeView-beta</c> block is read into: its nodes — the box drawing in front of each where there is one, its label bare
/// or in quotes, the slash making it a folder and what is noted after it — every character kept; and, once its stages have run,
/// each node gathered with the nodes under it, by indentation or by where its branch stands.
/// </summary>
[TestClass]
[CoversNode("treeview-ast")]
public class TreeViewParserTests
{
    /// <summary>The blocks Mermaid's documentation shows, which read with nothing wrong with them.</summary>
    public static readonly string[] Documented =
    [
        "treeView-beta\n    my-project/\n        src/\n            index.js\n        package.json\n        README.md",
        "treeView-beta\n    \"my project\"\n        \"folder with spaces\"\n            \"file.js\"",
        "treeView-beta\n├── src/\n│   ├── index.ts\n│   └── utils.ts\n├── package.json\n└── README.md",
        "treeView-beta\n├── src/\n│   ├── App.tsx :::highlight icon(logos:react) ## main component\n│   └── index.ts ## entry point\n├── .env ## environment variables\n├── Dockerfile\n└── package.json",
        "treeView-beta\n├── packages/\n│   ├── mermaid/\n│   │   ├── src/\n│   │   │   ├── parser.ts\n│   │   │   └── renderer.ts\n│   │   └── package.json\n│   └── parser/\n│       └── src/\n└── README.md",
        "treeView-beta\n    src/\n        App.tsx :::highlight\n        index.js\n    package.json",
        "treeView-beta\n    src/\n        index.js ## app entry point\n        config.ts ## runtime configuration\n    package.json ## project manifest",
        "---\nconfig:\n  treeView:\n    showIcons: true\n---\ntreeView-beta\n    src/\n        index.js\n    package.json",
        "treeView-beta\n    src/\n        App.tsx icon(logos:react)\n        index.js\n    package.json",
        "treeView-beta\n    %% Generated files, do not edit\n    src/\n        generated/\n        index.js",
        "treeView-beta\n    🚀 rocket-app/\n        📦 packages/\n            🎨 ui/\n        📝 README.md",
        "---\nconfig:\n    treeView:\n        rowIndent: 80\n        lineThickness: 3\n    themeVariables:\n        treeView:\n            labelFontSize: '20px'\n            labelColor: '#FF0000'\n---\ntreeView-beta\n    \"packages\"\n        \"mermaid\"\n            \"src\"\n        \"parser\"",
    ];

    /// <summary>Every construct read, and what nobody means to write.</summary>
    public static readonly (string What, string Source)[] Blocks =
    [
        ("a header and nothing else", "treeView-beta"),
        ("no header, as a tree view fence needs none", "src/\n    index.js"),
        ("a title on a line of its own", "treeView-beta\ntitle Files\nsrc/"),
        ("a file called title", "treeView-beta\ntitle.txt\ntitles/"),
        ("a bare label with spaces in it", "treeView-beta\nmy file.txt"),
        ("a pair of percents in a bare label", "treeView-beta\n100%% sure.txt"),
        ("a comment after a label in quotes", "treeView-beta\n\"a b\" %% why"),
        ("a label in single quotes, a folder", "treeView-beta\n'my dir/'\n    x"),
        ("annotations in any order", "treeView-beta\nApp.tsx ## main :::highlight"),
        ("an icon hidden", "treeView-beta\nindex.js icon(none)\nmain.js icon()"),
        ("a class and its name apart", "treeView-beta\nx :::   highlight"),
        ("tabs for indentation", "treeView-beta\nsrc/\n\tindex.js"),
        ("a decoration line between nodes", "treeView-beta\n├── src/\n│   └── a.ts\n│\n└── b.ts"),
        ("the heavy box drawing", "treeView-beta\n┣━━ src/\n┃   ┗━━ a.ts\n┗━━ b.ts"),
        ("a root and its box drawn children", "treeView-beta\nproject/\n├── src/\n└── README.md"),
        ("written on Windows", "treeView-beta\r\nsrc/\r\n    a.js  \r\n"),
        ("blank lines between nodes", "treeView-beta\nsrc/\n\n    a.js\n\nb.js"),
        ("accessibility lines", "treeView-beta\naccTitle: Files\naccDescr: The tree\nsrc/"),
        // Half written.
        ("a label with nothing in it", "treeView-beta\n\"\""),
        ("a branch with nothing after it", "treeView-beta\n├── src/\n└── "),
        ("a description with nothing in it", "treeView-beta\nsrc/ ##"),
        ("a class mark and nothing after it", "treeView-beta\nsrc/ :::"),
        // What nobody means to write.
        ("a label never closed", "treeView-beta\n\"src\n    a.js"),
        ("an icon never closed", "treeView-beta\na.js icon(react"),
        ("something after a label in quotes", "treeView-beta\n\"a\"b"),
        ("indentation with no branch in a drawn tree", "treeView-beta\n├── src/\n    a.ts"),
        ("a header taking more than its word", "treeView-beta files\nsrc/"),
        ("nothing at all", ""),
    ];

    private static IEnumerable<(string What, string Source)> All => Blocks.Concat(Documented.Select(source => ("documented", source)));

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = TreeViewParser.Parse(source);
        return new AstPipeline(TreeViewPipeline.Of(tree, holes)).Run(tree);
    }

    [TestMethod, TestCategory("Unit")]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in All)
        {
            Assert.AreEqual(source, TreeViewParser.Parse(source).Print(), what);
            Assert.AreEqual(source, Staged(source).Print(), $"{what}, after the stages");
            Assert.AreEqual(source, Staged(source, holes: true).Print(), $"{what}, with holes");
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void AndSoDoesEveryStageOnTheWayThere()
    {
        foreach (var (what, source) in All)
            foreach (var holes in new[] { false, true })
            {
                var tree = TreeViewParser.Parse(source);

                foreach (var stage in TreeViewPipeline.Of(tree, holes))
                {
                    tree = stage.Run(tree);
                    Assert.AreEqual(source, tree.Print(), $"{what}: after {stage.Name}{(holes ? ", with holes" : "")}");
                }
            }
    }

    [TestMethod, TestCategory("Unit")]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in All)
            for (var length = 0; length <= source.Length; length++)
                Assert.AreEqual(source[..length], Staged(source[..length], holes: true).Print(), $"{what}: after {length} character(s)");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in All)
            foreach (var place in TreeViewParser.Parse(source).Placed())
                if (place.Node.IsLeaf)
                    Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text, $"{what}: {place.Node.Kind} at {place.Start}");
    }

    [TestMethod]
    public void EveryPieceSaysWhereItWasRead()
    {
        foreach (var (what, source) in All)
            foreach (var (stage, tree) in new[] { ("read", TreeViewParser.Parse(source)), ("after the stages", Staged(source)) })
            {
                var faults = AstOracle.Faults(source, tree).ToList();
                Assert.AreEqual(0, faults.Count, $"{what}, {stage}\n{string.Join("\n", faults)}");
            }
    }

    [TestMethod]
    public void AReversedTreeStillPrintsWhatWasWritten()
    {
        foreach (var (what, source) in All)
            Assert.AreEqual(source, AstOracle.Reversed(TreeViewParser.Parse(source)).Print(), what);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheDocumentedBlocksHaveNothingWrongWithThem()
    {
        foreach (var source in Documented)
        {
            var trouble = Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.AreEqual(0, trouble.Count, $"{source}\n{string.Join("\n", trouble)}");
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void WhatWillNotReadSaysWhy()
    {
        foreach (var source in new[] { "treeView-beta\n\"src", "treeView-beta\na.js icon(react", "treeView-beta\n\"a\"b", "treeView-beta files" })
            Assert.IsTrue(TreeViewParser.Parse(source).SelfAndDescendants().Any(node => node.Trouble is not null), source);

        Assert.IsTrue(Staged("treeView-beta\n├── src/\n    a.ts").SelfAndDescendants().Any(node => node.Trouble is not null),
                      "an indented node with no branch, in a drawn tree, is the stages' to see");
    }

    [TestMethod, TestCategory("Unit")]
    public void NothingStillToWriteIsWrong()
    {
        foreach (var source in new[] { "treeView-beta\n\"\"", "treeView-beta\n├── src/\n└── ", "treeView-beta\nsrc/ ##", "treeView-beta\nsrc/ :::", "treeView-beta\ntitle" })
            Assert.IsFalse(Staged(source, holes: true).SelfAndDescendants().Any(node => node.Trouble is not null && !node.IsDerived), source);
    }

    [TestMethod, TestCategory("Unit")]
    public void ALabelRunsToTheFirstAnnotation_AndASlashAfterItMakesAFolder()
    {
        var items = Items(Staged("treeView-beta\nmy file.txt :::highlight icon(file) ## the file\nsrc/\n\"my dir/\""));

        Assert.AreEqual("my file.txt", Label(items[0]));
        Assert.IsTrue(Said(items[0]).Highlighted);
        Assert.IsFalse(Said(items[0]).Folder);
        Assert.AreEqual("the file", items[0].SelfAndDescendants().Single(part => part.Role == TreeViewRoles.Description).Text);
        Assert.AreEqual("file", items[0].SelfAndDescendants().Single(part => part.Role == TreeViewRoles.Icon).Text);

        Assert.AreEqual("src", Label(items[1]), "the slash is no part of what the label says");
        Assert.IsTrue(Said(items[1]).Folder);
        Assert.AreEqual("my dir", Label(items[2]));
        Assert.IsTrue(Said(items[2]).Folder, "in quotes too, as Mermaid reads the name it unquotes");
    }

    [TestMethod, TestCategory("Unit")]
    public void IndentedNodesNestUnderTheNearestNodeIndentedLess_ManyAtTheTop()
    {
        var root = ContentPart.Of(Staged("treeView-beta\na/\n    b/\n        c\n    d\ne"));
        var top = Under(root);

        CollectionAssert.AreEqual(new[] { "a", "e" }, top.Select(Label).ToArray());
        CollectionAssert.AreEqual(new[] { "b", "d" }, Under(top[0]).Select(Label).ToArray());
        CollectionAssert.AreEqual(new[] { "c" }, Under(Under(top[0])[0]).Select(Label).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void DrawnNodesNestByWhereTheirBranchStands()
    {
        var root = ContentPart.Of(Staged(Documented[4]));
        var packages = Under(root)[0];

        CollectionAssert.AreEqual(new[] { "packages", "README.md" }, Under(root).Select(Label).ToArray());
        CollectionAssert.AreEqual(new[] { "mermaid", "parser" }, Under(packages).Select(Label).ToArray());
        CollectionAssert.AreEqual(new[] { "src", "package.json" }, Under(Under(packages)[0]).Select(Label).ToArray());
        CollectionAssert.AreEqual(new[] { "parser.ts", "renderer.ts" }, Under(Under(Under(packages)[0])[0]).Select(Label).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void ARootWrittenBareHoldsTheDrawnNodesUnderIt()
    {
        var root = ContentPart.Of(Staged("treeView-beta\nproject/\n├── src/\n│   └── a.ts\n└── README.md"));
        var project = Under(root).Single();

        CollectionAssert.AreEqual(new[] { "src", "README.md" }, Under(project).Select(Label).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void ALineOfNothingButBarsIsNoNode()
    {
        var root = ContentPart.Of(Staged("treeView-beta\n├── src/\n│   └── a.ts\n│\n└── b.ts"));
        CollectionAssert.AreEqual(new[] { "src", "b.ts" }, Under(root).Select(Label).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void AMermaidBlockHeadedTreeViewIsATreeView()
    {
        Assert.AreEqual(MermaidDiagram.TreeView, MermaidDiagrams.Named("treeView-beta"));
        Assert.AreEqual(MermaidDiagram.TreeView, MermaidBlock.Of(TreeViewParser.Parse("treeView-beta\nsrc/")).Diagram);
    }

    [TestMethod, TestCategory("Unit")]
    public void ALabelIsWrittenBareWhereItCanBe_AndInQuotesWhereItCannot()
    {
        Assert.AreEqual("index.js", TreeViewParser.Labelled("index.js", null, folder: false));
        Assert.AreEqual("my file", TreeViewParser.Labelled("my file", null, folder: false), "a space in the middle is a bare label's");
        Assert.AreEqual("src/", TreeViewParser.Labelled("src", null, folder: true));
        Assert.AreEqual("\"a ## b\"", TreeViewParser.Labelled("a ## b", null, folder: false), "what would read as a description");
        Assert.AreEqual("'\"quoted\"'", TreeViewParser.Labelled("\"quoted\"", null, folder: false), "in the quote it holds none of");
        Assert.AreEqual("\"%% not a comment/\"", TreeViewParser.Labelled("%% not a comment", '"', folder: true));
    }

    private static List<ContentPart> Items(ContentNode tree) => [.. ContentPart.Of(tree).SelfAndDescendants().Where(part => part.Kind == TreeViewKinds.Node)];

    private static List<ContentPart> Under(ContentPart holder) => [.. holder.Children.Where(part => part.Kind == TreeViewKinds.Node)];

    private static string Label(ContentPart item) => item.Children[0].Stated()!.Inner(TreeViewKinds.Label)!.Words()!.Text;

    private static TreeViewNode Said(ContentPart item) => (TreeViewNode)item.Node;
}
