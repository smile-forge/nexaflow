using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Radar;
using Nexaflow.Markdown.Mermaid.Radar.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Radar;

/// <summary>
/// What <see cref="RadarParser"/> reads a radar chart into: its axes, its curves and their values, its options — which axis each
/// value is for, and what is said where a curve's values do not fit its axes — and what is written instead of any of that, held with
/// the reason. The header is optional, since a <c>radar</c> fence already says what the block is.
/// </summary>
[TestClass]
[CoversNode("radar-ast")]
public class RadarParserTests : ContentParserContract
{
    /// <summary>The first chart the Mermaid documentation shows: titled in its front matter, two lines of axes.</summary>
    public const string Grades =
        """
        ---
        title: "Grades"
        ---
        radar-beta
          axis m["Math"], s["Science"], e["English"]
          axis h["History"], g["Geography"], a["Art"]
          curve a["Alice"]{85, 90, 80, 70, 75, 90}
          curve b["Bob"]{70, 75, 85, 80, 90, 85}

          max 100
          min 0
        """;

    /// <summary>The second: a title of its own, and a polygon graticule.</summary>
    public const string Restaurants =
        """
        radar-beta
          title Restaurant Comparison
          axis food["Food Quality"], service["Service"], price["Price"]
          axis ambiance["Ambiance"]

          curve a["Restaurant A"]{4, 3, 2, 4}
          curve b["Restaurant B"]{3, 4, 3, 3}
          curve c["Restaurant C"]{2, 3, 4, 2}
          curve d["Restaurant D"]{2, 2, 4, 3}

          graticule polygon
          max 5
        """;

    /// <summary>The documentation's example of config and theme.</summary>
    public const string Themed =
        """
        ---
        config:
          radar:
            axisScaleFactor: 0.25
            curveTension: 0.1
          theme: base
          themeVariables:
            cScale0: "#FF0000"
            cScale1: "#00FF00"
            cScale2: "#0000FF"
            radar:
              curveOpacity: 0
        ---
        radar-beta
          axis A, B, C, D, E
          curve c1{1,2,3,4,5}
          curve c2{5,4,3,2,1}
          curve c3{3,3,3,3,3}
        """;

    /// <summary>The documentation's curves: labelled and not, several to a line, and one naming the axis each value is for.</summary>
    public const string Details =
        """
        radar-beta
          axis axis1, axis2, axis3
          curve id1["Label1"]{1, 2, 3}
          curve id2["Label2"]{4, 5, 6}, id3{7, 8, 9}
          curve id4{ axis3: 30, axis1: 20, axis2: 10 }
        """;

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => RadarParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        "radar-beta\n  axis a, b, c curve x{1, 2, 3}\n  max 5 min 0\n",
        "radar-beta\n  title The axis of every curve\n  axis a, b, c\n  curve x{1, 2, 3}",
        "radar-beta\n  axis m, s\n  curve a{ m 85, s 90 }",
        "radar-beta %% grades\n  axis a\n  curve x{1}",
        "radar-beta title Grades\n  axis a\n  curve x{1}",
        "radar-beta\n  axis a, b title Grades\n  curve x{1, 2}",
        Grades,
        Restaurants,
        Themed,
        Details,
        "radar-beta\naxis A, B, C, D, E\ncurve c1{1,2,3,4,5}\ncurve c2{5,4,3,2,1}",
        "radar-beta\n  axis A, B, C\n  curve a{1, 2, 3}\n  showLegend true\n  max 100\n  min 0\n  graticule circle\n  ticks 5",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("a colon after the header", "radar-beta:\n  axis a, b\n  curve c{1, 2}"),
        ("a title", "radar-beta\n  title Skills\n  axis a"),
        ("a comment and a blank line", "radar-beta\n\n  %% the axes\n  axis a, b %% two of them\n  curve c{1, 2}"),
        ("accessibility lines", "radar-beta\n  accTitle: Skills\n  accDescr: Who knows what\n  axis a"),
        ("labels without quotes", "radar-beta\n  axis m[Math], s[ Science ]"),
        ("labels in single quotes", "radar-beta\n  axis m['Math'], s['Science']"),
        ("quotes a backslash keeps", "radar-beta\n  axis m[\"say \\\"x\\\"\"], s['it\\'s']"),
        ("a label across a line ending", "radar-beta\n  axis m[\"Pure\nMath\"], s\n  curve c{1, 2}"),
        ("a comment written inside a label", "radar-beta\n  axis m[\"50%% done\"]"),
        ("names in quotes", "radar-beta\n  axis \"two words\"[\"Two\"], b\n  curve c{ \"two words\": 1, b: 2 }"),
        ("space everywhere", "radar-beta\n  axis  a [ \"A\" ] ,  b\n  curve c [ \"C\" ] { a : 1 , b : 2 }  "),
        ("no space anywhere", "radar-beta\n  axis a[\"A\"],b\n  curve c[\"C\"]{1,2}"),
        ("values over several lines", "radar-beta\n  axis a, b, c\n  curve x{\n    1,\n    2,\n    3\n  }\n  max 5"),
        ("values over several lines, with comments", "radar-beta\n  axis a, b\n  curve x{ %% first\n    a: 1, %% the a\n    b: 2\n  } %% done"),
        ("options sharing a line", "radar-beta\n  axis a, b, c\n  curve x{1, 2, 3}\n  max 10, min 0, ticks 4, graticule polygon, showLegend false"),
        ("decimal values", "radar-beta\n  axis a, b\n  curve x{1.5, 0.25}"),
        ("axes written under the curves", "radar-beta\n  curve x{1, 2}\n  axis a, b"),
        ("written on Windows", "radar-beta\r\n  axis a, b  \r\n  curve x{\r\n    1, 2\r\n  }\r\n"),
        ("no header, as a radar fence holds it", "axis a, b\ncurve x{1, 2}"),
        ("a binding supplying lines", "radar-beta\n  axis a, b\n  {{Curves}}\n"),
        // Half written.
        ("an axis line naming nothing yet", "radar-beta\n  axis "),
        ("an axis still to name after a comma", "radar-beta\n  axis a, "),
        ("a curve still to name", "radar-beta\n  axis a\n  curve "),
        ("a curve with no values yet", "radar-beta\n  axis a, b\n  curve c"),
        ("values never closed", "radar-beta\n  axis a, b\n  curve c{1, "),
        ("values never closed, another curve under them", "radar-beta\n  axis a, b\n  curve c{1, \n  curve d{1, 2}"),
        ("a value still to come", "radar-beta\n  axis a, b\n  curve c{1, }"),
        ("an option set to nothing yet", "radar-beta\n  axis a\n  max "),
        ("an empty label", "radar-beta\n  axis a[\"\"]"),
        ("a title with nothing after it yet", "radar-beta\n  title"),
        // What nobody means to write.
        ("too few values", "radar-beta\n  axis a, b, c\n  curve x{1, 2}"),
        ("too many values", "radar-beta\n  axis a, b\n  curve x{1, 2, 3}"),
        ("a value naming no axis", "radar-beta\n  axis a, b\n  curve x{ a: 1, z: 2 }"),
        ("a value for the same axis twice", "radar-beta\n  axis a, b\n  curve x{ a: 1, a: 2 }"),
        ("values of both kinds", "radar-beta\n  axis a, b\n  curve x{ a: 1, 2 }"),
        ("an axis written twice", "radar-beta\n  axis a, a"),
        ("a negative value", "radar-beta\n  axis a, b\n  curve x{-1, 2}"),
        ("a value that is not a number", "radar-beta\n  axis a, b\n  curve x{lots, 2}"),
        ("a graticule nobody knows", "radar-beta\n  axis a\n  graticule star"),
        ("showLegend neither true nor false", "radar-beta\n  axis a\n  showLegend yes"),
        ("ticks that are not whole", "radar-beta\n  axis a\n  ticks 2.5"),
        ("a name starting with a digit", "radar-beta\n  axis 1a"),
        ("a label never closed", "radar-beta\n  axis a[\"Math"),
        ("a label in quotes with no bracket after it", "radar-beta\n  axis a[\"Math\" b"),
        ("an option nobody knows", "radar-beta\n  axis a\n  maxi 3"),
        ("a keyword run into its name", "radar-beta\n  axisa"),
        ("something after a curve", "radar-beta\n  axis a\n  curve x{1} y"),
        ("words after the header", "radar-beta wibble\n  axis a"),
        ("nothing but the keyword", "radar-beta"),
    ];

    /// <summary>
    /// The stages a radar chart's language offers the engine (<c>Shipped.Radar</c>), named again here — what the engine adds for
    /// every language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes)
    {
        var config = RadarConfig.Read(MermaidBlock.Of(tree).Config);

        return
        [
            new ResolveCurves(config, holes),
            new ResolveOptions(config),
            .. holes ? [new WithHoles(RadarParser.Holds)] : Array.Empty<IAstStage>(),
        ];
    }

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = RadarParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void AnAxisIsItsNameAndItsLabel_SeveralToALine()
    {
        var axes = Nodes(Grades, RadarKinds.Axis);

        CollectionAssert.AreEqual(new[] { "m", "s", "e", "h", "g", "a" }, axes.Select(Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Math", "Science", "English", "History", "Geography", "Art" }, axes.Select(Label).ToArray());
    }

    [TestMethod]
    public void ALabelIsReadInEitherQuote_ABackslashKeepingWhatWouldEndIt()
    {
        var axes = Nodes("radar-beta\n  axis m[\"say \\\"x\\\"\"], s['it\\'s'], t[ 'spaced' ]", RadarKinds.Axis);

        CollectionAssert.AreEqual(new[] { "say \"x\"", "it's", "spaced" }, axes.Select(Label).ToArray());
        Assert.AreEqual(2, axes[0].SelfAndDescendants().Count(node => node.Kind == RadarKinds.Escape), "each kept quote a piece of its own");
        Assert.AreEqual(0, Trouble("radar-beta\n  axis m[\"say \\\"x\\\"\"], s['it\\'s'], t[ 'spaced' ]").Count);
    }

    [TestMethod]
    public void ALabelRunsAcrossALineEndingToItsClosingQuote_AndTheAxisLineIsOneLineOfTheTree()
    {
        const string source = "radar-beta\n  axis m[\"Pure\nMath\"], s\n  curve c{1, 2}";

        Assert.AreEqual("Pure Math", Label(Nodes(source, RadarKinds.Axis)[0]), "a line ending is drawn as a space");
        Assert.AreEqual(1, Nodes(source, RadarKinds.Axis)[0].SelfAndDescendants().Count(node => node.Kind == RadarKinds.Break));
        Assert.AreEqual(3, RadarParser.Parse(source).Children.Count, "the header, the axes across two rows, and the curve");
    }

    [TestMethod]
    public void ACurvesValuesMayRunOverSeveralLines_ACommentOnAnyOfThem()
    {
        CollectionAssert.AreEqual(new double?[] { 1, 2, 3 }, Points("radar-beta\n  axis a, b, c\n  curve x{\n    1,\n    2,\n    3\n  }\n  max 5", "x"));

        const string commented = "radar-beta\n  axis a, b\n  curve x{ %% first\n    a: 1, %% the a\n    b: 2\n  } %% done";
        CollectionAssert.AreEqual(new double?[] { 1, 2 }, Points(commented, "x"));
        Assert.AreEqual(0, Trouble(commented).Count, string.Join(" | ", Trouble(commented)));
        Assert.AreEqual(3, Nodes(commented, Kinds.Comment).Count, "each comment a piece of its own");
    }

    [TestMethod]
    public void BracesNeverClosedEndWithTheirLine_SoTheCurveUnderThemIsStillRead()
    {
        const string source = "radar-beta\n  axis a, b\n  curve c{1, \n  curve d{1, 2}";

        CollectionAssert.AreEqual(new double?[] { 1, 2 }, Points(source, "d"));
        StringAssert.Contains(Trouble(source).Single(said => said.Contains("never closed", StringComparison.Ordinal)), "}");
    }

    [TestMethod]
    public void ANumberIsForTheAxisInItsPlace_AndAValueNamingItsAxisIsForThatAxis()
    {
        CollectionAssert.AreEqual(new double?[] { 1, 2, 3 }, Points(Details, "id1"));
        CollectionAssert.AreEqual(new double?[] { 7, 8, 9 }, Points(Details, "id3"), "a curve sharing its line with another has values of its own");
        CollectionAssert.AreEqual(new double?[] { 20, 10, 30 }, Points(Details, "id4"), "each for the axis it names, whatever the order it is written in");
    }

    [TestMethod]
    public void AxesWrittenUnderACurveAreStillTheAxesItsValuesAreFor() =>
        CollectionAssert.AreEqual(new double?[] { 1, 2 }, Points("radar-beta\n  curve x{1, 2}\n  axis a, b", "x"));

    [TestMethod]
    public void TheHeaderIsOptional_SinceARadarFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("axis a, b\ncurve x{1, 2}");

        Assert.AreEqual(RadarParser.Language, ((BlockNode)tree).Language);
        CollectionAssert.AreEqual(new double?[] { 1, 2 }, Points("axis a, b\ncurve x{1, 2}", "x"), "the first line is an axis line, not a header gone wrong");
        Assert.AreEqual(0, Trouble("axis a, b\ncurve x{1, 2}").Count);
    }

    [TestMethod]
    public void ACurveThatDoesNotGiveEachAxisOneValueSaysSo()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("radar-beta\n  axis a, b, c\n  curve x{1, 2}", "2 values for 3 axes"),
                     ("radar-beta\n  axis a, b\n  curve x{1, 2, 3}", "3 values for 2 axes"),
                     ("radar-beta\n  axis a, b\n  curve x{ a: 1 }", "no value for b"),
                     ("radar-beta\n  axis a, b\n  curve x{ a: 1, z: 2 }", "No axis called z"),
                     ("radar-beta\n  axis a, b\n  curve x{ a: 1, a: 2 }", "already gives a a value"),
                     ("radar-beta\n  axis a, b\n  curve x{ a: 1, 2 }", "names the axis it is for"),
                     ("radar-beta\n  axis a, a", "already written"),
                 })
        {
            var trouble = Trouble(source);
            Assert.IsTrue(trouble.Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", trouble)}");
        }
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[]
                 {
                     "radar-beta\n  axis ",
                     "radar-beta\n  axis a, ",
                     "radar-beta\n  axis a, b\n  curve ",
                     "radar-beta\n  axis a, b\n  curve c",
                     "radar-beta\n  axis a, b\n  curve c{1, }",
                     "radar-beta\n  axis a\n  max ",
                     "radar-beta\n  axis a[\"\"]",
                     "radar-beta\n  title",
                 })
            Assert.AreEqual(0, Trouble(source).Count, $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void ValuesNeverClosedAreStillRead_WithTheReason()
    {
        const string source = "radar-beta\n  axis a, b\n  curve c{1, 2";

        CollectionAssert.AreEqual(new double?[] { 1, 2 }, Points(source, "c"));
        StringAssert.Contains(Trouble(source).Single(), "never closed");
    }

    [TestMethod]
    public void AnAxisStillToNameStandsWhereTypingItsNamePutsIt()
    {
        const string source = "radar-beta\n  axis a, ";
        var name = RadarParser.Parse(source).Placed().Last(place => place.Node.Kind == Kinds.Words);

        Assert.AreEqual(source.Length, name.Start);
        Assert.AreEqual(string.Empty, name.Node.Text);
    }

    [TestMethod]
    public void AnOptionSaysWhatIsWrongWithWhatItIsSetTo()
    {
        foreach (var (line, reason) in new[]
                 {
                     ("max lots", "not a number"),
                     ("min -1", "nought or more"),
                     ("ticks -1", "nought or more"),
                     ("graticule star", "circle or a polygon"),
                     ("showLegend yes", "true or false"),
                 })
        {
            var source = "radar-beta\n  axis a\n  " + line;
            StringAssert.Contains(Trouble(source).Single(), reason, source);
        }
    }

    [TestMethod]
    public void OptionsMayShareALine() =>
        CollectionAssert.AreEqual(
            new[] { "max", "min", "ticks", "graticule", "showLegend" },
            Nodes("radar-beta\n  axis a\n  max 10, min 0, ticks 4, graticule polygon, showLegend false", RadarKinds.Option)
                .Select(option => option.Children.First(child => child.Kind == MermaidKinds.Key).Text)
                .ToArray());

    [TestMethod]
    public void ALineThatIsNoRadarLineIsHeldWithTheReason()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("radar-beta\n  wibble", "A radar line is"),
                     ("radar-beta\n  axisa", "A radar line is"),
                     ("radar-beta\n  axis a b", "An axis line"),
                     ("radar-beta\n  axis a[\"Math", "never closed"),
                     ("radar-beta\n  axis a[\"Math\" b", "never closed with ]"),
                     ("radar-beta\n  curve x{1} y", "A curve line"),
                     ("radar-beta wibble", "Nothing but a colon"),
                 })
        {
            var held = RadarParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null);

            Assert.AreEqual(Kinds.Verbatim, held.Kind, source);
            StringAssert.Contains(held.Trouble, reason, source);
        }
    }

    [TestMethod]
    public void WrittenIntoALabel_WhatWouldEndItIsKept()
    {
        Assert.AreEqual("say #quot;hi#quot; a\\\\b", RadarParser.Escaped("say \"hi\" a\\b", '"'), "a quote as its entity code, as in any diagram, and a backslash by another");
        Assert.AreEqual("it\\'s \"so\"", RadarParser.Escaped("it's \"so\"", '\''), "in single quotes, that quote by a backslash");
    }

    private static List<ContentNode> Nodes(string source, string kind) =>
        [.. Staged(source).SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];

    private static string Name(ContentNode item) =>
        item.Children.Single(child => child.Kind == MermaidKinds.Name).Inner(Kinds.Words)!.Text;

    /// <summary>What an axis's or a curve's label says, each escape and line ending in it as what it stands for.</summary>
    private static string Label(ContentNode item)
    {
        var said = item.Children.Single(child => child.Kind == MermaidKinds.Label).Inner(Kinds.Words)!;
        return said.IsLeaf ? said.Text : string.Concat(said.Children.Select(piece => RadarStandIns.Decoded(piece) ?? piece.Text));
    }

    /// <summary>How far a curve reaches along each spoke, as the stages worked it out.</summary>
    private static double?[] Points(string source, string curve) =>
        [.. Nodes(source, RadarKinds.Curve).OfType<RadarCurveNode>().Single(node => Name(node) == curve).Points];
}
