using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.EventModeling;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.EventModeling;

/// <summary>
/// The tree an <c>eventmodeling</c> block is read into: a stream of tokens as Mermaid's grammar reads one, so a statement may run
/// over several rows and several may share one, with comments and directives wherever they stand — and whatever was written prints
/// back as it was written, what will not read held with the reason.
/// </summary>
[TestClass]
[CoversNode("eventmodeling-ast")]
public class EventModelingParserTests : ContentParserContract
{
    /// <summary>The shopping cart the documentation builds up, every statement in it.</summary>
    internal const string Cart = """
        eventmodeling

        tf 01 ui CartUI
        tf 02 cmd AddItem [[AddItem01]]
        tf 03 evt ItemAdded [[ItemAdded]]
        tf 04 cmd AddItem [[AddItem02]]
        tf 05 evt ItemAdded [[ItemAdded]]

        data AddItem01 {
          description: 'john'
          image: 'avatar_john'
          price: 20.4
        }

        data AddItem02 {
          description: 'jack'
          image: 'avatar_jack'
          price: 12.5
        }

        data ItemAdded {
          description: string
          image: string
          price: number
        }
        """;

    protected override ContentNode Parse(string? source) => EventModelingParser.Parse(source);

    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(holes);

    /// <summary>The stages the language entry offers, as it offers them.</summary>
    internal static IReadOnlyList<IAstStage> Offered(bool holes) =>
        [new Nexaflow.Markdown.EventModeling.Stages.ResolveTimeline(), .. holes ? [new WithHoles(EventModelingParser.Holds)] : Array.Empty<IAstStage>()];

    /// <summary>A block read and worked over as the engine works it over.</summary>
    internal static ContentNode Staged(string source, bool holes = false) => new AstPipeline(Offered(holes)).Run(EventModelingParser.Parse(source));

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("a timeline", "eventmodeling\ntf 01 ui CartUI\ntf 02 cmd AddItem\ntf 03 evt ItemAdded"),
        ("the long forms", "eventmodeling\ntimeframe 01 ui CartUI\ntimeframe 02 command AddItem\nresetframe 03 event ItemAdded\ntimeframe 04 readmodel Cart\ntimeframe 05 processor Robot"),
        ("statements sharing a row", "eventmodeling tf 01 ui A tf 02 cmd B"),
        ("a statement over several rows", "eventmodeling\ntf\n  01\n  ui\n  CartUI"),
        ("a qualified name", "eventmodeling\ntf 01 evt Inventory.ItemAdded"),
        ("a qualified name with space round its dot", "eventmodeling\ntf 01 evt Inventory . ItemAdded"),
        ("feeds", "eventmodeling\nrf 02 evt A\nrf 03 evt B\ntf 01 rmo C ->> 02 ->> 03"),
        ("a reference, a format and data", "eventmodeling\ntf 01 rmo UserAdded [[User01]] `json`{ \"name\": \"foo\" }"),
        ("data in quotes", "eventmodeling\ntf 01 cmd A \"say\" \ntf 02 cmd B 'it'"),
        ("data running to the last brace on its row", "eventmodeling\ntf 01 cmd A { a: { b: 1 } }"),
        ("a data block, and one opened on the row under its name", "eventmodeling\ndata A {\n  x: 1\n}\ndata B\n{\n  y: 2\n}"),
        ("a data block whose indented brace does not close it", "eventmodeling\ndata A {\n  x: {\n  }\n}"),
        ("a data block holding what would be a comment", "eventmodeling\ndata A {\n  %% kept\n  // kept\n}"),
        ("a note", "eventmodeling\ntf 01 ui A\nnote 01 {\n  why it is\n}"),
        ("a note with a format", "eventmodeling\ntf 01 ui A\nnote 01 `md`{\n  **why**\n}"),
        ("a specification", "eventmodeling\nentity Added\ngwt 01 given evt Added when cmd Add then evt Added"),
        ("a specification over rows, without when", "eventmodeling\ngwt 01\n  given evt A\n  then evt B evt C"),
        ("an entity", "eventmodeling\nentity Inventory.Thing"),
        ("a title, and one closed by a comment", "eventmodeling\ntitle The cart\ntitle Shop %% note"),
        ("a title with nothing after it", "eventmodeling\ntitle"),
        ("accessibility lines", "eventmodeling\naccTitle: Cart\naccDescr: How a cart fills\naccDescr {\n  over\n  rows\n}"),
        ("every kind of comment", "eventmodeling\n%% one\n// two\n/* three\n   rows */ tf 01 ui A // after\ntf /* inside */ 02 cmd B"),
        ("a directive", "%%{init: {'theme': 'dark'}}%%\neventmodeling\ntf 01 ui A"),
        ("front matter", "---\ntitle: Cart\nconfig:\n  eventmodeling:\n    padding: 20\n  themeVariables:\n    emEventFill: '#ff0000'\n---\neventmodeling\ntf 01 evt A"),
        ("front matter holding a list", "---\nconfig:\n  - one\n  # note\n---\neventmodeling"),
        ("no header, as an eventmodeling fence needs none", "tf 01 ui A\ntf 02 cmd B"),
        ("written on Windows", "eventmodeling\r\ntf 01 ui A\r\ndata X {\r\n  a: 1\r\n}\r\n"),
        // Half written.
        ("a frame still to be named", "eventmodeling\ntf 01 ui"),
        ("a frame with no type", "eventmodeling\ntf 01"),
        ("a frame with no number", "eventmodeling\ntf"),
        ("a feed still to name its frame", "eventmodeling\ntf 01 ui A ->>"),
        ("a name ending in a dot", "eventmodeling\ntf 01 evt Inventory."),
        ("empty data", "eventmodeling\ntf 01 cmd A {}"),
        ("a reference never closed", "eventmodeling\ntf 01 cmd A [[B"),
        ("data never closed on its row", "eventmodeling\ntf 01 cmd A { x: 1\ntf 02 evt B"),
        ("a block never closed", "eventmodeling\ndata A {\n  x: 1"),
        ("a description never closed", "eventmodeling\naccDescr {\n  over"),
        ("front matter never closed", "---\ntitle: Cart\neventmodeling"),
        // What nobody means to write.
        ("a word that is no type", "eventmodeling\ntf 01 thing A"),
        ("words in capitals, which are no keywords", "eventmodeling\nTF 01 UI A"),
        ("a reserved word where a name goes", "eventmodeling\ntf 01 ui data"),
        ("words after a frame", "eventmodeling\ntf 01 ui My Cart"),
        ("a frame's data written as a block", "eventmodeling\ntf 01 cmd A {\n  x: 1\n}"),
        ("a data block written on its row", "eventmodeling\ndata A { x: 1 }"),
        ("a format nobody knows", "eventmodeling\ntf 01 cmd A `yaml`{ x }"),
        ("a specification with no then", "eventmodeling\ngwt 01 given evt A"),
        ("a clause listing nothing", "eventmodeling\ngwt 01 given then evt A"),
        ("prose", "eventmodeling\nhello there\ntf 01 ui A"),
        ("stray punctuation", "eventmodeling\n]] -> ` : ;"),
        ("nothing at all", ""),
        ("only space", "  \n \n"),
    ];

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        "eventmodeling\n\ntf 01 ui CartUI\ntf 02 cmd AddItem\ntf 03 evt ItemAdded",
        "eventmodeling\n\ntimeframe 01 ui CartUI\ntimeframe 02 command AddItem\ntimeframe 03 event ItemAdded",
        "eventmodeling\n\ntf 01 ui CartUI\ntf 02 cmd AddItem { description: string }\ntf 03 evt ItemAdded { description: string }",
        Cart,
        "eventmodeling\n\ntf 01 ui CartUI\ntf 02 cmd AddItem\ntf 03 evt ItemAdded\n\nrf 04 evt External.InventoryChanged\ntf 05 pcr InventoryProcessor\ntf 06 cmd ChangeInventory\ntf 07 evt Cart.InventoryChanged",
        "eventmodeling\n\ntf 01 evt ItemAdded\ntf 02 ui OtherUI\nrf 03 rmo CartItems ->> 01",
        "eventmodeling\n\nrf 02 evt CartCreated\nrf 03 evt ItemAdded\nrf 04 evt ItemRemoved\nrf 05 evt CartCleared\ntf 01 rmo CartUI ->> 02 ->> 03 ->> 04 ->> 05",
        "eventmodeling\n\ntf 03 evt ItemAdded\ntf 02 rmo CartItems\ntf 04 ui CartUI",
        "eventmodeling\n\ntf 03 evt External.InventoryChanged\ntf 02 pcr InventoryProcessor\ntf 04 cmd ChangeInventory\ntf 05 evt Cart.InventoryChanged",
        "eventmodeling\n\nrf 01 evt Inventory.InventoryChanged\nrf 02 evt External.InventoryChanged",
        "eventmodeling\ntf 01 rmo UserAdded `json`{ \"name\": \"foo\" }\ndata UserAdded01 `json`{\n\"name\": \"foo\"\n}",
        "---\ndescription: >-\n  A command carrying inline data and an event referencing a separate named data block.\n---\neventmodeling\n\ntf 01 ui CartScreen\ntf 02 cmd AddItem { productId: 7 }\ntf 03 evt ItemAdded [[ItemAddedData]]\n\ndata ItemAddedData\n{\n  productId: 7,\n  quantity: 1\n}",
        "eventmodeling\n\ntf 01 ui CartScreen\ntf 02 cmd AddItem\ntf 03 cmd RemoveItem\ntf 04 evt ItemChanged ->> 02 ->> 03",
        "eventmodeling\n\ntf 01 ui CartScreen\ntf 02 cmd Inventory.AddItem\ntf 03 evt Inventory.ItemAdded",
        "eventmodeling\n\ntf 01 ui CartScreen\ntf 02 cmd AddItem\ntf 03 evt ItemAdded\ntf 04 rmo CartItems ->> 03\ntf 05 evt AccountingItemAdded",
    ];

    [TestMethod]
    public void AStatementIsAsLongAsItsTokens_SoSeveralShareARowAndOneRunsOverSeveral()
    {
        Assert.AreEqual(2, Statements("eventmodeling tf 01 ui A tf 02 cmd B", EventModelingKinds.Frame).Count);

        var frame = Statements("eventmodeling\ntf\n  01\n  ui\n  CartUI", EventModelingKinds.Frame).Single();
        Assert.AreEqual("CartUI", Words(frame, EventModelingRoles.Name));
        Assert.AreEqual(0, Trouble("eventmodeling\ntf\n  01\n  ui\n  CartUI").Count);
    }

    [TestMethod]
    public void EveryCommentAndDirectiveIsTrivia_WhereverItStands()
    {
        const string Source = "%%{init: {}}%%\neventmodeling\n%% one\n// two\n/* three */ tf /* inside */ 01 ui A // after";
        var tree = Parse(Source);

        Assert.AreEqual(6, tree.SelfAndDescendants().Count(node => node.Kind is Kinds.Comment or EventModelingKinds.Directive));
        Assert.AreEqual("A", Words(Statements(Source, EventModelingKinds.Frame).Single(), EventModelingRoles.Name));
    }

    [TestMethod]
    public void WordsAreKeywordsOnlyAsTheGrammarSpellsThem_AndAReservedWordIsNeverAName()
    {
        Assert.AreEqual(0, Statements("eventmodeling\nTF 01 UI A", EventModelingKinds.Frame).Count, "the grammar's words are lowercase");

        // A reserved word where the name goes is the next statement, as the grammar's tokens make it.
        var tree = Parse("eventmodeling\ntf 01 ui data X {\n  a\n}");
        Assert.AreEqual(1, tree.Children.Count(node => node.Kind == EventModelingKinds.Data));
        Assert.AreEqual(0, tree.SelfAndDescendants().Single(node => node.Kind == EventModelingKinds.Name).Width, "the frame is still to be named");
    }

    [TestMethod]
    public void DataOnItsRowRunsToTheLastCloserThere_AndABlockToABraceStartingARow()
    {
        Assert.AreEqual(" a: { b: 1 } ", Words(Statements("eventmodeling\ntf 01 cmd A { a: { b: 1 } }", EventModelingKinds.Frame).Single(), EventModelingRoles.Said));

        var block = Statements("eventmodeling\ndata A {\n  x: {\n  }\n  %% kept\n}\ntf 01 ui B", EventModelingKinds.Data).Single();
        CollectionAssert.AreEqual(new[] { "  x: {", "  }", "  %% kept" },
                                  block.SelfAndDescendants().Where(node => node.Role == EventModelingRoles.Said).Select(node => node.Text).ToArray());
        Assert.AreEqual(1, Statements("eventmodeling\ndata A {\n  x: {\n  }\n  %% kept\n}\ntf 01 ui B", EventModelingKinds.Frame).Count);
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("eventmodeling\ntf 01 thing A", "entity type is"),
                     ("eventmodeling\ntf 01", "A frame is its word"),
                     ("eventmodeling\ntf 01 ui A ->>", "->> names the number"),
                     ("eventmodeling\ntf 01 evt Inventory.", "joined by dots"),
                     ("eventmodeling\ntf 01 cmd A [[B", "double brackets"),
                     ("eventmodeling\ntf 01 cmd A { x: 1", "never closed with }"),
                     ("eventmodeling\ndata A {\n  x: 1", "never closed: a }"),
                     ("eventmodeling\ntf 01 cmd A {\n  x: 1\n}", "written on its row"),
                     ("eventmodeling\ndata A { x: 1 }", "A data block is data"),
                     ("eventmodeling\ntf 01 cmd A `yaml`{ x }", "A data format is"),
                     ("eventmodeling\ngwt 01 given evt A", "A specification is gwt"),
                     ("eventmodeling\nhello there", "An event model is its frames"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void AFrameStillToBeNamedOrGivenDataHasAHoleWhereItGoes_AndNothingWrong()
    {
        foreach (var source in new[] { "eventmodeling\ntf 01 ui", "eventmodeling\ntf 01 cmd A {}" })
        {
            Assert.AreEqual(0, Trouble(source).Count, source);
            Assert.AreEqual(1, Staged(source, holes: true).SelfAndDescendants().Count(node => node.Kind == Kinds.Hole), source);
        }
    }

    [TestMethod]
    public void TheFrontMatterIsReadARowAtATime_KeyAndValue()
    {
        var settings = Parse("---\ntitle: Cart\nconfig:\n  eventmodeling:\n    padding: 20\n---\neventmodeling").SelfAndDescendants()
                           .Where(node => node.Kind == EventModelingKinds.Setting)
                           .Select(node => $"{node.Part(Roles.Name)?.Text}={node.Part(EventModelingRoles.Setting)?.Text}")
                           .ToArray();

        CollectionAssert.AreEqual(new[] { "title=Cart", "config=", "eventmodeling=", "padding=20" }, settings);
    }

    private static List<ContentNode> Statements(string source, string kind) =>
        [.. EventModelingParser.Parse(source).Children.Where(node => node.Kind == kind)];

    private static string? Words(ContentNode statement, string role) =>
        statement.SelfAndDescendants().FirstOrDefault(node => node.Role == role && node.IsLeaf)?.Text;

    private static List<string> Trouble(string source) =>
        [.. Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
