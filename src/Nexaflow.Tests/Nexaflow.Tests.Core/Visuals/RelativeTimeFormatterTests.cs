using System;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Tests.Core.Visuals;

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
        => Assert.AreEqual("just now", RelativeTimeFormatter.Format(Now.AddSeconds(-20), Now));

    [TestMethod]
    public void OneMinute_Singular()
        => Assert.AreEqual("1 minute ago", RelativeTimeFormatter.Format(Now.AddMinutes(-1), Now));

    [TestMethod]
    public void SeveralMinutes_Plural()
        => Assert.AreEqual("5 minutes ago", RelativeTimeFormatter.Format(Now.AddMinutes(-5), Now));

    [TestMethod]
    public void OneHour_Singular()
        => Assert.AreEqual("1 hour ago", RelativeTimeFormatter.Format(Now.AddMinutes(-75), Now));

    [TestMethod]
    public void SeveralHours_Plural()
        => Assert.AreEqual("3 hours ago", RelativeTimeFormatter.Format(Now.AddHours(-3), Now));

    [TestMethod]
    public void EarlierToday_StaysRelative()
    {
        // Same calendar day, 08:00 vs 14:30 — still "hours ago", not a date.
        var when = new DateTime(2026, 7, 15, 8, 0, 0);
        Assert.AreEqual("6 hours ago", RelativeTimeFormatter.Format(when, Now));
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
        Assert.IsFalse(text.Contains("ago"), $"expected an absolute date, got a relative phrase: '{text}'");
    }

    [TestMethod]
    public void FutureSkew_TreatedAsJustNow()
        => Assert.AreEqual("just now", RelativeTimeFormatter.Format(Now.AddSeconds(30), Now));
}
