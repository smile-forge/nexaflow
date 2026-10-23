using System.IO;
using Nexaflow.Core;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Which local models stay loaded: those used most days are loaded ahead once the first window is up, and kept; any
/// other loads with its first use and is let go once it has stood idle — and each use is counted, on this machine, for
/// what is loaded ahead next time.
/// </summary>
[TestClass]
[CoversNode("model-residency")]
public class ModelResidencyTests
{
    /// <summary>A clock a test moves on by hand, on UTC days.</summary>
    private sealed class Clock : TimeProvider
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => Start.AddTicks(Interlocked.Read(ref _ticks));
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    /// <summary>A provider whose model stays loaded, counting its warms and cools.</summary>
    private sealed class Kept(Clock clock, bool releasesItself = false) : IAiProvider, ITracksModelUse
    {
        public int Warmed;
        public int Cooled;
        public string Name => "Fake";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public ModelHarness Harness => ModelHarness.Owned;
        public bool IsAvailable => true;
        public event EventHandler? AvailabilityChanged { add { } remove { } }
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public ModelUseTracker ModelUse { get; } = new(clock);
        public bool ReleasesItself => releasesItself;

        public Task WarmupAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref Warmed);
            ModelUse.Loaded();
            return Task.CompletedTask;
        }

        public Task CooldownAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref Cooled);
            return Task.CompletedTask;
        }
    }

    private static ModelUseHistory History(Clock clock) => new(Path.Combine(TestTemp.CreateSubdirectory("nexaflow-model-use-").FullName, "model-use.json"), clock);

    /// <summary>Counts a use of <paramref name="model"/> on each of <paramref name="days"/> days, the last of them today.</summary>
    private static void UsedOn(ModelUseHistory history, Clock clock, string model, int days)
    {
        clock.Advance(TimeSpan.FromDays(-days));
        for (int day = 0; day < days; day++)
        {
            clock.Advance(TimeSpan.FromDays(1));
            history.Record(model);
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until) Assert.Fail("it did not happen within ten seconds");
            await Task.Delay(10);
        }
    }

    [TestMethod]
    public void Adaptive_LoadsAheadWhatIsUsedOnEnoughOfTheWeek_OnDemandNothing_AlwaysEverything()
    {
        var config = new ModelPreloadConfig();
        Assert.AreEqual((false, false, true, true), (config.Preloads(0), config.Preloads(2), config.Preloads(3), config.Preloads(7)));

        config.DaysToPreload = 0;
        Assert.IsFalse(config.Preloads(0), "a model never used is never loaded ahead, whatever the threshold");

        config.Mode = ModelPreloadMode.OnDemand;
        Assert.IsFalse(config.Preloads(7));
        config.Mode = ModelPreloadMode.Always;
        Assert.IsTrue(config.Preloads(0));
    }

    [TestMethod]
    public async Task AModelUsedMostDays_IsLoadedAheadOnceTheWindowIsUp_AndOneSeldomUsedIsNot()
    {
        var clock = new Clock();
        var history = History(clock);
        UsedOn(history, clock, "Fake/often", 4);
        history.Record("Fake/seldom");
        await using var residency = new ModelResidency(new ModelPreloadConfig(), history, clock);
        Kept often = new(clock), seldom = new(clock);
        residency.Track(often, "Fake/often");
        residency.Track(seldom, "Fake/seldom");

        residency.Preload([often, seldom]);
        await Task.Delay(100);
        Assert.AreEqual(0, often.Warmed, "nothing loads before the first window has drawn");

        residency.Open();
        await WaitFor(() => often.Warmed == 1);
        await Task.Delay(100);
        Assert.AreEqual(0, seldom.Warmed, "used on one day of the last seven: it loads with its next use");
    }

    [TestMethod]
    public async Task WithNoHistory_NothingIsLoadedAhead()
    {
        var clock = new Clock();
        await using var residency = new ModelResidency(new ModelPreloadConfig(), History(clock), clock);
        var model = new Kept(clock);
        residency.Track(model, "Fake/new");

        residency.Preload([model]);
        residency.Open();
        await Task.Delay(200);

        Assert.AreEqual(0, model.Warmed);
    }

    [TestMethod]
    public async Task AModelNotLoadedAhead_IsLetGoOnceItHasStoodIdle_NeverWhileInUse()
    {
        var clock = new Clock();
        await using var residency = new ModelResidency(new ModelPreloadConfig(), History(clock), clock);
        var model = new Kept(clock);
        residency.Track(model, "Fake/model");

        var use = await model.ModelUse.BeginAsync();
        clock.Advance(TimeSpan.FromHours(1));
        await residency.ReleaseIdleAsync(default);
        Assert.AreEqual(0, model.Cooled, "a session open for an hour is a session in use");

        use.Dispose();
        clock.Advance(TimeSpan.FromMinutes(14));
        await residency.ReleaseIdleAsync(default);
        Assert.AreEqual(0, model.Cooled, "idle for less than its fifteen minutes");

        clock.Advance(TimeSpan.FromMinutes(1));
        await residency.ReleaseIdleAsync(default);
        await residency.ReleaseIdleAsync(default);
        Assert.AreEqual(1, model.Cooled, "let go once, and not again until it is used");
    }

    [TestMethod]
    public async Task AModelLoadedAhead_IsKept_AndOneWhoseBackendLetsItGo_IsLeftToIt()
    {
        var clock = new Clock();
        var history = History(clock);
        UsedOn(history, clock, "Fake/ahead", 5);
        await using var residency = new ModelResidency(new ModelPreloadConfig(), history, clock);
        Kept ahead = new(clock), ollama = new(clock, releasesItself: true);
        residency.Track(ahead, "Fake/ahead");
        residency.Track(ollama, "Fake/ollama");
        residency.Preload([ahead, ollama]);
        residency.Open();
        await WaitFor(() => ahead.Warmed == 1);
        using (await ollama.ModelUse.BeginAsync()) { }

        clock.Advance(TimeSpan.FromHours(2));
        await residency.ReleaseIdleAsync(default);

        Assert.AreEqual((0, 0), (ahead.Cooled, ollama.Cooled));
    }

    [TestMethod]
    public async Task EachUse_CountsItsDay_AndAForgottenModelIsNoLongerFollowed()
    {
        var clock = new Clock();
        var history = History(clock);
        await using var residency = new ModelResidency(new ModelPreloadConfig(), history, clock);
        var model = new Kept(clock);
        residency.Track(model, "Fake/model");

        using (await model.ModelUse.BeginAsync()) { }
        using (await model.ModelUse.BeginAsync()) { }
        Assert.AreEqual(1, history.DaysUsed("Fake/model"), "a day counts once, however often it is used on it");

        residency.Forget(model);
        clock.Advance(TimeSpan.FromDays(1));
        using (await model.ModelUse.BeginAsync()) { }
        Assert.AreEqual(1, history.DaysUsed("Fake/model"));
    }

    [TestMethod]
    public void OnlyTheLastSevenDaysCount()
    {
        var clock = new Clock();
        var history = History(clock);
        history.Record("Fake/model");
        clock.Advance(TimeSpan.FromDays(1));
        history.Record("Fake/model");

        clock.Advance(TimeSpan.FromDays(6));
        Assert.AreEqual(1, history.DaysUsed("Fake/model"), "the first day is eight days back now");
        clock.Advance(TimeSpan.FromDays(1));
        Assert.AreEqual(0, history.DaysUsed("Fake/model"));
    }

    [TestMethod]
    public async Task TheDaysAreKeptOnDisk_AndAnUnreadableFileCountsAsNone()
    {
        var clock = new Clock();
        var path = Path.Combine(TestTemp.CreateSubdirectory("nexaflow-model-use-").FullName, "model-use.json");
        var history = new ModelUseHistory(path, clock);
        history.Record("Fake/model");
        await history.Saved;

        Assert.AreEqual(1, new ModelUseHistory(path, clock).DaysUsed("Fake/model"));

        await File.WriteAllTextAsync(path, "{ not json");
        Assert.AreEqual(0, new ModelUseHistory(path, clock).DaysUsed("Fake/model"));
    }
}
