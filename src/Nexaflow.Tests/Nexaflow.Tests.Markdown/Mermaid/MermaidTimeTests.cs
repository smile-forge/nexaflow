using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>Dates, lengths of time and axis formats as Mermaid reads and writes them.</summary>
[TestClass]
[CoversNode("gantt-ast")]
public class MermaidTimeTests
{
    private static readonly DateTime Today = new(2026, 9, 17);

    [TestMethod]
    public void ADateIsReadInItsFormat_AndOnlyWhereWritingItBackGivesWhatWasWritten()
    {
        Assert.AreEqual(new DateTime(2014, 1, 6), MermaidDate.Read("2014-01-06", "YYYY-MM-DD"));
        Assert.AreEqual(new DateTime(2025, 2, 10), MermaidDate.Read("10-02-2025", "DD-MM-YYYY"));
        Assert.AreEqual(new DateTime(2014, 3, 1, 17, 49, 0), MermaidDate.Read("2014-03-01 5:49 pm", "YYYY-MM-DD h:mm a"));
        Assert.AreEqual(new DateTime(1906, 1, 1), MermaidDate.Read("1906", "YYYY"));
        Assert.AreEqual(new DateTime(2014, 7, 1), MermaidDate.Read("Q3 2014", "[Q]Q YYYY"));
        Assert.AreEqual(new DateTime(2014, 1, 21), MermaidDate.Read("January 21st, 2014", "MMMM Do, YYYY"));

        Assert.IsNull(MermaidDate.Read("2014-1-6", "YYYY-MM-DD"), "not as the format writes it");
        Assert.IsNull(MermaidDate.Read("2014-02-30", "YYYY-MM-DD"), "no such day");
        Assert.IsNull(MermaidDate.Read("3d", "YYYY-MM-DD"), "a length of time");
        Assert.IsNull(MermaidDate.Read("after a1", "YYYY-MM-DD"));
    }

    [TestMethod]
    public void AFormatWithNoDayTakesTodays_AndAStampIsSecondsOrMillisecondsSince1970()
    {
        Assert.AreEqual(Today.AddHours(17).AddMinutes(49), MermaidDate.Read("17:49", "HH:mm", Today));
        Assert.AreEqual(MermaidDate.FromMilliseconds(71_000), MermaidDate.Read("71", "X"));
        Assert.AreEqual(MermaidDate.FromMilliseconds(71), MermaidDate.Read("71", "x"));
        Assert.IsNull(MermaidDate.Read("1410715640.579", "X"), "a stamp written with more than it writes back");
    }

    [TestMethod]
    public void ADateIsWrittenInItsFormat_BracketsAsTheyAre()
    {
        var date = new DateTime(2014, 1, 6, 9, 5, 3, 7);

        Assert.AreEqual("2014-01-06", MermaidDate.Write(date, "YYYY-MM-DD"));
        Assert.AreEqual("Mon 6th Jan 14, 9:05:03.007 AM Q1", MermaidDate.Write(date, "ddd Do MMM YY, h:mm:ss.SSS A [Q]Q"));
        Assert.AreEqual("monday", MermaidDate.Write(date, "dddd").ToLowerInvariant());
    }

    [TestMethod]
    public void ALengthOfTimeIsANumberAndItsUnit_AddedAsDayJsAddsIt()
    {
        var start = new DateTime(2014, 1, 31);

        Assert.AreEqual((30d, "d"), MermaidDuration.Read("30d"));
        Assert.AreEqual((1.5, "d"), MermaidDuration.Read("1.5d"));
        Assert.AreEqual((500d, "ms"), MermaidDuration.Read("500ms"));
        foreach (var wrong in new[] { "3dX", "d", "1.d", ".5d", "3 d", "2014-01-01", "" })
            Assert.IsNull(MermaidDuration.Read(wrong), wrong);

        Assert.AreEqual(new DateTime(2014, 2, 2), MermaidDuration.After(start, (1.5, "d")), "a day and a half is two");
        Assert.AreEqual(new DateTime(2014, 2, 28), MermaidDuration.After(start, (1, "M")), "a month on from the 31st is the month's last day");
        Assert.AreEqual(new DateTime(2014, 2, 14), MermaidDuration.After(start, (2, "w")));
        Assert.AreEqual(start.AddMinutes(90), MermaidDuration.After(start, (1.5, "h")));
    }

    [TestMethod]
    public void AnAxisFormatWritesD3sDirectives_PaddedAsAsked()
    {
        var date = new DateTime(2014, 1, 6, 17, 5, 3, 7);

        Assert.AreEqual("2014-01-06", MermaidTimeFormat.Write(date, "%Y-%m-%d"));
        Assert.AreEqual("Mon Monday Jan January 06  6 6", MermaidTimeFormat.Write(date, "%a %A %b %B %d %e %-d"));
        Assert.AreEqual("17:05:03.007 05 PM", MermaidTimeFormat.Write(date, "%H:%M:%S.%L %I %p"));
        Assert.AreEqual("006 01 01 1 14 100%", MermaidTimeFormat.Write(date, "%j %U %W %w %y 100%%"));
        Assert.AreEqual("1/6/2014, 5:05:03 PM", MermaidTimeFormat.Write(date, "%c"));
        Assert.AreEqual("71", MermaidTimeFormat.Write(MermaidDate.FromMilliseconds(71_000), "%s"));
    }
}
