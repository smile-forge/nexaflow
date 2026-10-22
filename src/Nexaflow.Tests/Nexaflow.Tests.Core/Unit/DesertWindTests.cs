using System;
using System.Linq;

using Nexaflow.Core.Themes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The Desert theme's weather, held to what it promises. The storm is the one rule a person can check by
/// watching — at most once an hour — and it is easy to break without noticing: a shorter cycle, a second storm
/// in it, or a new scene opening just before one. Each seed is a different afternoon, so every assertion runs
/// across many of them.
/// </summary>
[TestClass]
[CoversNode("desert")]
public class DesertWindTests
{
    private static readonly int[] Seeds = Enumerable.Range(1, 200).ToArray();

    [TestMethod]
    public void TheStorm_ComesRoundNoMoreThanOnceAnHour()
    {
        // One storm per cycle, at its start: consecutive storms are a whole cycle apart.
        foreach (int seed in Seeds)
        {
            var wind = new DesertWind(new Random(seed));
            Assert.IsTrue(wind.CycleSeconds >= DesertWind.StormSpacing,
                $"seed {seed}: a {wind.CycleSeconds:F0}s cycle brings the storm round sooner than hourly");
        }
    }

    [TestMethod]
    public void ANewDesert_OpensInTheCalm_AnHourOrMoreBeforeTheStorm()
    {
        // A scene made again — a theme change, the battery policy restoring it — knows nothing of the storm the
        // last one showed. Opening an hour clear of the next is what keeps the rule across that boundary.
        foreach (int seed in Seeds)
        {
            var wind = new DesertWind(new Random(seed));
            Assert.IsTrue(wind.CycleSeconds - wind.OpeningSeconds >= DesertWind.StormSpacing,
                $"seed {seed}: opens {wind.CycleSeconds - wind.OpeningSeconds:F0}s before the next storm");
            Assert.IsTrue(wind.OpeningSeconds >= wind.SettledSeconds,
                $"seed {seed}: opens at {wind.OpeningSeconds:F0}s, before the storm has settled");
            Assert.AreEqual(0, wind.StormAt(wind.OpeningSeconds), $"seed {seed}: opens inside the storm");
        }
    }

    [TestMethod]
    public void TheStorm_Threatens_ButNeverArrives()
    {
        foreach (int seed in Seeds)
        {
            var wind  = new DesertWind(new Random(seed));
            double peak = Enumerable.Range(0, (int)wind.Storm.End).Max(t => wind.StormAt(t));
            Assert.IsTrue(peak >= 0.7, $"seed {seed}: the storm only reaches {peak:F2} — no threat at all");
            Assert.IsTrue(peak < 1,    $"seed {seed}: the storm reaches full strength");
            Assert.AreEqual(0, wind.StormAt(wind.Storm.End + 1), $"seed {seed}: the storm never dies away");
        }
    }

    [TestMethod]
    public void Gusts_NeverOverlap_AndNoneBlowsInTheStorm()
    {
        // GustAt looks at one gust only, which is right only if a front has left the window before the next sets
        // off; and gusts are held back until the storm has settled.
        foreach (int seed in Seeds)
        {
            var wind  = new DesertWind(new Random(seed));
            var gusts = wind.Gusts;
            Assert.IsTrue(gusts.Count > 30, $"seed {seed}: only {gusts.Count} gusts in a cycle — hardly occasional");
            Assert.IsTrue(gusts[0].Start >= wind.SettledSeconds, $"seed {seed}: a gust blows before the calm returns");
            for (int i = 1; i < gusts.Count; i++)
                Assert.IsTrue(gusts[i].Start >= gusts[i - 1].Gone,
                    $"seed {seed}: gust {i} sets off before gust {i - 1} has crossed the window");
        }
    }

    [TestMethod]
    public void TheCycle_WrapsOnTheBreeze()
    {
        // The clock wraps from the end of the cycle to its start; anything still blowing there would jump.
        foreach (int seed in Seeds)
        {
            var wind = new DesertWind(new Random(seed));
            foreach (double across in new[] { 0.0, 0.5, 1.0 })
            {
                Assert.AreEqual(DesertWind.Calm, wind.StrengthAt(0, across), 1e-9, $"seed {seed}: not calm at the start");
                Assert.AreEqual(DesertWind.Calm, wind.StrengthAt(wind.CycleSeconds, across), 1e-9,
                    $"seed {seed}: not calm at the wrap");
            }
            Assert.IsTrue(wind.Gusts[^1].Gone < wind.CycleSeconds, $"seed {seed}: the last gust runs past the wrap");
        }
    }

    [TestMethod]
    public void AGust_ReachesTheFarSideLater()
    {
        var wind = new DesertWind(new Random(7));
        var gust = wind.Gusts[0];
        double t = gust.Start + gust.Rise;   // at its peak on the left edge

        Assert.AreEqual(gust.Peak, wind.GustAt(t, 0), 1e-9);
        Assert.IsTrue(wind.GustAt(t, 1) < gust.Peak, "the front reaches the right edge as soon as the left");
        Assert.AreEqual(gust.Peak, wind.GustAt(t + gust.Crossing, 1), 1e-9);
    }

    [TestMethod]
    public void Strength_StaysBetweenTheBreezeAndAFullStorm()
    {
        var wind = new DesertWind(new Random(11));
        for (double t = 0; t < wind.CycleSeconds; t += 0.5)
        {
            double s = wind.StrengthAt(t, 0.5);
            Assert.IsTrue(s >= DesertWind.Calm && s < 1, $"strength {s} at {t}s");
        }
    }

    [TestMethod]
    public void Travel_OnlyEverMovesForward()
    {
        // The drift's offset is the air's travel; a step backward would run the sand upwind.
        var wind = new DesertWind(new Random(3));
        double last = wind.TravelAt(0);
        Assert.AreEqual(0, last, 1e-9);
        for (double t = 0.1; t <= wind.CycleSeconds; t += 0.1)
        {
            double next = wind.TravelAt(t);
            Assert.IsTrue(next > last, $"travel stalls or reverses at {t}s");
            last = next;
        }
        Assert.AreEqual(wind.TravelPerCycle, last, wind.TravelPerCycle * 1e-3);
    }
}
