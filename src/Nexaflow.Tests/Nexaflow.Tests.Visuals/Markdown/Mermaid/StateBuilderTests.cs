using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.State;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>stateDiagram</c> block drawn on the shared layout tree: the states in the ranks the transitions put them in, the composite
/// states holding the states written inside them, the notes beside what they are about, and the transitions over all of it.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("state-diagram")]
public class StateBuilderTests : MermaidBuilderContract
{
    private const string Intro =
        "stateDiagram-v2\n    [*] --> Still\n    Still --> [*]\n\n    Still --> Moving\n    Moving --> Still\n"
        + "    Moving --> Crash\n    Crash --> [*]";

    private const string Composite =
        "stateDiagram-v2\n  [*] --> Draft\n  Draft --> Submitted : submit\n  state Review {\n    [*] --> Screening\n"
        + "    Screening --> Decision\n  }\n  Submitted --> Review\n  Review --> Published : approved\n  Published --> [*]";

    public override MermaidDiagram Diagram => MermaidDiagram.State;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("where it starts and where it stops", Intro),
        ("a composite state", Composite),
        ("laid out left to right", "stateDiagram-v2\n  direction LR\n  [*] --> a --> b --> [*]"),
        ("laid out bottom up", "stateDiagram-v2\n  direction BT\n  a --> b"),
        ("laid out right to left", "stateDiagram-v2\n  direction RL\n  a --> b"),
        ("a state said again", "stateDiagram-v2\n  a --> b\n  a : The first thing\n  b : The second"),
        ("what is written on it first", "stateDiagram-v2\n  state \"Standing still\" as still\n  still --> [*]"),
        ("a fork and a join",
         "stateDiagram-v2\n  state f <<fork>>\n  state j <<join>>\n  [*] --> f\n  f --> one\n  f --> two\n  one --> j\n"
         + "  two --> j\n  j --> [*]"),
        ("a choice", "stateDiagram-v2\n  state c <<choice>>\n  [*] --> c\n  c --> yes : if it is\n  c --> no : if it is not"),
        ("regions running at the same time",
         "stateDiagram-v2\n  state A {\n    one --> two\n    --\n    three --> four\n  }"),
        ("a note either side", "stateDiagram-v2\n  one --> two\n  note right of one : mind this\n  note left of two : and this"),
        ("a note across several lines", "stateDiagram-v2\n  one\n  note right of one\n    the first line\n    and the second\n  end note"),
        ("composite states nested",
         "stateDiagram-v2\n  [*] --> A\n  state A {\n    [*] --> B\n    state B {\n      [*] --> c\n    }\n  }"),
        ("a composite state running its own way",
         "stateDiagram-v2\n  direction LR\n  state A {\n    direction TB\n    one --> two\n  }\n  A --> B"),
        ("classes and styles",
         "stateDiagram-v2\n  one:::busy --> two\n  classDef busy fill:#6e6ce6,color:white\n  style two stroke:#f66,stroke-width:3px"),
        ("a ring of states", "stateDiagram-v2\n  a --> b --> c --> a"),
        ("a state joined to itself", "stateDiagram-v2\n  a --> a\n  a --> b"),
        ("a spacing of its own",
         "---\nconfig:\n  state:\n    nodeSpacing: 80\n    rankSpacing: 80\n---\nstateDiagram-v2\n  a --> b\n  a --> c"),
        ("where pressing a state leads", "stateDiagram-v2\n  a --> b\n  click a \"https://example.com\" \"Go there\""),
        ("still being written", "stateDiagram-v2\n  a : \n  b --> "),
        ("what nobody means to write", "stateDiagram-v2\n  a\n  }\n  style nowhere fill:#969"),
        ("nothing to draw", "stateDiagram-v2"),
    ];

    [TestMethod]
    public void ATransitionPutsWhatItReachesInTheRankBeyondWhatItLeaves() => UiThread.Run(() =>
    {
        var down = Nodes("stateDiagram-v2\n  a --> b");
        Assert.IsTrue(down["b"].Top > down["a"].Bottom, $"b is below a: {down["a"]} then {down["b"]}");

        var right = Nodes("stateDiagram-v2\n  direction LR\n  a --> b");
        Assert.IsTrue(right["b"].Left > right["a"].Right, $"b is right of a: {right["a"]} then {right["b"]}");
    });

    [TestMethod]
    public void TheDotsAScopeStartsAndStopsAtAreDrawnSmallAndRound() => UiThread.Run(() =>
    {
        var laid = Build("stateDiagram-v2\n  [*] --> one\n  one --> [*]");
        var dots = Pieces(laid, StatePiece.State).Where(piece => Said(piece).Count() == 0).ToList();

        Assert.AreEqual(2, dots.Count, "one dot to start at and one to stop at, neither with a word on it");

        foreach (var dot in dots)
        {
            Assert.AreEqual(dot.Bounds.Width, dot.Bounds.Height, 0.01, "a dot is round");
            Assert.IsTrue(dot.Bounds.Width < 30, $"and small: {dot.Bounds}");
        }
    });

    [TestMethod]
    public void AForkIsABarAcrossTheWayItRuns() => UiThread.Run(() =>
    {
        var down = Shape(Build("stateDiagram-v2\n  state f <<fork>>\n  [*] --> f"), "f");
        Assert.IsTrue(down.Width > down.Height * 3, $"running down the page the bar lies across it: {down}");

        var across = Shape(Build("stateDiagram-v2\n  direction LR\n  state f <<fork>>\n  [*] --> f"), "f");
        Assert.IsTrue(across.Height > across.Width * 3, $"and running across the page it stands up: {across}");
    });

    [TestMethod]
    public void ACompositeStatesStatesAreDrawnInsideIt() => UiThread.Run(() =>
    {
        const string source = "stateDiagram-v2\n  outside\n  state A {\n    one --> two\n  }";

        var laid = Build(source);
        var group = Pieces(laid, StatePiece.Group).Single();
        var inside = Pieces(laid, StatePiece.State)
            .Where(piece => piece.Ancestors().Any(over => over.Kind == StatePiece.Group))
            .ToList();

        CollectionAssert.AreEqual(new[] { "one", "two" }, inside.Select(piece => Said(piece).First().Words!.Glyphs.Text).ToArray(),
                                  "the composite state's own states hang off it, and the one outside does not");

        foreach (var node in inside)
            Assert.IsTrue(Holds(group.Bounds, node.Bounds), $"{node.Bounds} sits inside {group.Bounds}");
    });

    [TestMethod]
    public void PressingACompositeStateMeansTheWholeOfWhatItWasWrittenAs() => UiThread.Run(() =>
    {
        const string source = "stateDiagram-v2\n  state A {\n    one\n  }";

        var group = Pieces(Build(source), StatePiece.Group).Single();
        Assert.AreEqual("state A {\n    one\n  }", Written(source, group.Part));
    });

    [TestMethod]
    public void ACompositeStateRunsTheWayItsOwnDirectionSays() => UiThread.Run(() =>
    {
        var nodes = Nodes("stateDiagram-v2\n  direction LR\n  state A {\n    direction TB\n    one --> two\n  }\n  c --> one");

        Assert.IsTrue(nodes["two"].Top > nodes["one"].Bottom, $"inside it runs down: {nodes["one"]} then {nodes["two"]}");
        Assert.IsTrue(nodes["one"].Left > nodes["c"].Right, $"and the diagram itself still runs right: {nodes["c"]} then {nodes["one"]}");
    });

    [TestMethod]
    public void ANoteIsDrawnBesideTheStateItIsAbout() => UiThread.Run(() =>
    {
        var laid = Build("stateDiagram-v2\n  one --> two\n  note right of one : mind this");
        var note = Pieces(laid, StatePiece.Note).Single();
        var one = Nodes(laid)["one"];

        Assert.AreEqual("mind this", Said(note).First().Words!.Glyphs.Text);
        Assert.IsTrue(note.Bounds.Top < one.Bottom && note.Bounds.Bottom > one.Top,
                      $"a note is beside what it is about rather than after it: {one} and {note.Bounds}");
    });

    [TestMethod]
    public void ANoteAcrossSeveralLinesDrawsEveryOneOfThem() => UiThread.Run(() =>
    {
        var laid = Build("stateDiagram-v2\n  one\n  note right of one\n    the first line\n    and the second\n  end note");
        var note = Pieces(laid, StatePiece.Note).Single();

        CollectionAssert.AreEqual(new[] { "the first line", "and the second" },
                                  Said(note).Select(said => said.Words!.Glyphs.Text).ToArray());
    });

    [TestMethod]
    public void ALineDividesTheRegionsOfACompositeState() => UiThread.Run(() =>
    {
        var laid = Build("stateDiagram-v2\n  state A {\n    one\n    --\n    two\n  }");
        var divider = Pieces(laid, StatePiece.Divider).Single();
        var group = Pieces(laid, StatePiece.Group).Single();
        var nodes = Nodes(laid);

        Assert.IsTrue(divider.Bounds.Width > group.Bounds.Width / 2,
                      $"the line is drawn the width of the composite state: {divider.Bounds} of {group.Bounds}");
        Assert.IsTrue(divider.Bounds.Top > nodes["one"].Top && divider.Bounds.Bottom < nodes["two"].Bottom,
                      "and it runs between the regions it divides");
    });

    [TestMethod]
    public void WhatIsWrittenOnATransitionIsDrawnOverTheMiddleOfItsLine() => UiThread.Run(() =>
    {
        var laid = Build("stateDiagram-v2\n  one --> two : and then");
        var said = Pieces(laid, StatePiece.Label).Single();
        var nodes = Nodes(laid);

        Assert.AreEqual("and then", Said(said).First().Words!.Glyphs.Text);
        Assert.IsTrue(said.Bounds.Top > nodes["one"].Top && said.Bounds.Bottom < nodes["two"].Bottom,
                      $"between the states it joins: {nodes["one"]}, {said.Bounds}, {nodes["two"]}");
    });

    [TestMethod]
    public void AStateIsColouredByTheClassItIsGiven() => UiThread.Run(() =>
    {
        var laid = Build("stateDiagram-v2\n  one:::busy --> two\n  classDef busy fill:#6e6ce6");
        var drawn = Pieces(laid, StatePiece.State)
            .ToDictionary(piece => Said(piece).Select(said => said.Words!.Glyphs.Text).FirstOrDefault() ?? string.Empty, Filled);

        Assert.AreEqual(Color.FromRgb(0x6e, 0x6c, 0xe6), drawn["one"]);
        Assert.AreNotEqual(drawn["one"], drawn["two"], "and one without a class keeps the card's own colour");
    });

    // ── What it works with ──────────────────────────────────────────────────

    private static Laid Build(string source, double room = 900) =>
        new StateBuilder(MermaidBuilders.Read(source), EditState.For(source), StyleFormat.Dark, isReadOnly: true).Lay(room);

    /// <summary>Every state drawn, by what is written on it.</summary>
    private static Dictionary<string, Rect> Nodes(string source) => Nodes(Build(source));

    private static Dictionary<string, Rect> Nodes(Laid laid)
    {
        var nodes = new Dictionary<string, Rect>();

        foreach (var piece in Pieces(laid, StatePiece.State))
            nodes[Said(piece).Select(said => said.Words!.Glyphs.Text).FirstOrDefault() ?? piece.Bounds.ToString()] = piece.Bounds;

        return nodes;
    }

    /// <summary>Where the state written with these words was drawn.</summary>
    private static Rect Shape(Laid laid, string said) => Nodes(laid)[said];

    /// <summary>The words drawn under a piece, in the order they were drawn.</summary>
    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    private static Color? Filled(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Shape).Select(Fill).FirstOrDefault();

    private static bool Holds(Rect over, Rect inner) =>
        inner.Left >= over.Left - 1 && inner.Right <= over.Right + 1 && inner.Top >= over.Top - 1 && inner.Bottom <= over.Bottom + 1;
}
