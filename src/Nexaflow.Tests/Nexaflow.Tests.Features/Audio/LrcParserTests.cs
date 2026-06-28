using Nexaflow.Features.Audio.Services;

namespace Nexaflow.Tests.Features.Audio;

[TestClass]
public class LrcParserTests
{
    [TestMethod]
    public void Parse_SyncedLines_ReadsTimesAndText_InOrder()
    {
        var lines = LrcParser.Parse(
            """
            [ti:Song]
            [00:12.50]First line
            [00:05.00]Earlier line
            [01:00.00]Minute mark
            """);

        Assert.AreEqual(3, lines.Count);
        Assert.AreEqual("Earlier line", lines[0].Text);
        Assert.AreEqual(System.TimeSpan.FromMilliseconds(5000), lines[0].Time);
        Assert.AreEqual("First line", lines[1].Text);
        Assert.AreEqual(System.TimeSpan.FromMilliseconds(12500), lines[1].Time);
        Assert.AreEqual(System.TimeSpan.FromMinutes(1), lines[2].Time);
    }

    [TestMethod]
    public void Parse_MultipleTimestampsPerLine_ExpandsToOneLinePerStamp()
    {
        var lines = LrcParser.Parse("[00:01.00][00:03.00]Repeated chorus");

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("Repeated chorus", lines[0].Text);
        Assert.AreEqual("Repeated chorus", lines[1].Text);
        Assert.AreEqual(System.TimeSpan.FromSeconds(1), lines[0].Time);
        Assert.AreEqual(System.TimeSpan.FromSeconds(3), lines[1].Time);
    }

    [TestMethod]
    public void Parse_MalformedAndIdOnlyLines_AreSkipped()
    {
        var lines = LrcParser.Parse(
            """
            [ar:Artist]
            no timestamp here
            [00:02.00]Only this counts
            """);

        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual("Only this counts", lines[0].Text);
    }

    [TestMethod]
    public void Parse_OffsetTag_ShiftsEveryLine()
    {
        // offset is milliseconds; positive shifts lyrics later.
        var lines = LrcParser.Parse(
            """
            [offset:500]
            [00:10.00]Shifted
            """);

        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual(System.TimeSpan.FromMilliseconds(10500), lines[0].Time);
    }

    [TestMethod]
    public void Parse_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(0, LrcParser.Parse(null).Count);
        Assert.AreEqual(0, LrcParser.Parse("   ").Count);
    }
}
