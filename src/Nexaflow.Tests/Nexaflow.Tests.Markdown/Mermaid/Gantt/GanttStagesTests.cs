using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Gantt;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Gantt;

/// <summary>
/// What a <c>gantt</c> block's stages say it means, as Mermaid means it: when every task runs, on the day the block is read, and
/// what the lines setting something for the whole chart mean.
/// </summary>
[TestClass]
[CoversNode("gantt-ast")]
public class GanttStagesTests
{
    private static GanttBlockNode Chart(string source) => (GanttBlockNode)MermaidStaged.Read(source);

    private static IReadOnlyList<(string Says, GanttTaskNode Task)> Tasks(ContentNode chart) =>
        [.. chart.SelfAndDescendants().OfType<GanttTaskNode>().Select(task => (Said(task), task))];

    private static GanttTaskNode Task(ContentNode chart, string name) => Tasks(chart).Single(task => task.Says == name).Task;

    private static string Said(ContentNode task) => task.Children.FirstOrDefault(child => child.Kind == GanttKinds.Text).Words()?.Text ?? string.Empty;

    [TestMethod]
    public void ATaskStartsWhenItSaysOrWhenTheOneBeforeItEnds_AndEndsADateOrALengthLater()
    {
        var chart = Chart(GanttGrammarTests.First);

        Assert.AreEqual((new DateTime(2014, 1, 1), new DateTime(2014, 1, 31)), (Task(chart, "A task").Start, Task(chart, "A task").End));
        Assert.AreEqual((new DateTime(2014, 1, 31), new DateTime(2014, 2, 20)), (Task(chart, "Another task").Start, Task(chart, "Another task").End), "after a1");
        Assert.AreEqual(new DateTime(2014, 1, 24), Task(chart, "Task in Another").End);
        Assert.AreEqual((new DateTime(2014, 1, 24), new DateTime(2014, 2, 17)), (Task(chart, "another task").Start, Task(chart, "another task").End), "after the task before it");
    }

    [TestMethod]
    public void AfterTakesTheLatestEnd_UntilTheEarliestStart_AndAnIdNoTaskHasIsToday()
    {
        var chart = Chart("gantt\n    apple :a, 2017-07-20, 1w\n    banana :crit, b, 2017-07-23, 1d\n    cherry :active, c, after b a, 1d\n    kiwi   :d, 2017-07-20, until b c\n    fig :after nobody, 1d");

        Assert.AreEqual(new DateTime(2017, 7, 27), Task(chart, "cherry").Start, "apple ends last");
        Assert.AreEqual(new DateTime(2017, 7, 23), Task(chart, "kiwi").End, "banana starts first");
        Assert.AreEqual(chart.Now.Date, Task(chart, "fig").Start);
    }

    [TestMethod]
    public void TodayIsTheMomentTheBlockIsRead()
    {
        var before = DateTime.Now;
        var chart = Chart("gantt\n  A :2014-01-01, 3d");

        Assert.IsTrue(chart.Now >= before && chart.Now <= DateTime.Now);
    }

    [TestMethod]
    public void UntilWaitsForATaskWrittenAfterIt()
    {
        var chart = Chart(GanttGrammarTests.Syntax);

        Assert.AreEqual(new DateTime(2014, 1, 27), Task(chart, "Add to mermaid").End, "the milestone starts on Saturday the 25th, and an end that is no date is pushed past the weekend");
        Assert.AreEqual(17, Tasks(chart).Count);
    }

    [TestMethod]
    public void ExcludedDaysPushALengthsEndOn_ButNotADatesEnd()
    {
        var chart = Chart(GanttGrammarTests.Syntax);

        Assert.AreEqual(new DateTime(2014, 1, 14), Task(chart, "Active task").End, "three days from Thursday, over a weekend");
        Assert.AreEqual(new DateTime(2014, 1, 8), Task(chart, "Completed task").End, "an end written as a date stays");
        Assert.IsTrue(chart.Days.Excluded(new DateTime(2014, 1, 11)) && !chart.Days.Excluded(new DateTime(2014, 1, 10)));

        var friday = Chart("gantt\n  excludes weekends\n  weekend friday\n  includes 2014-01-11\n  Task : 2014-01-01, 1d").Days;
        Assert.IsTrue(friday.Excluded(new DateTime(2014, 1, 10)), "Friday");
        Assert.IsFalse(friday.Excluded(new DateTime(2014, 1, 11)), "a Saturday includes names");
        Assert.IsFalse(friday.Excluded(new DateTime(2014, 1, 12)), "Sunday is a weekday");
    }

    [TestMethod]
    public void StampsInclusiveEndsAndTimesAreRead()
    {
        var stamps = Chart("gantt\n    dateFormat X\n    axisFormat %s\n    71   : 0, 71");
        Assert.AreEqual(71_000, (Task(stamps, "71").End - Task(stamps, "71").Start).TotalMilliseconds, 1);

        var inclusive = Chart("gantt\n  inclusiveEndDates\n  Task : 2014-01-01, 2014-01-05");
        Assert.AreEqual(new DateTime(2014, 1, 6), Task(inclusive, "Task").End);

        var times = Chart("gantt\n    dateFormat HH:mm\n    axisFormat %H:%M\n    Initial vert : vert, v1, 17:30, 2m\n    Task A : 3m");
        Assert.AreEqual(times.Now.Date.AddHours(17).AddMinutes(32), Task(times, "Task A").Start, "a time of day is that time today");
        Assert.AreEqual("%H:%M", times.AxisFormat);
        Assert.AreEqual("%d", Chart("gantt\n  dateFormat D\n  A :1, 3d").AxisFormat, "Mermaid's for a chart counted in days");
    }

    [TestMethod]
    public void SettingsClicksAndTheFrontMatterAreRead()
    {
        var chart = Chart("---\nconfig:\n  gantt:\n    barHeight: 30\n    topAxis: true\n    numberSectionStyles: 2\n  themeVariables:\n    critBkgColor: \"#ff0000\"\n---\n"
            + "gantt\n  tickInterval 1week\n  weekday monday\n  todayMarker off\n  Visit :cl1, 2014-01-07, 3d\n  click cl1 href \"https://mermaidjs.github.io/\"");

        Assert.AreEqual((new GanttTick(1, "week"), DayOfWeek.Monday, true), (chart.Tick, chart.Weekday, chart.Marker.Off));
        Assert.IsTrue(chart.TopAxis);
        Assert.AreEqual((30d, 2, "#ff0000"), (chart.Config.BarHeight!.Value, chart.Config.NumberSectionStyles!.Value, chart.Config.CritBackground));
        Assert.AreEqual("https://mermaidjs.github.io/", Task(chart, "Visit").Link);
        Assert.IsTrue(Task(chart, "Visit").Clickable);
        Assert.IsFalse(Task(Chart("gantt\n  A :a1, 2014-01-01, 3d"), "A").Clickable);
    }

    [TestMethod]
    public void TheLastLineWritingASettingIsTheOneThatHolds()
    {
        var chart = Chart("gantt\n  axisFormat %d\n  weekday monday\n  axisFormat %m\n  weekday friday\n  A :2014-01-01, 3d");

        Assert.AreEqual(("%m", DayOfWeek.Friday), (chart.AxisFormat, chart.Weekday));
    }

    [TestMethod]
    public void ATickIntervalIsAWholeNumberOfAUnitMermaidCountsIn()
    {
        Assert.AreEqual(new GanttTick(2, "week"), GanttTick.Read("2week"));
        Assert.AreEqual(new GanttTick(15, "minute"), GanttTick.Read(" 15minute "));
        Assert.IsNull(GanttTick.Read("1year"), "Mermaid counts in nothing longer than a month");
        Assert.IsNull(GanttTick.Read("1decade"));
        Assert.IsNull(GanttTick.Read("week"), "a number of them");
        Assert.IsNull(Chart("gantt\n  tickInterval 1decade\n  A :2014-01-01, 3d").Tick, "so the axis chooses its own");
    }

    [TestMethod]
    public void ATodayMarkerIsReadAsTheStyleItWrites()
    {
        var today = GanttToday.Read("stroke-width:5px,stroke:#0f0,opacity:0.5,stroke-dasharray:6 3");

        Assert.AreEqual((5d, "#0f0", 0.5), (today.Width!.Value, today.Stroke, today.Opacity!.Value));
        CollectionAssert.AreEqual(new[] { 6d, 3d }, today.Dashes!.ToArray());
        Assert.IsTrue(GanttToday.Read("off").Off);
        Assert.AreEqual(GanttToday.Default, Chart("gantt\n  A :2014-01-01, 3d").Marker, "the theme's where nothing is written");
    }

    [TestMethod]
    public void ATaskThatNeverComesToAStartAndAnEndIsLeftAsWritten()
    {
        const string source = "gantt\n  Task : someday, 3d\n  Next : 2d\n  Fine : 2014-01-01, 1d\n  Tagged :milestone";
        var chart = Chart(source);

        CollectionAssert.AreEqual(new[] { "Fine" }, Tasks(chart).Select(task => task.Says).ToArray());
        Assert.AreEqual(4, chart.SelfAndDescendants().Count(node => node.Kind == GanttKinds.Task), "every task is still there, as written");
        Assert.AreEqual(source, chart.Print());
    }
}
