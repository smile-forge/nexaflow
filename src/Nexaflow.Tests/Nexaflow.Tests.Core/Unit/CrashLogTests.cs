using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Nexaflow.Core.Services.Logging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The crash log's job, learned from one customer report: stay bounded on disk. The log that prompted this was 178MB of a single
/// exception repeated 17,724 times in 85 seconds, with no version stamp to say which build produced it. What it writes is what
/// reaches it through the logs at <see cref="LogLevel.Error"/> and above, and nothing else — a UI journey fails on any crash file.
/// </summary>
[TestClass]
[CoversNode("crash-log")]
public class CrashLogTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(TestTemp.Root, "nexaflow-crashlog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private DateTimeOffset _now = new(2026, 8, 27, 11, 16, 9, TimeSpan.Zero);

    private CrashLogProvider _crash = null!;

    /// <summary>A logger whose entries reach only a crash log in the test's folder, on the test's clock.</summary>
    private ILogger NewLog(string version = "9.9.9.9")
    {
        _crash = new CrashLogProvider(() => _dir, () => _now, version);
        return new LoggerFactory([_crash]).CreateLogger("Tests");
    }

    private static Exception Faulted(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (Exception ex) { return ex; }
    }

    private string[] Logs() => Directory.GetFiles(_dir, "crash*.log").OrderBy(p => p).ToArray();

    private string AllText() => string.Join("\n", Logs().Select(File.ReadAllText));

    private static int Occurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    [TestMethod]
    public void WritesADatedFile_StampedWithTheVersion()
    {
        NewLog().LogError(Faulted("boom"), "unhandled on the UI thread");

        var files = Logs();
        Assert.AreEqual(1, files.Length, "one fault should produce exactly one log file");
        Assert.AreEqual("crash-2026-08-27.log", Path.GetFileName(files[0]),
            "the file is dated, so retention has something to work with");
        Assert.AreEqual(_crash.CurrentPath, files[0], "the file named to the user is the one written");

        var text = AllText();
        StringAssert.Contains(text, "9.9.9.9", "the build that crashed must be in the file");
        StringAssert.Contains(text, "unhandled on the UI thread", "what met the fault is said above its trace");
        StringAssert.Contains(text, "boom");
    }

    [TestMethod]
    public void BelowAFault_NothingIsWritten()
    {
        var log = NewLog();
        log.LogWarning(Faulted("tried and carried on"), "loading {File}", "Nexaflow.Features.Broken.dll");
        log.LogInformation("startup");

        Assert.AreEqual(0, Logs().Length, "a failure the shell carries on without, or a step, is no crash — and a journey fails on any file");
    }

    [TestMethod]
    public void AnEntryWithoutAFault_IsItsMessage()
    {
        NewLog().LogCritical("the fault above repeated {Repeats} times", 250);

        StringAssert.Contains(AllText(), "--- the fault above repeated 250 times ---");
    }

    [TestMethod]
    public void RollsOverAtMidnight()
    {
        var log = NewLog();
        log.LogError(Faulted("day one"), "fault");
        _now = _now.AddDays(1);
        log.LogError(Faulted("day two"), "fault");

        CollectionAssert.AreEqual(
            new[] { "crash-2026-08-27.log", "crash-2026-08-28.log" },
            Logs().Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public void PrunesLogsPastTheRetentionWindow()
    {
        var stale  = Path.Combine(_dir, "crash-2026-08-10.log");   // 17 days back
        var recent = Path.Combine(_dir, "crash-2026-08-25.log");   // 2 days back
        var legacy = Path.Combine(_dir, "crash.log");              // pre-rotation, the 170MB shape
        File.WriteAllText(stale,  "old");
        File.WriteAllText(recent, "recent");
        File.WriteAllText(legacy, "the one that grew forever");
        File.SetLastWriteTime(legacy, _now.AddDays(-30).LocalDateTime);

        NewLog().LogError(Faulted("today"), "fault");

        Assert.IsFalse(File.Exists(stale),  "a log past the retention window should be gone");
        Assert.IsFalse(File.Exists(legacy), "the undated legacy log is pruned on its last-write date");
        Assert.IsTrue(File.Exists(recent),  "a log inside the window must survive");
    }

    [TestMethod]
    public void IdenticalFaults_CollapseToACount()
    {
        var log = NewLog();
        for (int i = 0; i < CrashLogProvider.FullTracesPerBurst + 3; i++) log.LogError(Faulted("same fault"), "fault");
        _crash.Flush();

        var text = AllText();
        Assert.AreEqual(CrashLogProvider.FullTracesPerBurst, Occurrences(text, "same fault"),
            "only the first few identical traces are written in full");
        StringAssert.Contains(text, "3 further identical occurrence(s) suppressed");
    }

    [TestMethod]
    public void ACollapsedBurst_IsCountedWhenTheLogEnds()
    {
        var log = NewLog();
        for (int i = 0; i < CrashLogProvider.FullTracesPerBurst + 2; i++) log.LogError(Faulted("tail-end burst"), "fault");

        _crash.Dispose();

        StringAssert.Contains(AllText(), "2 further identical occurrence(s) suppressed",
            "a burst still collapsing when the process ends must not go unrecorded");
    }

    [TestMethod]
    public void DifferentFaults_AreNotCollapsed()
    {
        var log = NewLog();
        for (int i = 0; i < 4; i++)
        {
            log.LogError(Faulted("alpha"), "fault");
            log.LogError(Faulted("beta"), "fault");
        }
        _crash.Flush();

        var text = AllText();
        Assert.AreEqual(4, Occurrences(text, "alpha"), "distinct faults must each be recorded");
        Assert.AreEqual(4, Occurrences(text, "beta"));
    }

    [TestMethod]
    public void ASlowDrip_IsNotABurst()
    {
        var log = NewLog();
        for (int i = 0; i < CrashLogProvider.FullTracesPerBurst + 3; i++)
        {
            log.LogError(Faulted("occasional"), "fault");
            _now = _now.AddMinutes(1);   // well outside the burst window
        }

        Assert.AreEqual(CrashLogProvider.FullTracesPerBurst + 3, Occurrences(AllText(), "occasional"),
            "the same fault spread over time is not a burst and must not be suppressed");
    }

    [TestMethod]
    public void TheLineEndingALiveLock_FollowsTheCountOfWhatItSuppressed()
    {
        var log = NewLog();
        for (int i = 0; i < CrashLogProvider.FullTracesPerBurst + 3; i++) log.LogError(Faulted("render loop"), "unhandled on the UI thread");
        log.LogCritical("the fault above repeated {Repeats} times; the process is being allowed to terminate", 8);

        var text       = AllText();
        var suppressed = text.IndexOf("3 further identical occurrence(s) suppressed", StringComparison.Ordinal);
        var ending     = text.IndexOf("--- the fault above repeated 8 times; the process is being allowed to terminate ---", StringComparison.Ordinal);
        Assert.IsTrue(suppressed >= 0 && ending > suppressed, text);
    }

    [TestMethod]
    public void ADaysFile_StopsGrowingAtTheCap()
    {
        // Start the day already at the ceiling, the way a live-lock would have left it.
        File.WriteAllBytes(Path.Combine(_dir, "crash-2026-08-27.log"), new byte[CrashLogProvider.MaxBytesPerDay]);

        var log = NewLog();
        log.LogError(Faulted("first past the cap"), "fault");
        log.LogError(Faulted("second past the cap"), "fault");

        var text = AllText();
        Assert.AreEqual(1, Occurrences(text, "cap reached"), "the cap is announced once, not per entry");
        Assert.AreEqual(0, Occurrences(text, "first past the cap"), "no entry is written past the cap");
        Assert.AreEqual(0, Occurrences(text, "second past the cap"));
    }
}
