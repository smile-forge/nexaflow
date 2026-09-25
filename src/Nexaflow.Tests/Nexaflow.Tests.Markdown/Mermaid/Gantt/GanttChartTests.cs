using Nexaflow.Markdown.Mermaid.Gantt;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Gantt;

/// <summary>A <c>gantt</c> block worked out as Mermaid works it out: every task's start and end, its row, and the chart's settings.</summary>
[TestClass]
[CoversNode("gantt-ast")]
public class GanttChartTests
{
    private static readonly DateTime Today = new(2026, 9, 17);

    private static GanttTask Task(GanttChart chart, string name) => chart.Tasks.Single(task => task.Says == name);

    [TestMethod]
    public void ATaskStartsWhenItSaysOrWhenTheOneBeforeItEnds_AndEndsADateOrALengthLater()
    {
        var chart = GanttChart.Of(MermaidStaged.Read(GanttGrammarTests.First), Today);

        Assert.AreEqual((new DateTime(2014, 1, 1), new DateTime(2014, 1, 31)), (Task(chart, "A task").Start, Task(chart, "A task").End));
        Assert.AreEqual((new DateTime(2014, 1, 31), new DateTime(2014, 2, 20)), (Task(chart, "Another task").Start, Task(chart, "Another task").End), "after a1");
        Assert.AreEqual(new DateTime(2014, 1, 24), Task(chart, "Task in Another").End);
        Assert.AreEqual((new DateTime(2014, 1, 24), new DateTime(2014, 2, 17)), (Task(chart, "another task").Start, Task(chart, "another task").End), "after the task before it");
        CollectionAssert.AreEqual(new[] { "Section", "Another" }, chart.Sections.Select(section => section.Says).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, chart.Tasks.Select(task => task.Order).ToArray());
    }

    [TestMethod]
    public void AfterTakesTheLatestEnd_UntilTheEarliestStart_AndAnIdNoTaskHasIsToday()
    {
        var chart = GanttChart.Of(MermaidStaged.Read("gantt\n    apple :a, 2017-07-20, 1w\n    banana :crit, b, 2017-07-23, 1d\n    cherry :active, c, after b a, 1d\n    kiwi   :d, 2017-07-20, until b c\n    fig :after nobody, 1d"), Today);

        Assert.AreEqual(new DateTime(2017, 7, 27), Task(chart, "cherry").Start, "apple ends last");
        Assert.AreEqual(new DateTime(2017, 7, 23), Task(chart, "kiwi").End, "banana starts first");
        Assert.AreEqual(Today, Task(chart, "fig").Start);
        Assert.IsTrue(Task(chart, "banana").Critical && Task(chart, "cherry").Active);
    }

    [TestMethod]
    public void UntilWaitsForATaskWrittenAfterIt()
    {
        var chart = GanttChart.Of(MermaidStaged.Read(GanttGrammarTests.Syntax), Today);

        Assert.AreEqual(new DateTime(2014, 1, 27), Task(chart, "Add to mermaid").End, "the milestone starts on Saturday the 25th, and an end that is no date is pushed past the weekend");
        Assert.IsTrue(Task(chart, "Functionality added").Milestone);
        Assert.AreEqual(17, chart.Tasks.Count);
    }

    [TestMethod]
    public void ExcludedDaysPushALengthsEndOn_ButNotADatesEnd()
    {
        var chart = GanttChart.Of(MermaidStaged.Read(GanttGrammarTests.Syntax), Today);

        Assert.AreEqual(new DateTime(2014, 1, 14), Task(chart, "Active task").End, "three days from Thursday, over a weekend");
        Assert.AreEqual(new DateTime(2014, 1, 8), Task(chart, "Completed task").End, "an end written as a date stays");
        Assert.IsTrue(chart.Excluded(new DateTime(2014, 1, 11)) && !chart.Excluded(new DateTime(2014, 1, 10)));

        var friday = GanttChart.Of(MermaidStaged.Read("gantt\n  excludes weekends\n  weekend friday\n  includes 2014-01-11\n  Task : 2014-01-01, 1d"), Today);
        Assert.IsTrue(friday.Excluded(new DateTime(2014, 1, 10)), "Friday");
        Assert.IsFalse(friday.Excluded(new DateTime(2014, 1, 11)), "a Saturday includes names");
        Assert.IsFalse(friday.Excluded(new DateTime(2014, 1, 12)), "Sunday is a weekday");
    }

    [TestMethod]
    public void StampsInclusiveEndsAndTimesAreRead()
    {
        var stamps = GanttChart.Of(MermaidStaged.Read("gantt\n    dateFormat X\n    axisFormat %s\n    71   : 0, 71"), Today);
        Assert.AreEqual(71_000, (Task(stamps, "71").End - Task(stamps, "71").Start).TotalMilliseconds, 1);

        var inclusive = GanttChart.Of(MermaidStaged.Read("gantt\n  inclusiveEndDates\n  Task : 2014-01-01, 2014-01-05"), Today);
        Assert.AreEqual(new DateTime(2014, 1, 6), Task(inclusive, "Task").End);

        var times = GanttChart.Of(MermaidStaged.Read("gantt\n    dateFormat HH:mm\n    axisFormat %H:%M\n    Initial vert : vert, v1, 17:30, 2m\n    Task A : 3m"), Today);
        Assert.AreEqual(Today.AddHours(17).AddMinutes(32), Task(times, "Task A").Start);
        Assert.AreEqual(-1, Task(times, "Initial vert").Order, "a vertical marker takes no row");
        Assert.AreEqual(0, Task(times, "Task A").Order);
        Assert.AreEqual("%H:%M", times.AxisFormat);
    }

    [TestMethod]
    public void CompactModeSharesRowsWhereTasksDoNotOverlap()
    {
        var chart = GanttChart.Of(MermaidStaged.Read("---\ndisplayMode: compact\n---\ngantt\n    section Section\n    A task :a1, 2014-01-01, 30d\n    Another task :a2, 2014-01-20, 25d\n    Another one :a3, 2014-02-10, 20d"), Today);

        CollectionAssert.AreEqual(new[] { 0, 1, 0 }, chart.Tasks.Select(task => task.Order).ToArray());
    }

    [TestMethod]
    public void SettingsClicksAndTheFrontMatterAreRead()
    {
        var chart = GanttChart.Of(MermaidStaged.Read("---\nconfig:\n  gantt:\n    barHeight: 30\n    topAxis: true\n    numberSectionStyles: 2\n  themeVariables:\n    critBkgColor: \"#ff0000\"\n---\n"
            + "gantt\n  tickInterval 1week\n  weekday monday\n  todayMarker off\n  Visit :cl1, 2014-01-07, 3d\n  click cl1 href \"https://mermaidjs.github.io/\""), Today);

        Assert.AreEqual((new GanttTick(1, "week"), DayOfWeek.Monday, true), (chart.Tick, chart.Weekday, chart.Today.Off));
        Assert.IsTrue(chart.TopAxis);
        Assert.AreEqual((30d, 2, "#ff0000"), (chart.Config.BarHeight!.Value, chart.Config.NumberSectionStyles!.Value, chart.Config.CritBackground));
        Assert.AreEqual("https://mermaidjs.github.io/", Task(chart, "Visit").Link);
        Assert.IsTrue(Task(chart, "Visit").Clickable);
    }

    [TestMethod]
    public void ATickIntervalIsAWholeNumberOfAUnitMermaidCountsIn()
    {
        Assert.AreEqual(new GanttTick(2, "week"), GanttTick.Read("2week"));
        Assert.AreEqual(new GanttTick(15, "minute"), GanttTick.Read(" 15minute "));
        Assert.IsNull(GanttTick.Read("1year"), "Mermaid counts in nothing longer than a month");
        Assert.IsNull(GanttTick.Read("1decade"));
        Assert.IsNull(GanttTick.Read("week"), "a number of them");
        Assert.IsNull(GanttChart.Of(MermaidStaged.Read("gantt\n  tickInterval 1decade\n  A :2014-01-01, 3d"), Today).Tick, "so the axis chooses its own");
    }

    [TestMethod]
    public void ATodayMarkerIsReadAsTheStyleItWrites()
    {
        var today = GanttToday.Read("stroke-width:5px,stroke:#0f0,opacity:0.5,stroke-dasharray:6 3");

        Assert.AreEqual((5d, "#0f0", 0.5), (today.Width!.Value, today.Stroke, today.Opacity!.Value));
        CollectionAssert.AreEqual(new[] { 6d, 3d }, today.Dashes!.ToArray());
        Assert.IsTrue(GanttToday.Read("off").Off);
        Assert.AreEqual(GanttToday.Default, GanttChart.Of(MermaidStaged.Read("gantt\n  A :2014-01-01, 3d"), Today).Today, "the theme's where nothing is written");
    }

    [TestMethod]
    public void ATaskThatCannotBeWorkedOutIsNotDrawn()
    {
        var chart = GanttChart.Of(MermaidStaged.Read("gantt\n  Task : someday, 3d\n  Next : 2d\n  Fine : 2014-01-01, 1d\n  Tagged :milestone"), Today);

        CollectionAssert.AreEqual(new[] { "Fine" }, chart.Tasks.Select(task => task.Says).ToArray());
    }
}
