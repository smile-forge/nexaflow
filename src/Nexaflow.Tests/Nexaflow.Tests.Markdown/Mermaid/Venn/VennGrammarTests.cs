using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Venn;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Venn;

/// <summary>
/// What a <c>venn-beta</c> block is read into: a title, sets and unions with their names, labels and sizes, the items
/// written in them and the styles of all three — and what is written instead of any of that, held with the reason.
/// </summary>
[TestClass]
[CoversNode("venn-ast")]
public class VennGrammarTests : MermaidGrammarContract
{
    /// <summary>The first block Mermaid's documentation shows: three sets and every overlap between them.</summary>
    public const string Features =
        """
        venn-beta
          title What makes a good feature
          set Desirable
          set Feasible
          set Viable
          union Desirable,Feasible["Buildable"]
          union Feasible,Viable["Sustainable"]
          union Desirable,Viable["Marketable"]
          union Desirable,Feasible,Viable["Ship it"]
        """;

    /// <summary>The documentation's block with sizes, items and a style of each kind.</summary>
    public const string Styled =
        """
        venn-beta
          set A["Alpha"]:20
            text A1["React"]
            text A2["Design Systems"]
          set B["Beta"]:12
          union A,B["AB"]:3
          style A fill:#ff6b6b
          style A,B color:#333
          style A1 color:red
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Venn;

    protected override IEnumerable<string> DocumentedBlocks => [Features, Styled];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documented features", Features),
        ("the documented styles", Styled),
        ("a quoted title", "venn-beta\n  title \"Team overlap\"\n  set Frontend\n  set Backend\n  union Frontend,Backend[\"APIs\"]"),
        ("items under sets and a union",
            "venn-beta\n  set A[\"Frontend\"]\n    text A1[\"React\"]\n  set B[\"Backend\"]\n    text B1[\"API\"]\n  union A,B[\"Shared\"]\n    text AB1[\"OpenAPI\"]"),
        ("an item naming its region", "venn-beta\nset A\nset B\nunion A,B\ntext A,B AB1[\"OpenAPI\"]"),
        ("quoted names", "venn-beta\n  set \"Foo Bar\"[\"Foo\"]\n    text \"an item\""),
        ("a bare label", "venn-beta\n  set A[ Alpha ]"),
        ("space everywhere", "venn-beta\n  set A [\"Alpha\"] : 20  \n  set B\n  union A , B"),
        ("sizes", "venn-beta\n  set A:20\n  set B:.5\n  union A,B:+3"),
        ("rgb in a style", "venn-beta\n  set A\n  style A fill:rgb(1, 2, 3), stroke-width:4px, fill-opacity:0.5"),
        ("a comment closing a line", "venn-beta\n  set A[\"Alpha\"] %% the first\n  %% on its own\n    text A1"),
        ("a new item being written", "venn-beta\n  set A\n    text \"\"\n    text A1"),
        ("written on Windows", "venn-beta\r\n  set A\r\n    text \"\"\r\n"),
        // What nobody means to write.
        ("no name", "venn-beta\n  set"),
        ("a union still being written", "venn-beta\n  set A\n  union A, "),
        ("a union of one set", "venn-beta\n  set A\n  union A"),
        ("a union of a set not written", "venn-beta\n  set A\n  union A,B"),
        ("a label never closed", "venn-beta\n  set A[\"Alpha"),
        ("a name never closed", "venn-beta\n  set \"Alpha"),
        ("a size that is no number", "venn-beta\n  set A:lots"),
        ("a size of nothing", "venn-beta\n  set A:0"),
        ("a size still to come", "venn-beta\n  set A: "),
        ("a style of nothing", "venn-beta\n  set A\n  style A"),
        ("a style nobody knows", "venn-beta\n  set A\n  style A glow:yes"),
        ("a style of something not there", "venn-beta\n  set A\n  style Z fill:red"),
        ("an item with no region", "venn-beta\n  text A1"),
        ("an item at the start of a line", "venn-beta\n  set A\ntext A1"),
        ("an unknown line", "venn-beta\n  circle A"),
        ("words after the keyword", "venn-beta please"),
        ("nothing but the keyword", "venn-beta"),
    ];

    [TestMethod]
    public void ASetIsItsNameItsLabelAndItsSize()
    {
        var set = Nodes("venn-beta\n  set A[\"Alpha\"]:20", VennKinds.Set).Single();

        Assert.AreEqual("A", Name(set.Children.Single(child => child.Kind == MermaidKinds.Name)));
        Assert.AreEqual("Alpha", Name(set.Children.Single(child => child.Kind == MermaidKinds.Label)));
        Assert.AreEqual("20", set.SelfAndDescendants().Single(node => node.Kind == Kinds.Number).Text);
    }

    [TestMethod]
    public void ANameInQuotesIsWhatIsBetweenThem_AndABareLabelIsItsWordsWithoutTheSpaceRoundThem()
    {
        var quoted = Nodes("venn-beta\n  set \"Foo Bar\"[ Foo ]", VennKinds.Set).Single();

        Assert.AreEqual("Foo Bar", Name(quoted.Children.Single(child => child.Kind == MermaidKinds.Name)));
        Assert.AreEqual("Foo", Name(quoted.Children.Single(child => child.Kind == MermaidKinds.Label)));
    }

    [TestMethod]
    public void AUnionListsTheSetsItIsTheOverlapOf()
    {
        var union = Nodes(Features, VennKinds.Union).Last();

        CollectionAssert.AreEqual(new[] { "Desirable", "Feasible", "Viable" },
                                  union.SelfAndDescendants().Where(node => node is { Kind: Kinds.Words, Role: VennRoles.Id })
                                      .Select(node => node.Text).ToArray());
        Assert.AreEqual("Ship it", Name(union.Children.Single(child => child.Kind == MermaidKinds.Label)));
    }

    [TestMethod]
    public void AnItemWrittenAtTheStartOfALineNamesItsRegionFirst()
    {
        var item = Nodes("venn-beta\nset A\nset B\nunion A,B\ntext A,B AB1[\"OpenAPI\"]", VennKinds.Text).Single();

        Assert.AreEqual("A,B", item.Part(VennRoles.Region)!.Print());
        Assert.AreEqual("AB1", Name(item.Children.Single(child => child.Kind == MermaidKinds.Name)));
        Assert.AreEqual("OpenAPI", Name(item.Children.Single(child => child.Kind == MermaidKinds.Label)));
    }

    [TestMethod]
    public void AStyleSetsEachPropertyItNames_AColourWithCommasInItKeptWhole()
    {
        var style = Nodes("venn-beta\n  set A\n  style A fill:rgb(1, 2, 3), stroke-width:4px, fill-opacity:0.5", VennKinds.Style).Single();

        CollectionAssert.AreEqual(
            new[] { ("fill", "rgb(1, 2, 3)"), ("stroke-width", "4px"), ("fill-opacity", "0.5") },
            style.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Property)
                .Select(property => (property.Children.First().Text,
                                     property.Children.Single(child => child.Kind == MermaidKinds.Setting).Text))
                .ToArray());
    }

    [TestMethod]
    public void ACommentClosingALineIsTheLinesOwn()
    {
        var set = Nodes("venn-beta\n  set A[\"Alpha\"] %% the first", VennKinds.Set).Single();

        Assert.AreEqual("%% the first", set.Children.Single(child => child.Kind == Kinds.Comment).Text);
        Assert.AreEqual("Alpha", Name(set.Children.Single(child => child.Kind == MermaidKinds.Label)));
    }

    [TestMethod]
    public void WhatCannotBeReadIsHeldWithTheReason()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("venn-beta\n  set", "is named"),
                     ("venn-beta\n  set A[\"Alpha", "never closed"),
                     ("venn-beta\n  set \"Alpha", "never closed"),
                     ("venn-beta\n  set A B", "its name, then its label"),
                     ("venn-beta\n  style A", "what it sets"),
                     ("venn-beta\n  circle A", "a set, a union"),
                 })
        {
            var held = MermaidParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null);
            Assert.AreEqual(Kinds.Verbatim, held.Kind, source);
            StringAssert.Contains(held.Trouble, reason, source);
        }
    }

    [TestMethod]
    public void WhatIsWrongWithAPartIsSaidOnThatPart()
    {
        foreach (var (source, kind, reason) in new[]
                 {
                     ("venn-beta\n  set A:lots", Kinds.Number, "not a number"),
                     ("venn-beta\n  set A:0", Kinds.Number, "greater than nought"),
                     ("venn-beta\n  set 1A", Kinds.Words, "starts with a letter"),
                     ("venn-beta\n  set A[]", Kinds.Words, "has something in it"),
                     ("venn-beta\n  set A\n  style A glow:yes", MermaidKinds.Key, "not 'glow'"),
                     ("venn-beta\n  set A\n  style A fill-opacity:2", MermaidKinds.Setting, "from 0 to 1"),
                     ("venn-beta\n  set A\n  style A stroke-width:thick", MermaidKinds.Setting, "pixels"),
                     ("venn-beta please", Kinds.Verbatim, "Nothing follows venn-beta"),
                 })
        {
            var troubled = MermaidParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null);
            Assert.AreEqual(kind, troubled.Kind, source);
            StringAssert.Contains(troubled.Trouble, reason, source);
        }
    }

    [TestMethod]
    public void ASizeOrAUnionStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[] { "venn-beta\n  set A: ", "venn-beta\n  set A\n  union A, " })
            Assert.IsFalse(MermaidStaged.Read(source).SelfAndDescendants().Any(node => node.Trouble is not null), source);
    }

    /// <summary>A block read through the pipeline, with where each part sits.</summary>
    private static ContentPart Reading(string source, bool holes = false) => ContentReading.Of(MermaidStaged.Read(source, holes)).Root;

    private static List<ContentNode> Nodes(string source, string kind) =>
        [.. MermaidParser.Parse(source).SelfAndDescendants().Where(node => node.Kind == kind)];

    /// <summary>What a name or a label says, without its quotes or brackets.</summary>
    private static string Name(ContentNode node) =>
        node.SelfAndDescendants().First(child => child.Kind == Kinds.Words).Text;
}
