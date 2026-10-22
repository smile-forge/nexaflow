using System;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Visuals;

/// <summary>
/// <see cref="RelativeTimeFormatter"/> reads a past instant the way a chat log wants it: relative while it's
/// still today, absolute once a day boundary has passed. <c>now</c> is injected so the boundaries are testable.
/// </summary>
[TestClass]
[CoversNode("vcommon-formatters")]
public class RelativeTimeFormatterTests
{
    private static readonly DateTime Now = new(2026, 7, 15, 14, 30, 0);

    [TestMethod]
    public void UnderAMinute_JustNow()
        => Assert.AreEqual(Str.Get("Common.Time.JustNow"), RelativeTimeFormatter.Format(Now.AddSeconds(-20), Now));

    [TestMethod]
    public void OneMinute_Singular()
        => Assert.AreEqual(Str.Format("Common.Time.MinuteAgo", 1), RelativeTimeFormatter.Format(Now.AddMinutes(-1), Now));

    [TestMethod]
    public void SeveralMinutes_Plural()
        => Assert.AreEqual(Str.Format("Common.Time.MinutesAgo", 5), RelativeTimeFormatter.Format(Now.AddMinutes(-5), Now));

    [TestMethod]
    public void OneHour_Singular()
        => Assert.AreEqual(Str.Format("Common.Time.HourAgo", 1), RelativeTimeFormatter.Format(Now.AddMinutes(-75), Now));

    [TestMethod]
    public void SeveralHours_Plural()
        => Assert.AreEqual(Str.Format("Common.Time.HoursAgo", 3), RelativeTimeFormatter.Format(Now.AddHours(-3), Now));

    [TestMethod]
    public void EarlierToday_StaysRelative()
    {
        // Same calendar day, 08:00 vs 14:30 — still "hours ago", not a date.
        var when = new DateTime(2026, 7, 15, 8, 0, 0);
        Assert.AreEqual(Str.Format("Common.Time.HoursAgo", 6), RelativeTimeFormatter.Format(when, Now));
    }

    [TestMethod]
    public void Yesterday_BecomesAbsolute()
    {
        // Only a few hours earlier by the clock, but across the midnight boundary — a date reads better
        // than "18 hours ago", which stops meaning anything once the day flips. Asserted culture-robustly
        // (the month abbreviation varies by the machine's ICU data): it's the date, not a relative phrase.
        var when = new DateTime(2026, 7, 14, 22, 0, 0);
        var text = RelativeTimeFormatter.Format(when, Now);

        StringAssert.Contains(text, "2026");
        StringAssert.Contains(text, "22:00");
        Assert.AreNotEqual(Str.Format("Common.Time.HoursAgo", 16), text, "expected an absolute date, got a relative phrase");
    }

    [TestMethod]
    public void FutureSkew_TreatedAsJustNow()
        => Assert.AreEqual(Str.Get("Common.Time.JustNow"), RelativeTimeFormatter.Format(Now.AddSeconds(30), Now));
}
