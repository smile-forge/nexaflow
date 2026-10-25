using System;
using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Markdown.Layout.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>Dates along a time axis: round boundaries of a unit chosen for about as many marks as asked, or every so many of a named unit.</summary>
[TestClass]
[CoversNode("gantt")]
public class DiagramTimeTests
{
    [TestMethod]
    public void AboutTenMarksFallOnTheRoundBoundariesOfAUnit()
    {
        var month = PortableDiagramTime.Ticks(new DateTime(2014, 1, 1), new DateTime(2014, 1, 31));
        Assert.IsTrue(month.All(mark => mark.TimeOfDay == TimeSpan.Zero), "a month is marked in days");
        CollectionAssert.AreEqual(new[] { 1, 3, 5 }, month.Take(3).Select(mark => mark.Day).ToArray(), "every two days, from the 1st");

        var hour = PortableDiagramTime.Ticks(new DateTime(2014, 1, 1, 17, 30, 0), new DateTime(2014, 1, 1, 18, 8, 0));
        CollectionAssert.AreEqual(new[] { 30, 35, 40 }, hour.Take(3).Select(mark => mark.Minute).ToArray(), "half an hour in fives of minutes");

        var century = PortableDiagramTime.Ticks(new DateTime(1900, 1, 1), new DateTime(1935, 1, 1));
        Assert.IsTrue(century.All(mark => mark.Year % 5 == 0 && mark.DayOfYear == 1), "decades in fives of years");
    }

    [TestMethod]
    public void MarksEverySoManyOfAUnitAreItsBoundariesCountingAMultiple()
    {
        var weeks = PortableDiagramTime.Every(new DateTime(2014, 1, 1), new DateTime(2014, 1, 31), 1, "week", DayOfWeek.Monday)!;
        Assert.IsTrue(weeks.All(mark => mark.DayOfWeek == DayOfWeek.Monday), "weeks start on the weekday asked");
        Assert.AreEqual(new DateTime(2014, 1, 6), weeks[0]);

        var days = PortableDiagramTime.Every(new DateTime(2014, 1, 1), new DateTime(2014, 1, 3), 1, "day", DayOfWeek.Sunday)!;
        Assert.AreEqual(3, days.Count, "both ends are marked");

        Assert.IsNull(PortableDiagramTime.Every(new DateTime(2014, 1, 1), new DateTime(2015, 1, 1), 1, "second", DayOfWeek.Sunday), "too many to be meant");
        Assert.IsNull(PortableDiagramTime.Every(new DateTime(2014, 1, 1), new DateTime(2015, 1, 1), 1, "decade", DayOfWeek.Sunday), "no such unit");
    }
}
