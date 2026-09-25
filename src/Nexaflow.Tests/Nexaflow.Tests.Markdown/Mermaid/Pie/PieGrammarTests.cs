using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Pie;

/// <summary>
/// What a <c>pie</c> block is read into: <c>showData</c> and a title after the keyword, a slice per line as its label,
/// its colon and its value — and what is written instead of any of that, held with the reason.
/// </summary>
[TestClass]
[CoversNode("pie-ast")]
public class PieGrammarTests : MermaidGrammarContract
{
    /// <summary>The block the Mermaid documentation shows, front matter and all.</summary>
    public const string Documented =
        """
        ---
        config:
          pie:
            textPosition: 0.5
            donutHole: 0.2
            highlightSlice: Potassium
          themeVariables:
            pieOuterStrokeWidth: "5px"
        ---
        pie showData
            title Key elements in Product X
            "Calcium" : 42.96
            "Potassium" : 50.05
            "Magnesium" : 10.01
            "Iron" :  5
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Pie;

    protected override IEnumerable<string> DocumentedBlocks => [Documented];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documented block", Documented),
        ("a title on the header", "pie title Pets\n  \"Dogs\" : 386"),
        ("showData and nothing else", "pie showData\n  \"Dogs\" : 386"),
        ("no options at all", "pie\n  \"Dogs\" : 386\n  \"Cats\" : 85"),
        ("a comment and a blank line", "pie\n\n  %% the dogs\n  \"Dogs\" : 386"),
        ("accessibility lines", "pie\n  accTitle: Pets\n  accDescr: How many of each\n  \"Dogs\" : 386"),
        ("space around the colon", "pie\n  \"Dogs\"   :   386  "),
        ("no space around the colon", "pie\n  \"Dogs\":386"),
        ("a label with a colon in it", "pie\n  \"Dogs: big ones\" : 386"),
        ("an empty label", "pie\n  \"\" : 386"),
        // What nobody means to write.
        ("a negative value", "pie\n  \"Dogs\" : -386"),
        ("a value of nought", "pie\n  \"Dogs\" : 0"),
        ("a value that is not a number", "pie\n  \"Dogs\" : lots"),
        ("no value at all", "pie\n  \"Dogs\" :"),
        ("no value yet, and the space left for it", "pie\n  \"Dogs\" : "),
        ("a slice just started among others", "pie\n  \"Dogs\" : 3\n  \"\" : \n  \"Cats\" : 1"),
        ("written on Windows", "pie\r\n  \"Dogs\" : 3  \r\n  \"Cats\" : \r\n"),
        ("no colon", "pie\n  \"Dogs\" 386"),
        ("a label never closed", "pie\n  \"Dogs : 386"),
        ("no quotes", "pie\n  Dogs : 386"),
        ("options nobody knows", "pie wibble\n  \"Dogs\" : 386"),
        ("nothing but the keyword", "pie"),
    ];

    [TestMethod]
    public void ASliceIsItsLabelItsColonAndItsValue()
    {
        var slice = Slices("pie\n  \"Calcium\" : 42.96").Single();

        Assert.AreEqual("Calcium", slice.SelfAndDescendants().Single(node => node.Kind == MermaidKinds.Words).Text);
        Assert.AreEqual("42.96", Value(slice).Text);
        Assert.IsNull(slice.SelfAndDescendants().FirstOrDefault(node => node.Trouble is not null));
    }

    [TestMethod]
    public void EverySliceOfTheDocumentedBlockIsRead()
    {
        var slices = Slices(Documented);

        CollectionAssert.AreEqual(
            new[] { "Calcium", "Potassium", "Magnesium", "Iron" },
            slices.Select(slice => slice.SelfAndDescendants().Single(node => node.Kind == MermaidKinds.Words).Text).ToArray(),
            "clockwise, in the order they were written");

        CollectionAssert.AreEqual(
            new[] { "42.96", "50.05", "10.01", "5" },
            slices.Select(slice => Value(slice).Text).ToArray());

        Assert.IsFalse(MermaidParser.Parse(Documented).SelfAndDescendants().Any(node => node.Trouble is not null),
                       "and nothing in it is a complaint");
    }

    [TestMethod]
    public void TheHeaderSaysWhetherTheValuesAreShownAndWhatTheChartIsCalled()
    {
        var options = Nodes(Documented, PieKinds.Options).Single();
        Assert.AreEqual(PieGrammar.ShowData, options.SelfAndDescendants().Single(node => node.Kind == PieKinds.ShowData).Text);

        var title = Nodes(Documented, MermaidKinds.Title).Single();
        Assert.AreEqual("Key elements in Product X", title.Part(MermaidRoles.Title)?.Text);
    }

    [TestMethod]
    public void ATitleOnTheHeaderReadsTheSameAsOneOnItsOwnLine()
    {
        var header = Nodes("pie title Pets\n  \"Dogs\" : 386", MermaidKinds.Title).Single();
        var line = Nodes("pie\n  title Pets\n  \"Dogs\" : 386", MermaidKinds.Title).Single();

        Assert.AreEqual("Pets", header.Part(MermaidRoles.Title)?.Text);
        Assert.AreEqual("Pets", line.Part(MermaidRoles.Title)?.Text);
    }

    [TestMethod]
    public void AValueThatIsNotWorthAnythingSaysSoOnTheNumber()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("pie\n  \"Dogs\" : -386", "more than nought"),
                     ("pie\n  \"Dogs\" : 0", "more than nought"),
                     ("pie\n  \"Dogs\" : lots", "not a number"),
                 })
        {
            var value = Value(Slices(source).Single());
            StringAssert.Contains(value.Trouble, reason, source);
        }
    }

    [TestMethod]
    public void ALineThatIsNoSliceIsHeldWithTheReason()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("pie\n  \"Dogs\" 386", "colon"),
                     ("pie\n  \"Dogs : 386", "never closed"),
                     ("pie\n  Dogs : 386", "label in quotes"),
                 })
        {
            var held = MermaidParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null);
            Assert.AreEqual(Kinds.Verbatim, held.Kind, source);
            StringAssert.Contains(held.Trouble, reason, source);
        }
    }

    [TestMethod]
    public void ASliceWithNoValueYetIsStillASlice()
    {
        foreach (var source in new[] { "pie\n  \"Dogs\" :", "pie\n  \"Dogs\" : ", "pie\n  \"Dogs\" : \n  \"Cats\" : 1" })
        {
            var slice = Slices(source).First();

            Assert.AreEqual(string.Empty, Value(slice).Text, source);
            Assert.IsNull(slice.SelfAndDescendants().FirstOrDefault(node => node.Trouble is not null),
                          $"{source}: nothing is wrong with it, it is only not finished");
        }
    }

    [TestMethod]
    public void AndItsValueStandsAfterTheSpaceLeftForIt()
    {
        // Where the value is read as standing is where typing it puts it: past the space, not hard against the colon.
        foreach (var source in new[] { "pie\n  \"Dogs\" : ", "pie\n  \"Dogs\" : \n  \"Cats\" : 1" })
        {
            var value = MermaidParser.Parse(source).Placed().First(place => place.Node.Kind == MermaidKinds.Number);
            Assert.AreEqual(source.IndexOf(": ", StringComparison.Ordinal) + 2, value.Start, source);
        }
    }

    [TestMethod]
    public void TheSpaceAfterAnythingWrittenIsTheLinesOwn()
    {
        const string source = "pie\n  \"Dogs\" : 386  \n  title Pets  ";

        Assert.AreEqual("\"Dogs\" : 386", Slices(source).Single().Print());
        Assert.AreEqual("title Pets", Nodes(source, MermaidKinds.Title).Single().Print());
    }

    [TestMethod]
    public void ANewSliceIsALabelAndAValueStillToWrite()
    {
        var (text, caret) = new PieGrammar().Blank(above: null)!.Value;
        var slice = Slices("pie\n  " + text).Single();

        Assert.AreEqual(string.Empty, slice.SelfAndDescendants().Single(node => node.Kind == MermaidKinds.Words).Text);
        Assert.AreEqual(string.Empty, Value(slice).Text);
        Assert.AreEqual('"', text[caret - 1], "the caret starts inside the label's quotes");
    }

    [TestMethod]
    public void AQuoteTypedIntoALabelIsWrittenAsItsEntityCode()
    {
        const string source = "pie\n  \"Dogs\" : 3";
        var label = ContentReading.Of(MermaidStaged.Read(source)).Root.SelfAndDescendants().Single(part => part.Kind == MermaidKinds.Words);

        var writing = new PieGrammar().Escaping(label, label.End, "\"")!.Value;
        var written = source[..writing.Start] + writing.Text + source[writing.End..];

        Assert.AreEqual("pie\n  \"Dogs#quot;\" : 3", written);
        Assert.IsNull(new PieGrammar().Escaping(label, label.End, "s"), "and anything else goes in as it is");
        Assert.AreEqual("Dogs\"", MermaidText.Decode(PieChart.Of(MermaidStaged.Read(written)).Slices.Single().Name), "which reads as the quote typed");
    }

    [TestMethod]
    public void OptionsNobodyKnowsAreHeldWithTheReason()
    {
        var held = MermaidParser.Parse("pie wibble\n  \"Dogs\" : 386")
            .SelfAndDescendants().Single(node => node.Trouble is not null);

        Assert.AreEqual("wibble", held.Text);
        StringAssert.Contains(held.Trouble, "showData");
    }

    private static List<ContentNode> Slices(string source) => Nodes(source, PieKinds.Slice);

    private static List<ContentNode> Nodes(string source, string kind) =>
        [.. MermaidParser.Parse(source).SelfAndDescendants().Where(node => node.Kind == kind)];

    private static ContentNode Value(ContentNode slice) =>
        slice.SelfAndDescendants().Single(node => node.Kind == MermaidKinds.Number);
}
