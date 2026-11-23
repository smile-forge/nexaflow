using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Markdown.Mermaid.Pie.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Pie;

/// <summary>
/// What <see cref="PieParser"/> reads a pie into: <c>showData</c> and a title after the keyword, a slice per line as its label,
/// its colon and its value — and what is written instead of any of that, held with the reason. The header is optional, since a
/// <c>pie</c> fence already says what the block is.
/// </summary>
[TestClass]
[CoversNode("pie-ast")]
public class PieParserTests : ContentParserContract
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

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => PieParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Documented,
        "pie showData %% legend shows values\n\"A\" : 1\n",
        "pie title Pets %% the household\n\"Dogs\" : 3\n",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documented block", Documented),
        ("a title on the header", "pie title Pets\n  \"Dogs\" : 386"),
        ("showData and nothing else", "pie showData\n  \"Dogs\" : 386"),
        ("no options at all", "pie\n  \"Dogs\" : 386\n  \"Cats\" : 85"),
        ("a comment and a blank line", "pie\n\n  %% the dogs\n  \"Dogs\" : 386"),
        ("a comment after a slice", "pie\n  \"Dogs\" : 386 %% the most"),
        ("accessibility lines", "pie\n  accTitle: Pets\n  accDescr: How many of each\n  \"Dogs\" : 386"),
        ("space around the colon", "pie\n  \"Dogs\"   :   386  "),
        ("no space around the colon", "pie\n  \"Dogs\":386"),
        ("a label with a colon in it", "pie\n  \"Dogs: big ones\" : 386"),
        ("a label in single quotes", "pie\n  'Dogs' : 386"),
        ("quotes a backslash keeps", "pie\n  \"say \\\"woof\\\"\" : 3\n  'it\\'s a cat' : 1"),
        ("a backslash kept", "pie\n  \"a\\\\b\" : 1"),
        ("a label across a line ending", "pie\n  \"Big\ndogs\" : 386\n  \"Cats\" : 1"),
        ("a comment written inside a label", "pie\n  \"50%% off\" : 1"),
        ("an empty label", "pie\n  \"\" : 386"),
        ("a value of nought", "pie\n  \"Dogs\" : 386\n  \"Cats\" : 0"),
        ("no header, as a pie fence holds it", "\"Dogs\" : 386\n\"Cats\" : 85"),
        ("a binding supplying slices", "pie\n  \"Dogs\" : 386\n  {{Slices}}\n"),
        // Half written.
        ("no value at all", "pie\n  \"Dogs\" :"),
        ("no value yet, and the space left for it", "pie\n  \"Dogs\" : "),
        ("a slice just started among others", "pie\n  \"Dogs\" : 3\n  \"\" : \n  \"Cats\" : 1"),
        ("a title with nothing after it yet", "pie\n  title"),
        ("written on Windows", "pie\r\n  \"Dogs\" : 3  \r\n  \"Cats\" : \r\n"),
        ("a label across a line ending, written on Windows", "pie\r\n  \"Big\r\ndogs\" : 3\r\n"),
        ("nothing but the keyword", "pie"),
        // What nobody means to write.
        ("a negative value", "pie\n  \"Dogs\" : -386"),
        ("a value that is not a number", "pie\n  \"Dogs\" : lots"),
        ("no colon", "pie\n  \"Dogs\" 386"),
        ("a label never closed", "pie\n  \"Cats\" : 1\n  \"Dogs : 386"),
        ("a quote closed only by the next line's", "pie\n  \"Dogs : 386\n  \"Cats\" : 1"),
        ("no quotes", "pie\n  Dogs : 386"),
        ("options nobody knows", "pie wibble\n  \"Dogs\" : 386"),
    ];

    /// <summary>
    /// The stages a pie's language offers the engine (<c>Shipped.Pie</c>), named again here — what the engine adds for every
    /// language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes)
    {
        var config = PieConfig.Read(MermaidBlock.Of(tree).Config);

        return
        [
            new ResolveSlices(config),
            new ResolveShares(holes),
            new WithConfig<PieConfig>(config),
            .. holes ? [new WithHoles(PieParser.Holds)] : Array.Empty<IAstStage>(),
        ];
    }

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = PieParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void ASliceIsItsLabelItsColonAndItsValue()
    {
        var slice = Slices("pie\n  \"Calcium\" : 42.96").Single();

        Assert.AreEqual("Calcium", slice.SelfAndDescendants().Single(node => node.Kind == Kinds.Words).Text);
        Assert.AreEqual("42.96", Value(slice).Text);
        Assert.IsNull(slice.SelfAndDescendants().FirstOrDefault(node => node.Trouble is not null));
    }

    [TestMethod]
    public void EverySliceOfTheDocumentedBlockIsRead()
    {
        var slices = Slices(Documented);

        CollectionAssert.AreEqual(
            new[] { "Calcium", "Potassium", "Magnesium", "Iron" },
            slices.Select(slice => slice.SelfAndDescendants().Single(node => node.Kind == Kinds.Words).Text).ToArray(),
            "clockwise, in the order they were written");

        CollectionAssert.AreEqual(
            new[] { "42.96", "50.05", "10.01", "5" },
            slices.Select(slice => Value(slice).Text).ToArray());

        Assert.IsFalse(PieParser.Parse(Documented).SelfAndDescendants().Any(node => node.Trouble is not null),
                       "and nothing in it is a complaint");
    }

    [TestMethod]
    public void TheHeaderSaysWhetherTheValuesAreShownAndWhatTheChartIsCalled()
    {
        var options = Nodes(Documented, PieKinds.Options).Single();
        Assert.AreEqual(PieParser.ShowData, options.SelfAndDescendants().Single(node => node.Kind == PieKinds.ShowData).Text);

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
    public void TheHeaderIsOptional_SinceAPieFenceSaysWhatTheBlockIs()
    {
        var tree = PieParser.Parse("\"Dogs\" : 386\n\"Cats\" : 85");

        Assert.AreEqual(PieParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(2, Nodes("\"Dogs\" : 386\n\"Cats\" : 85", PieKinds.Slice).Count, "the first line is a slice, not a header gone wrong");
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void ALabelIsReadInEitherQuote_ABackslashKeepingWhatWouldEndIt()
    {
        var slices = Slices("pie\n  \"say \\\"woof\\\"\" : 3\n  'it\\'s a cat' : 1");

        Assert.AreEqual(2, slices.Count);
        Assert.AreEqual(2, slices[0].SelfAndDescendants().Count(node => node.Kind == PieKinds.Escape), "each kept quote a piece of its own");
        Assert.AreEqual("say \"woof\"", PieStandIns.Reads(Said(slices[0])));
        Assert.AreEqual("it's a cat", PieStandIns.Reads(Said(slices[1])));
        Assert.IsFalse(slices.SelectMany(slice => slice.SelfAndDescendants()).Any(node => node.Trouble is not null));
    }

    [TestMethod]
    public void ALabelRunsAcrossALineEndingToItsClosingQuote_AndTheSliceIsOneLineOfTheTree()
    {
        const string source = "pie\n  \"Big\ndogs\" : 386\n  \"Cats\" : 1";
        var tree = PieParser.Parse(source);
        var slices = Slices(source);

        Assert.AreEqual(2, slices.Count);
        Assert.AreEqual(1, slices[0].SelfAndDescendants().Count(node => node.Kind == PieKinds.Break));
        Assert.AreEqual("Big dogs", PieStandIns.Reads(Said(slices[0])), "a line ending is drawn as a space");
        Assert.AreEqual(3, tree.Children.Count, "the header, the slice across two rows, and the one after it");
    }

    [TestMethod]
    public void WhatALabelSaysIsTheLabels_CommentAndAll()
    {
        var slice = Slices("pie\n  \"50%% off\" : 1 %% sale").Single();

        Assert.AreEqual("50%% off", Said(slice).Text);
        Assert.AreEqual("1", Value(slice).Text);
        Assert.AreEqual("%% sale", slice.SelfAndDescendants().Single(node => node.Kind == Kinds.Comment).Text);
    }

    [TestMethod]
    public void AQuoteNothingClosesIsHeldWithTheReason_AndTheSlicesAboveItStand()
    {
        // A quote is closed by the next one written, however many lines on: only one nothing closes is a character like any other.
        const string source = "pie\n  \"Cats\" : 1\n  \"Dogs : 386";

        Assert.AreEqual("Cats", Said(Slices(source).Single()).Text);
        StringAssert.Contains(PieParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null).Trouble, "never closed");
    }

    [TestMethod]
    public void WrittenIntoALabel_WhatWouldEndItIsKept()
    {
        Assert.AreEqual("say #quot;hi#quot; a\\\\b", PieParser.Escaped("say \"hi\" a\\b", '"'), "a quote as its entity code, as in any diagram, and a backslash by another");
        Assert.AreEqual("it\\'s \"so\"", PieParser.Escaped("it's \"so\"", '\''), "in single quotes, that quote by a backslash");
        Assert.AreEqual("one<br>two", PieParser.Escaped("one\ntwo", '"'), "and a line break as the mark that writes one");
    }

    [TestMethod]
    public void ASliceWorthNoughtIsASliceLikeAnyOther()
    {
        // Mermaid's own pie takes nought, which is what a count of nothing — nothing broken, say — is written as.
        var slice = Slices("pie\n  \"Faulted\" : 0").Single();

        Assert.AreEqual("0", Value(slice).Text);
        Assert.IsNull(slice.SelfAndDescendants().FirstOrDefault(node => node.Trouble is not null));
    }

    [TestMethod]
    public void AValueThatIsNotWorthAnythingSaysSoOnTheNumber()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("pie\n  \"Dogs\" : -386", "less than nought"),
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
            var held = PieParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null);
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
            var value = PieParser.Parse(source).Placed().First(place => place.Node.Kind == Kinds.Number);
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
    public void OptionsNobodyKnowsAreHeldWithTheReason()
    {
        var held = PieParser.Parse("pie wibble\n  \"Dogs\" : 386")
            .SelfAndDescendants().Single(node => node.Trouble is not null);

        Assert.AreEqual("wibble", held.Text);
        StringAssert.Contains(held.Trouble, "showData");
    }

    private static List<ContentNode> Slices(string source) => Nodes(source, PieKinds.Slice);

    private static List<ContentNode> Nodes(string source, string kind) =>
        [.. PieParser.Parse(source).SelfAndDescendants().Where(node => node.Kind == kind)];

    private static ContentNode Value(ContentNode slice) =>
        slice.SelfAndDescendants().Single(node => node.Kind == Kinds.Number);

    /// <summary>What a slice's label says, as the words it is written with.</summary>
    private static ContentNode Said(ContentNode slice) =>
        slice.Children.Single(child => child.Kind == MermaidKinds.Quoted).Children.Single(child => child.Kind == Kinds.Words);
}
