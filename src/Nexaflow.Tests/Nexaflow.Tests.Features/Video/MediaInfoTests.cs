using Nexaflow.Features.Video.Models;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Video;

/// <summary>The info-panel model: row value-equality, the "Off" subtitle option, and section composition.</summary>
[TestClass]
[CoversNode("video-infopanel")]
public class MediaInfoTests
{
    [TestMethod]
    public void MediaInfoRow_HasValueEquality()
    {
        Assert.AreEqual(new MediaInfoRow("Resolution", "1920 × 1080"),
                        new MediaInfoRow("Resolution", "1920 × 1080"));
        Assert.AreNotEqual(new MediaInfoRow("Resolution", "1920 × 1080"),
                           new MediaInfoRow("Resolution", "1280 × 720"));
    }

    [TestMethod]
    public void SubtitleTrackOption_OffHasNullId()
    {
        var off = new SubtitleTrackOption(null, "Off");
        var track = new SubtitleTrackOption("3", "English");
        Assert.IsNull(off.Id);
        Assert.AreEqual("Off", off.Label);
        Assert.AreEqual("3", track.Id);
    }

    [TestMethod]
    public void MediaInfoSection_StartsEmpty_AndCollectsRows()
    {
        var section = new MediaInfoSection { Header = "Video" };
        Assert.AreEqual(0, section.Rows.Count);
        section.Rows.Add(new MediaInfoRow("Codec", "H264"));
        Assert.AreEqual("Video", section.Header);
        Assert.AreEqual(1, section.Rows.Count);
    }

    [TestMethod]
    public void MediaInfo_HoldsFileNameSummaryAndSections()
    {
        var info = new MediaInfo
        {
            FileName = "clip.mp4",
            Summary  = "1920×1080 H264, 00:03:21, AAC stereo",
            Sections = [new MediaInfoSection { Header = "File" }],
        };

        Assert.AreEqual("clip.mp4", info.FileName);
        StringAssert.Contains(info.Summary, "H264");
        Assert.AreEqual(1, info.Sections.Count);
    }

    [TestMethod]
    public void MediaInfo_Sections_DefaultToEmpty()
        => Assert.AreEqual(0, new MediaInfo { FileName = "x", Summary = "y" }.Sections.Count);
}
