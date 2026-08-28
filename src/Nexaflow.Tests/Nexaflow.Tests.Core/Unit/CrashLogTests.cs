using System;
using System.IO;
using System.Linq;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The crash log's two jobs, both learned from one customer report: stay bounded on disk, and stop feeding a
/// fault that only recurs because it is being handled. The log that prompted this was 178MB of a single
/// exception repeated 17,724 times in 85 seconds, with no version stamp to say which build produced it.
/// </summary>
[TestClass]
[CoversNode("crash-log")]
public class CrashLogTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexaflow-crashlog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private DateTimeOffset _now = new(2026, 8, 27, 11, 16, 9, TimeSpan.Zero);

    private CrashLog NewLog(string version = "9.9.9.9") => new(() => _dir, () => _now, version);

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
        NewLog().Record(Faulted("boom"));

        var files = Logs();
        Assert.AreEqual(1, files.Length, "one fault should produce exactly one log file");
        Assert.AreEqual("crash-2026-08-27.log", Path.GetFileName(files[0]),
            "the file is dated, so retention has something to work with");

        var text = AllText();
        StringAssert.Contains(text, "9.9.9.9", "the build that crashed must be in the file");
        StringAssert.Contains(text, "boom");
    }

    [TestMethod]
    public void RollsOverAtMidnight()
    {
        var log = NewLog();
        log.Record(Faulted("day one"));
        _now = _now.AddDays(1);
        log.Record(Faulted("day two"));

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

        NewLog().Record(Faulted("today"));

        Assert.IsFalse(File.Exists(stale),  "a log past the retention window should be gone");
        Assert.IsFalse(File.Exists(legacy), "the undated legacy log is pruned on its last-write date");
        Assert.IsTrue(File.Exists(recent),  "a log inside the window must survive");
    }

    [TestMethod]
    public void IdenticalFaults_CollapseToACount()
    {
        var log = NewLog();
        for (int i = 0; i < CrashLog.FullTracesPerBurst + 3; i++) log.Record(Faulted("same fault"));
        log.Flush();

        var text = AllText();
        Assert.AreEqual(CrashLog.FullTracesPerBurst, Occurrences(text, "same fault"),
            "only the first few identical traces are written in full");
        StringAssert.Contains(text, "3 further identical occurrence(s) suppressed");
    }

    [TestMethod]
    public void DifferentFaults_AreNotCollapsed()
    {
        var log = NewLog();
        for (int i = 0; i < 4; i++)
        {
            log.Record(Faulted("alpha"));
            log.Record(Faulted("beta"));
        }
        log.Flush();

        var text = AllText();
        Assert.AreEqual(4, Occurrences(text, "alpha"), "distinct faults must each be recorded");
        Assert.AreEqual(4, Occurrences(text, "beta"));
    }

    [TestMethod]
    public void ASlowDrip_IsNotABurst()
    {
        var log = NewLog();
        for (int i = 0; i < CrashLog.FullTracesPerBurst + 3; i++)
        {
            Assert.IsTrue(log.Record(Faulted("occasional")), "an occasional fault stays survivable");
            _now = _now.AddMinutes(1);   // well outside the burst window
        }

        Assert.AreEqual(CrashLog.FullTracesPerBurst + 3, Occurrences(AllText(), "occasional"),
            "the same fault spread over time is not a burst and must not be suppressed");
    }

    [TestMethod]
    public void ARenderLoop_StopsBeingHandled()
    {
        var log = NewLog();

        for (int i = 1; i < CrashLog.LiveLockCount; i++)
            Assert.IsTrue(log.Record(Faulted("render loop")), $"occurrence {i} should still be handled");

        Assert.IsFalse(log.Record(Faulted("render loop")),
            "past the live-lock threshold the process must be allowed to die rather than spin");
        StringAssert.Contains(AllText(), "allowed to terminate instead of live-locking");
    }

    [TestMethod]
    public void ADaysFile_StopsGrowingAtTheCap()
    {
        // Start the day already at the ceiling, the way a live-lock would have left it.
        File.WriteAllBytes(Path.Combine(_dir, "crash-2026-08-27.log"), new byte[CrashLog.MaxBytesPerDay]);

        var log = NewLog();
        log.Record(Faulted("first past the cap"));
        log.Record(Faulted("second past the cap"));

        var text = AllText();
        Assert.AreEqual(1, Occurrences(text, "cap reached"), "the cap is announced once, not per entry");
        Assert.AreEqual(0, Occurrences(text, "first past the cap"), "no entry is written past the cap");
        Assert.AreEqual(0, Occurrences(text, "second past the cap"));
    }
}
