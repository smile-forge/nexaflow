using Nexaflow.Features.Video.ViewModels;

namespace Nexaflow.Tests.Features.Video;

/// <summary>The scene-strip thumbnail's time label — sub-hour uses m:ss, an hour or more uses h:mm:ss.</summary>
[TestClass]
public class KeyframeViewModelTests
{
    private static string Label(TimeSpan at) => new KeyframeViewModel { Position = at }.TimeLabel;

    [TestMethod]
    public void TimeLabel_SubHour_IsMinutesSeconds()
    {
        Assert.AreEqual("0:00", Label(TimeSpan.Zero));
        Assert.AreEqual("0:05", Label(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("1:23", Label(TimeSpan.FromSeconds(83)));
        Assert.AreEqual("10:00", Label(TimeSpan.FromMinutes(10)));
    }

    [TestMethod]
    public void TimeLabel_OneHourOrMore_IsHoursMinutesSeconds()
    {
        Assert.AreEqual("1:02:03", Label(new TimeSpan(1, 2, 3)));
        Assert.AreEqual("2:00:00", Label(TimeSpan.FromHours(2)));
    }

    [TestMethod]
    public void Thumbnail_DefaultsToNull()
        => Assert.IsNull(new KeyframeViewModel { Position = TimeSpan.Zero }.Thumbnail);
}
