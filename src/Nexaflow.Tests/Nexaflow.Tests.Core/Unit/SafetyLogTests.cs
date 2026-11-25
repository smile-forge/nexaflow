using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Nexaflow.AiHost.Client;
using Nexaflow.Core.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The session's log, kept while safety logging is on: off unless the user turns it on, one file per session from startup to
/// shutdown, and what startup logs before the setting is known held until it is — then written out beneath the banner, or dropped.
/// </summary>
[TestClass]
[CoversNode("safety-log")]
public class SafetyLogTests
{
    private string _dir = string.Empty;
    private DateTimeOffset _now = new(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);
    private SessionLogProvider _session = null!;

    [TestInitialize]
    public void Setup() => _dir = Path.Combine(TestTemp.CreateSubdirectory("nexaflow-safetylog-").FullName, "logs");

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true); } catch (IOException) { }
    }

    /// <summary>A logger whose entries reach a session log in the test's folder — and, when <paramref name="others"/> are given,
    /// those destinations too.</summary>
    private ILogger NewLog(params LogDestination[] others)
    {
        _session = new SessionLogProvider(() => _dir, () => _now, "9.9.9.9", CrashLogProvider.FileNameFor);
        return new LoggerFactory([_session, .. others]).CreateLogger("Tests");
    }

    private string[] Files() =>
        Directory.Exists(_dir) ? Directory.GetFiles(_dir, $"{SessionLogProvider.FilePrefix}-*.log") : [];

    private static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    private static Exception Thrown(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (Exception ex) { return ex; }
    }

    [TestMethod]
    public void WhatStartupHeld_IsWrittenBeneathTheBanner_WhenLoggingIsOn()
    {
        var log = NewLog();
        log.LogInformation("startup");
        log.StartupMilestone("Init.ConfigManager");

        _session.Apply(true);
        log.StartupMilestone("Init.Theme");
        log.LogInformation("shutdown complete");
        _session.Dispose();

        var text = Read(Files().Single());
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        StringAssert.Contains(lines[0], "=== Nexaflow 9.9.9.9", "the banner says which build wrote it");
        StringAssert.EndsWith(lines[1], "startup");
        StringAssert.EndsWith(lines[2], "startup: Init.ConfigManager");
        StringAssert.EndsWith(lines[3], "startup: Init.Theme");
        StringAssert.EndsWith(lines[^1], "shutdown complete");
    }

    [TestMethod]
    public void WithLoggingOff_NothingIsWritten_AndNoFolderIsOffered()
    {
        var log = NewLog();
        log.LogInformation("startup");

        _session.Apply(false);
        log.StartupMilestone("Init.Theme");
        log.LogWarning(Thrown("missing"), "loading {File}", "Nexaflow.Features.Broken.dll");
        log.LogInformation("shutdown complete");
        _session.Dispose();

        Assert.AreEqual(0, Files().Length);
        Assert.IsNull(_session.Folder, "the AI host is told to keep no log");
    }

    [TestMethod]
    public void TurnedOnMidSession_TheFileSaysSo_AndTheFolderIsOffered()
    {
        var log = NewLog();
        log.LogInformation("startup");
        _session.Apply(false);

        _session.Apply(true);

        Assert.AreEqual(_dir, _session.Folder, "the AI host started from now on logs beside the shell");
        var text = Read(_session.CurrentPath!);
        StringAssert.Contains(text, "turned on mid-session");
        Assert.IsFalse(text.Contains("  startup" + Environment.NewLine), "what startup held was dropped when the setting said off");
    }

    [TestMethod]
    public void TurnedOff_TheFileSaysSo_AndTakesNothingMore()
    {
        var log = NewLog();
        _session.Apply(true);
        var path = _session.CurrentPath!;

        _session.Apply(false);
        log.LogInformation("after it was turned off");

        Assert.IsNull(_session.Folder);
        var text = Read(path);
        StringAssert.Contains(text, "safety logging turned off");
        Assert.IsFalse(text.Contains("after it was turned off"));
    }

    [TestMethod]
    public void BelowInformation_NothingIsKept()
    {
        var log = NewLog();
        _session.Apply(true);

        log.LogDebug("a detail the session does not keep");

        Assert.IsFalse(Read(_session.CurrentPath!).Contains("a detail the session does not keep"));
    }

    [TestMethod]
    public void AFailure_IsOneLine_NamingWhatWasTried()
    {
        var log = NewLog();
        _session.Apply(true);

        log.LogWarning(Thrown("first line\nsecond line"), "activating {File}", "Nexaflow.Features.Broken.dll");

        StringAssert.Contains(Read(_session.CurrentPath!),
            "FAILED activating Nexaflow.Features.Broken.dll: System.InvalidOperationException: first line second line");
    }

    [TestMethod]
    public void AnAiHostThatDied_IsRecordedWithWhyAndItsLastWords_OnlyWhileLoggingIsOn()
    {
        var exit = new AiHostExitedEventArgs(4242, unchecked((int)0xC0000409),
            "the AI host exited with exit code 0xC0000409 (STATUS_STACK_BUFFER_OVERRUN: fast-fail: abort() or a failed runtime check)",
            ["CUDA error: an illegal memory access was encountered", "ggml aborted: CUDA error"]);

        var off = NewLog();
        _session.Apply(false);
        ShellHost.RecordAiHostEnded(off, exit);
        _session.Dispose();
        Assert.AreEqual(0, Files().Length, "with logging off, a host's crash is written nowhere");

        var on = NewLog();
        _session.Apply(true);
        ShellHost.RecordAiHostEnded(on, exit);

        var text = Read(_session.CurrentPath!);
        StringAssert.Contains(text, "AI host process 4242 lost: the AI host exited with exit code 0xC0000409 (STATUS_STACK_BUFFER_OVERRUN");
        StringAssert.Contains(text, "AI host process 4242 error output: CUDA error: an illegal memory access was encountered");
        StringAssert.Contains(text, "AI host process 4242 error output: ggml aborted: CUDA error");
    }

    [TestMethod]
    public void ATypeLoadFailure_NamesTheLoadersReasons()
    {
        var ex = new ReflectionTypeLoadException([null], [new FileNotFoundException("Could not load Missing.Dependency")]);

        StringAssert.Contains(SessionLogProvider.Describe(ex), "Could not load Missing.Dependency",
            "the loader's reason names the missing dependency; the outer message never does");
    }

    [TestMethod]
    public void EachFaultTheCrashLogWritesInFull_IsOneLineInTheSession()
    {
        var crashDir = Path.Combine(Path.GetDirectoryName(_dir)!, "crash");
        var log = NewLog(new CrashLogProvider(() => crashDir, () => _now, "9.9.9.9"));
        _session.Apply(true);

        var fault = Thrown("boom");
        for (int i = 0; i < CrashLogProvider.FullTracesPerBurst + 3; i++) log.LogError(fault, "unhandled on the UI thread");

        var text = Read(_session.CurrentPath!);
        Assert.AreEqual(CrashLogProvider.FullTracesPerBurst, text.Split("fault recorded in crash-2026-11-01.log").Length - 1,
            "a burst the crash log collapses to a count is not repeated here either");
        StringAssert.Contains(text, "System.InvalidOperationException: boom");
        Assert.IsTrue(File.Exists(Path.Combine(crashDir, "crash-2026-11-01.log")), "the trace itself is in the crash log the line names");
    }
}
