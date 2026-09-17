using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Gantt;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>gantt</c> block drawn on the shared layout tree: a bar, diamond or marker standing for each task, its row banded by its
/// section, dates along the foot, and every task's and section's name typed into where it is drawn.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("gantt")]
public class GanttBuilderTests : MermaidBuilderContract
{
    private const string Plan =
        "gantt\n  title Plan\n  dateFormat YYYY-MM-DD\n  excludes weekends\n  section Build\n  Design the whole thing :done, a1, 2014-01-06, 10d\n  Code :active, a2, after a1, 5d\n"
        + "  section Ship\n  Test :crit, 3d\n  Release :milestone, m1, after a2, 0d\n  Freeze :vert, 2014-01-15, 1d";

    public override MermaidDiagram Diagram => MermaidDiagram.Gantt;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("a plan with every kind of task", Plan),
        ("times of day", "gantt\n    dateFormat HH:mm\n    axisFormat %H:%M\n    Initial milestone : milestone, m1, 17:49, 2m\n    Task A : 10m\n    Task B : 5m"),
        ("compact, over the top, a week apart", "---\ndisplayMode: compact\nconfig:\n  gantt:\n    topAxis: true\n---\ngantt\n  tickInterval 1week\n  weekday monday\n  section S\n  A :a1, 2014-01-01, 30d\n  B :a2, 2014-01-20, 25d"),
        ("stamps", "gantt\n    dateFormat X\n    axisFormat %s\n    section Issue\n    71   : 0, 71"),
        ("clicks and a today marker styled", "gantt\n  todayMarker stroke-width:5px,stroke:#0f0,opacity:0.5\n  Visit :cl1, 2014-01-07, 3d\n  click cl1 href \"https://mermaidjs.github.io/\""),
        ("theme of its own", "---\nconfig:\n  gantt:\n    barHeight: 30\n    fontSize: 14\n  themeVariables:\n    taskBkgColor: \"#203040\"\n    critBkgColor: \"#ff0000\"\n---\ngantt\n  A : 2014-01-01, 3d\n  B :crit, 2d"),
        ("still being written", "gantt\n  : 2014-01-01, 3d\n  section \n  Next :\n  Other :after , 2d"),
        ("what nobody means to write", "gantt\n  Task : someday, 3d\n  Fine : 2014-01-01, 3dX"),
        ("nothing to draw", "gantt"),
    ];

    private static Laid Build(string source, double room = 700) =>
        GanttBuilder.Build(EditState.For(source), MarkdownPalette.Dark, 1.0, room);

    private static string Task(string source, Piece piece) => Written(source, piece.Part).Split(':')[0].Trim();

    [TestMethod]
    public void EveryTaskStandsForItsLine_InTheRowItIsWrittenIn() => UiThread.Run(() =>
    {
        var tasks = Pieces(Build(Plan), GanttPiece.Task).Where(task => Task(Plan, task) != "Freeze").ToList();

        CollectionAssert.AreEqual(new[] { "Design the whole thing", "Code", "Test", "Release" }, tasks.OrderBy(task => task.Bounds.Top).Select(task => Task(Plan, task)).ToArray(),
                                  "each task a row further down than the one written before it");
    });

    [TestMethod]
    public void ABarRunsFromItsStartToItsEnd_AfterStartingWhereItsTaskEnds() => UiThread.Run(() =>
    {
        const string source = "gantt\n  A :a1, 2014-01-06, 10d\n  B :after a1, 5d";
        var tasks = Pieces(Build(source), GanttPiece.Task).ToDictionary(task => Task(source, task));

        Assert.AreEqual(tasks["A"].Bounds.Right, tasks["B"].Bounds.Left, 3, "B starts where a1 ends");
        Assert.AreEqual(tasks["A"].Bounds.Width / 2, tasks["B"].Bounds.Width, 3, "and lasts half as long");
    });

    [TestMethod]
    public void ANameThatFitsIsInItsBar_OneThatDoesNotIsBesideIt_AndAllAreTheCharactersWritten() => UiThread.Run(() =>
    {
        var laid = Build(Plan);
        var labels = Pieces(laid, GanttPiece.Label);
        var bars = Pieces(laid, GanttPiece.Task).ToDictionary(task => Task(Plan, task));

        Assert.IsTrue(labels.All(label => label.Words is { Maps: true }), "every name typed into where it is drawn");
        Assert.IsTrue(bars["Design the whole thing"].Bounds.Contains(labels.Single(label => Written(Plan, label.Part) == "Design the whole thing").Bounds), "a name that fits is in its bar");
        Assert.IsFalse(bars["Release"].Bounds.IntersectsWith(labels.Single(label => Written(Plan, label.Part) == "Release").Bounds), "a milestone's name is beside it");
        CollectionAssert.AreEquivalent(new[] { "Build", "Ship" }, Pieces(laid, GanttPiece.SectionName).Select(name => Written(Plan, name.Part)).ToArray());
    });

    [TestMethod]
    public void DatesAreWrittenAlongTheFootInTheAxisFormat_AndOverTheTopWhereAsked() => UiThread.Run(() =>
    {
        var dates = Pieces(Build("gantt\n  axisFormat %d/%m\n  topAxis\n  A : 2014-01-01, 10d"), GanttPiece.Date);

        Assert.IsTrue(dates.Count >= 4 && dates.All(date => date.Words?.Glyphs.Text.Length == 5), string.Join(", ", dates.Select(date => date.Words?.Glyphs.Text)));
        Assert.AreEqual(2, dates.Select(date => date.Bounds.Top).Distinct().Count(), "a row under the chart and a row over it");
    });

    [TestMethod]
    public void ExcludedDaysAreBanded_AndAMarkerIsALineDownTheChart() => UiThread.Run(() =>
    {
        var laid = Build(Plan);
        var freeze = Pieces(laid, GanttPiece.Task).Single(task => Task(Plan, task) == "Freeze");

        Assert.AreEqual(1, Pieces(laid, GanttPiece.Excluded).Count);
        Assert.IsTrue(freeze.Bounds.Height > freeze.Bounds.Width * 10);
    });
}
