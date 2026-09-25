using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Radar;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Radar;

/// <summary>
/// What a <c>radar-beta</c> block is read into: its axes, its curves and their values, its options — which axis each value is
/// for, and what is said where a curve's values do not fit its axes — and what is written instead of any of that, held with the reason.
/// </summary>
[TestClass]
[CoversNode("radar-ast")]
public class RadarGrammarTests : MermaidGrammarContract
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

    public override MermaidDiagram Diagram => MermaidDiagram.Radar;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
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
        ("names in quotes", "radar-beta\n  axis \"two words\"[\"Two\"], b\n  curve c{ \"two words\": 1, b: 2 }"),
        ("space everywhere", "radar-beta\n  axis  a [ \"A\" ] ,  b\n  curve c [ \"C\" ] { a : 1 , b : 2 }  "),
        ("no space anywhere", "radar-beta\n  axis a[\"A\"],b\n  curve c[\"C\"]{1,2}"),
        ("options sharing a line", "radar-beta\n  axis a, b, c\n  curve x{1, 2, 3}\n  max 10, min 0, ticks 4, graticule polygon, showLegend false"),
        ("decimal values", "radar-beta\n  axis a, b\n  curve x{1.5, 0.25}"),
        ("axes written under the curves", "radar-beta\n  curve x{1, 2}\n  axis a, b"),
        ("written on Windows", "radar-beta\r\n  axis a, b  \r\n  curve x{1, 2}\r\n"),
        // Half written.
        ("an axis line naming nothing yet", "radar-beta\n  axis "),
        ("an axis still to name after a comma", "radar-beta\n  axis a, "),
        ("a curve still to name", "radar-beta\n  axis a\n  curve "),
        ("a curve with no values yet", "radar-beta\n  axis a, b\n  curve c"),
        ("values never closed", "radar-beta\n  axis a, b\n  curve c{1, "),
        ("a value still to come", "radar-beta\n  axis a, b\n  curve c{1, }"),
        ("an option set to nothing yet", "radar-beta\n  axis a\n  max "),
        ("an empty label", "radar-beta\n  axis a[\"\"]"),
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
        ("an option nobody knows", "radar-beta\n  axis a\n  maxi 3"),
        ("a keyword run into its name", "radar-beta\n  axisa"),
        ("something after a curve", "radar-beta\n  axis a\n  curve x{1} y"),
        ("words after the header", "radar-beta wibble\n  axis a"),
        ("nothing but the keyword", "radar-beta"),
    ];

    [TestMethod]
    public void AnAxisIsItsNameAndItsLabel_SeveralToALine()
    {
        var axes = Nodes(Grades, RadarKinds.Axis);

        CollectionAssert.AreEqual(new[] { "m", "s", "e", "h", "g", "a" }, axes.Select(Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Math", "Science", "English", "History", "Geography", "Art" },
                                  axes.Select(axis => axis.Children.Single(child => child.Kind == MermaidKinds.Label).Inner(MermaidKinds.Words)!.Text).ToArray());
    }

    [TestMethod]
    public void ANumberIsForTheAxisInItsPlace_AndAValueNamingItsAxisIsForThatAxis()
    {
        CollectionAssert.AreEqual(new[] { "axis1", "axis2", "axis3" }, For(Details, "id1"));
        CollectionAssert.AreEqual(new[] { "axis1", "axis2", "axis3" }, For(Details, "id3"), "a curve sharing its line with another has values of its own");
        CollectionAssert.AreEqual(new[] { "axis3", "axis1", "axis2" }, For(Details, "id4"), "each in the order written, for the axis it names");
    }

    [TestMethod]
    public void AxesWrittenUnderACurveAreStillTheAxesItsValuesAreFor() =>
        CollectionAssert.AreEqual(new[] { "a", "b" }, For("radar-beta\n  curve x{1, 2}\n  axis a, b", "x"));

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
                 })
            Assert.AreEqual(0, Trouble(source).Count, $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void ValuesNeverClosedAreStillRead_WithTheReason()
    {
        const string source = "radar-beta\n  axis a, b\n  curve c{1, 2";

        CollectionAssert.AreEqual(new[] { "a", "b" }, For(source, "c"));
        StringAssert.Contains(Trouble(source).Single(), "never closed");
    }

    [TestMethod]
    public void AnAxisStillToNameStandsWhereTypingItsNamePutsIt()
    {
        const string source = "radar-beta\n  axis a, ";
        var name = MermaidParser.Parse(source).Placed().Last(place => place.Node.Kind == MermaidKinds.Words);

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
                     ("ticks 2.5", "whole number"),
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
                     ("radar-beta\n  curve x{1} y", "A curve line"),
                     ("radar-beta wibble", "Nothing but a colon"),
                 })
        {
            var held = MermaidParser.Parse(source).SelfAndDescendants().Single(node => node.Trouble is not null);

            Assert.AreEqual(Kinds.Verbatim, held.Kind, source);
            StringAssert.Contains(held.Trouble, reason, source);
        }
    }

    [TestMethod]
    public void ANewLineUnderACurveIsAnotherCurve_UnderAnOptionNothing_AndElsewhereAnAxis()
    {
        var grammar = new RadarGrammar();
        const string source = "radar-beta\n  axis a\n  curve x{1}\n  max 2";

        Assert.AreEqual(("curve ", 6), grammar.Blank(Nodes(source, RadarKinds.Curves).Single()));
        Assert.IsNull(grammar.Blank(Nodes(source, RadarKinds.Options).Single()));
        Assert.AreEqual(("axis ", 5), grammar.Blank(Nodes(source, RadarKinds.Axes).Single()));
        Assert.AreEqual(("axis ", 5), grammar.Blank(null));
    }

    [TestMethod]
    public void AnAxisRenamedToHoldASpaceIsPutInQuotes_AndSoIsEveryValueNamingIt()
    {
        const string source = "radar-beta\n  axis a, b\n  curve x{ a: 1, b: 2 }";
        var grammar = new RadarGrammar();
        var a = grammar.Names(ContentReading.Of(MermaidStaged.Read(source)).Root).Single(name => name.Name == "a");

        Assert.AreEqual(1, a.Uses.Count, "the value that names it");

        var words = a.Declared.Words()!;
        var writing = grammar.Escaping(words, words.End, " ")!.Value;

        Assert.AreEqual("radar-beta\n  axis \"a \", b\n  curve x{ a: 1, b: 2 }", source[..writing.Start] + writing.Text + source[writing.End..]);
        Assert.AreEqual("\"two words\"", grammar.Naming("two words"));
        Assert.AreEqual("\"9lives\"", grammar.Naming("9lives"), "a name starting with a digit too");
        Assert.AreEqual("renamed", grammar.Naming("renamed"));
    }

    private static List<ContentNode> Nodes(string source, string kind) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];

    private static string Name(ContentNode item) =>
        item.Children.Single(child => child.Kind == MermaidKinds.Name).Inner(MermaidKinds.Words)!.Text;

    /// <summary>The axis each of a curve's values is for, as the stage worked it out.</summary>
    private static string?[] For(string source, string curve) =>
        [.. Nodes(source, RadarKinds.Curve).Single(node => Name(node) == curve)
            .SelfAndDescendants().Where(node => node.Kind == RadarKinds.Entry).Select(entry => entry.Said(RadarRoles.For))];
}
