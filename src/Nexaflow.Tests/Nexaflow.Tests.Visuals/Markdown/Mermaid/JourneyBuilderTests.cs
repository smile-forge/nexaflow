using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Journey;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>journey</c> block drawn on the shared layout tree: tasks in a row standing for their lines, a face over each one
/// standing for its score and floating higher the better it scored, a mark for everyone taking part, and a legend saying
/// which colour is whose.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("journey")]
public class JourneyBuilderTests : MermaidBuilderContract
{
    private const string Working =
        "journey\n  title My working day\n  section Go to work\n    Make tea: 5: Me\n    Go upstairs: 3: Me\n    Do work: 1: Me, Cat\n"
        + "  section Go home\n    Go downstairs: 5: Me\n    Sit down: 5: Me";

    public override MermaidDiagram Diagram => MermaidDiagram.Journey;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documented working day", Working),
        ("tasks before any section", "journey\n  Wake up: 3: Me\n  section Go to work\n    Make tea: 5: Me"),
        ("a task with no actors, and one nothing scores", "journey\n  Sit down: 5\n  Stand up: "),
        ("sizes and colours of its own",
            "---\nconfig:\n  journey:\n    width: 200\n    height: 60\n    taskFontSize: 14\n    actorColours: [\"#ff0000\"]\n    sectionFills:\n      - \"#101010\"\n---\n"
            + "journey\n  section Go to work\n    Make tea: 5: Me"),
        ("still being written", "journey\n  Make tea: \n  Go upstairs: 3: \n  section "),
        ("what nobody means to write", "journey\n  Make tea: 9: Me\n  Go upstairs"),
        ("nothing to draw", "journey"),
    ];

    private static Laid Build(string source, double room = 900) =>
        JourneyBuilder.Build(EditState.For(source), new DiagramLaying(MarkdownPalette.Dark, 1.0, room));

    [TestMethod]
    public void TheTasksRunInARowUnderTheBandOfTheSectionTheyAreIn() => UiThread.Run(() =>
    {
        var laid = Build(Working);
        var tasks = Pieces(laid, JourneyPiece.Task);
        var bands = Pieces(laid, JourneyPiece.Section);

        Assert.AreEqual(5, tasks.Count);
        Assert.AreEqual(2, bands.Count);
        Assert.IsTrue(tasks[0].Bounds.Right <= tasks[1].Bounds.Left, "one task after another");
        Assert.AreEqual(tasks[0].Bounds.Top, tasks[4].Bounds.Top, 0.5, "all in the one row");
        Assert.IsTrue(bands[0].Bounds.Bottom <= tasks[0].Bounds.Top, "the band over them");
        Assert.IsTrue(bands[0].Bounds.Right >= tasks[2].Bounds.Right - 0.5, "as wide as the three tasks it groups");
    });

    [TestMethod]
    public void ATaskStandsForItsLine_AndItsFaceForWhatScoresIt() => UiThread.Run(() =>
    {
        var laid = Build(Working);

        Assert.AreEqual("Make tea: 5: Me", Written(Working, Pieces(laid, JourneyPiece.Task)[0].Part));
        Assert.AreEqual("5", Written(Working, Pieces(laid, JourneyPiece.Face)[0].Part), "the face means the score");
        Assert.IsTrue(Pieces(laid, JourneyPiece.Says).All(said => said.Words is { Maps: true }), "typed into where it is drawn");
    });

    [TestMethod]
    public void AFaceFloatsHigherTheBetterItScored() => UiThread.Run(() =>
    {
        var faces = Pieces(Build("journey\n  Best: 5: Me\n  Middling: 3: Me\n  Worst: 1: Me\n  Unscored: "), JourneyPiece.Face)
            .Select(face => Middle(face.Bounds).Y).ToList();

        Assert.IsTrue(faces[0] < faces[1], "five over three");
        Assert.IsTrue(faces[1] < faces[2], "three over one");
        Assert.AreEqual(faces[1], faces[3], 0.5, "and a task nothing scores floats in the middle");
    });

    [TestMethod]
    public void EveryoneTakingPartIsMarkedOnTheTask_AndNamedInTheLegend() => UiThread.Run(() =>
    {
        var laid = Build(Working);
        var marks = Pieces(laid, JourneyPiece.Actor);
        var keys = Pieces(laid, MermaidPiece.Key);

        Assert.AreEqual(6, marks.Count, "Me on five tasks and Cat on one");
        Assert.AreEqual("Cat", Written(Working, marks[3].Part), "a mark means where that actor is named");
        Assert.AreEqual(2, keys.Count, "a legend row each");
        Assert.IsTrue(keys.All(key => key.Bounds.Bottom <= Pieces(laid, JourneyPiece.Task)[0].Bounds.Top), "the legend along the top");
    });

    [TestMethod]
    public void TheTasksOfASectionShareItsColour_AndAnActorsIsOneOfTheirOwn() => UiThread.Run(() =>
    {
        var laid = Build(Working);
        var tasks = Pieces(laid, JourneyPiece.Task).Select(Fill).ToList();
        var marks = Pieces(laid, JourneyPiece.Actor).Select(Fill).ToList();

        Assert.AreEqual(tasks[0], tasks[1], "both in the first section");
        Assert.AreNotEqual(tasks[2], tasks[3], "and the next section is its own colour");
        Assert.AreEqual(marks[0], marks[1], "one actor, one colour");
        Assert.AreNotEqual(marks[2], marks[3], "and the other actor another");
    });

    [TestMethod]
    public void TheColoursTheFrontMatterWritesAreWhatIsDrawn() => UiThread.Run(() =>
    {
        var laid = Build("---\nconfig:\n  journey:\n    actorColours: [\"#ff0000\"]\n    sectionFills: [\"#101010\"]\n---\njourney\n  section Go to work\n    Make tea: 5: Me");

        Assert.AreEqual(Color.FromRgb(0xFF, 0, 0), Fill(Pieces(laid, JourneyPiece.Actor).Single()));
        Assert.AreEqual(Color.FromRgb(0x10, 0x10, 0x10), Fill(Pieces(laid, JourneyPiece.Section).Single())!.Value with { A = 0xFF },
            "the band is its fill, tinted");
    });

    [TestMethod]
    public void TheJourneyIsAsBigAsItsFrontMatterAsks() => UiThread.Run(() =>
    {
        var plain = Pieces(Build("journey\n  Make tea: 5: Me"), JourneyPiece.Task).Single().Bounds;
        var bigger = Pieces(Build("---\nconfig:\n  journey:\n    width: 200\n    height: 60\n---\njourney\n  Make tea: 5: Me"), JourneyPiece.Task).Single().Bounds;

        Assert.AreEqual(150, plain.Width, 0.5);
        Assert.AreEqual(200, bigger.Width, 0.5);
        Assert.AreEqual(60, bigger.Height, 0.5);
    });
}
