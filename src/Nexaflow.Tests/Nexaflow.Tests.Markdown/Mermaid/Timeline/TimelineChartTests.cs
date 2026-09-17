using Nexaflow.Markdown.Mermaid.Timeline;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Timeline;

/// <summary>
/// A <c>timeline</c> block read into what it describes: which way it runs, the sections grouping its periods, the events
/// each period gathers from its own line and from the lines going on after it, and what the front matter asks for.
/// </summary>
[TestClass]
[CoversNode("timeline-ast")]
public class TimelineChartTests
{
    [TestMethod]
    public void EachPeriodGathersTheEventsWrittenAfterItsColons()
    {
        var chart = TimelineChart.Read(TimelineGrammarTests.Social);
        var periods = chart.Periods.ToList();

        Assert.AreEqual(4, periods.Count);
        Assert.AreEqual("2002", periods[0].Says.Says.Text);
        CollectionAssert.AreEqual(new[] { "Facebook", "Google" }, periods[1].Events.Select(said => said.Says.Says.Text).ToArray());
    }

    [TestMethod]
    public void ALineOfFurtherEventsAddsThemToThePeriodAboveIt()
    {
        var period = TimelineChart.Read("timeline\n    2004 : Facebook\n         : Google\n         : Orkut").Periods.Single();

        CollectionAssert.AreEqual(new[] { "Facebook", "Google", "Orkut" }, period.Events.Select(said => said.Says.Says.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, period.Events.Select(said => said.Order).ToArray());
    }

    [TestMethod]
    public void SectionsGroupThePeriodsWrittenUnderThem()
    {
        var chart = TimelineChart.Read(TimelineGrammarTests.Pizzas);

        Assert.IsTrue(chart.Sectioned);
        Assert.AreEqual(2, chart.Sections.Count);
        Assert.AreEqual("2021-2022", chart.Sections[0].Name!.Says.Text);
        Assert.AreEqual(2, chart.Sections[0].Periods.Count);
        Assert.AreEqual(2, chart.Sections[0].Periods[0].Events.Count, "the continuation line's event too");
        Assert.AreEqual(1, chart.Sections[1].Periods.Count);
    }

    [TestMethod]
    public void PeriodsWrittenBeforeAnySectionAreASectionWithNoName()
    {
        var chart = TimelineChart.Read("timeline\n    2002 : LinkedIn\n    section Later\n        2006 : Twitter");

        Assert.AreEqual(2, chart.Sections.Count);
        Assert.IsNull(chart.Sections[0].Name);
        Assert.AreEqual("2002", chart.Sections[0].Periods.Single().Says.Says.Text);
        Assert.IsTrue(chart.Sectioned, "something is still written as a section");
    }

    [TestMethod]
    public void WhichWayItRunsIsWhatTheLastLineSayingSoAsks()
    {
        Assert.AreEqual(TimelineWay.LeftToRight, TimelineChart.Read("timeline\n    2002 : LinkedIn").Way);
        Assert.AreEqual(TimelineWay.TopDown, TimelineChart.Read("timeline TD\n    2002 : LinkedIn").Way);
        Assert.AreEqual(TimelineWay.TopDown, TimelineChart.Read("timeline\n    direction TB\n    2002 : LinkedIn").Way);
        Assert.AreEqual(TimelineWay.LeftToRight, TimelineChart.Read("timeline TD\n    direction LR\n    2002 : LinkedIn").Way);
        Assert.AreEqual(TimelineWay.LeftToRight, TimelineChart.Read("timeline\n    direction sideways\n    2002 : LinkedIn").Way, "a way nobody knows is no way at all");
    }

    [TestMethod]
    public void AnEventThatSaysNothingIsNothingWritten()
    {
        var period = TimelineChart.Read("timeline\n    2004 : : Google").Periods.Single();

        Assert.AreEqual("Google", period.Events.Single().Says.Says.Text);
    }

    [TestMethod]
    public void TheFrontMattersOptionsAndColourSlotsAreRead()
    {
        var config = TimelineChart.Read(
            "---\nconfig:\n  timeline:\n    disableMulticolor: true\n    padding: 4\n  themeVariables:\n    cScale2: \"#4e79a7\"\n"
            + "    cScaleLabel2: \"#ffffff\"\n---\ntimeline\n    2002 : LinkedIn").Config;

        Assert.IsTrue(config.DisableMulticolor);
        Assert.AreEqual(4, config.Padding);
        Assert.AreEqual("#4e79a7", config.ScaleAt(2));
        Assert.AreEqual("#ffffff", config.ScaleLabelAt(2));
        Assert.IsNull(config.ScaleAt(0), "a slot nobody writes is the theme's");
    }

    [TestMethod]
    public void ABlockWithNoPeriodsHasNothingToDraw()
    {
        Assert.IsTrue(TimelineChart.Read("timeline").Empty);
        Assert.IsTrue(TimelineChart.Read("timeline\n    section Early").Empty);
        Assert.IsFalse(TimelineChart.Read("timeline\n    2002").Empty);
    }
}
