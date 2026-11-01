using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The shell's safety log: off unless the user turns it on, one file per session from startup to shutdown, and what
/// startup records before the setting is known held until it is — then written out beneath the banner, or dropped.
/// </summary>
[TestClass]
[CoversNode("safety-log")]
public class ShellSafetyLogTests
{
    private string _dir = string.Empty;
    private DateTimeOffset _now = new(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);

    [TestInitialize]
    public void Setup() => _dir = Path.Combine(TestTemp.CreateSubdirectory("nexaflow-safetylog-").FullName, "logs");

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true); } catch (IOException) { }
    }

    private ShellSafetyLog NewLog() => new(() => _dir, () => _now, "9.9.9.9");

    private string[] Files() =>
        Directory.Exists(_dir) ? Directory.GetFiles(_dir, $"{ShellSafetyLog.FilePrefix}-*.log") : [];

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
        log.Step("startup");
        log.Step("startup: Init.ConfigManager");

        log.Apply(true);
        log.Step("startup: Init.Theme");
        log.Close();

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
        log.Step("startup");

        log.Apply(false);
        log.Step("startup: Init.Theme");
        log.Failed("loading Nexaflow.Features.Broken.dll", Thrown("missing"));
        log.Close();

        Assert.AreEqual(0, Files().Length);
        Assert.IsNull(log.Folder, "the AI host is told to keep no log");
    }

    [TestMethod]
    public void TurnedOnMidSession_TheFileSaysSo_AndTheFolderIsOffered()
    {
        var log = NewLog();
        log.Step("startup");
        log.Apply(false);

        log.Apply(true);

        Assert.AreEqual(_dir, log.Folder, "the AI host started from now on logs beside the shell");
        var text = Read(log.CurrentPath!);
        StringAssert.Contains(text, "turned on mid-session");
        Assert.IsFalse(text.Contains("  startup" + Environment.NewLine), "what startup held was dropped when the setting said off");
    }

    [TestMethod]
    public void TurnedOff_TheFileSaysSo_AndTakesNothingMore()
    {
        var log = NewLog();
        log.Apply(true);
        var path = log.CurrentPath!;

        log.Apply(false);
        log.Step("after it was turned off");

        Assert.IsNull(log.Folder);
        var text = Read(path);
        StringAssert.Contains(text, "safety logging turned off");
        Assert.IsFalse(text.Contains("after it was turned off"));
    }

    [TestMethod]
    public void AFailure_IsOneLine_NamingWhatWasTried()
    {
        var log = NewLog();
        log.Apply(true);

        log.Failed("activating Nexaflow.Features.Broken.dll", Thrown("first line\nsecond line"));

        StringAssert.Contains(Read(log.CurrentPath!),
            "FAILED activating Nexaflow.Features.Broken.dll: System.InvalidOperationException: first line second line");
    }

    [TestMethod]
    public void ATypeLoadFailure_NamesTheLoadersReasons()
    {
        var ex = new ReflectionTypeLoadException([null], [new FileNotFoundException("Could not load Missing.Dependency")]);

        StringAssert.Contains(ShellSafetyLog.Describe(ex), "Could not load Missing.Dependency",
            "the loader's reason names the missing dependency; the outer message never does");
    }

    [TestMethod]
    public void EachFaultTheCrashLogWritesInFull_IsOneLineInTheSession()
    {
        var session = NewLog();
        session.Apply(true);
        var crashDir = Path.Combine(Path.GetDirectoryName(_dir)!, "crash");
        var crash = new CrashLog(() => crashDir, () => _now, "9.9.9.9", session);

        var fault = Thrown("boom");
        for (int i = 0; i < CrashLog.FullTracesPerBurst + 3; i++) crash.Record(fault);

        var text = Read(session.CurrentPath!);
        Assert.AreEqual(CrashLog.FullTracesPerBurst, text.Split("fault recorded in crash-2026-11-01.log").Length - 1,
            "a burst the crash log collapses to a count is not repeated here either");
        StringAssert.Contains(text, "System.InvalidOperationException: boom");
    }
}
