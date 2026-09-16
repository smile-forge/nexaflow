using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A launch whose startup sequence throws ends: recorded, the single-instance guard released before anything can
/// block, the user told why and where the details are — rather than living on with no window and swallowing every
/// later launch.
/// </summary>
[TestClass]
[CoversNode("crash-log")]
public class StartupFailureTests
{
    private readonly List<string> _steps = [];
    private string?  _told;
    private string   _dir = string.Empty;
    private CrashLog _log = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexaflow-startup-failure-" + Guid.NewGuid().ToString("N"));
        var now = DateTimeOffset.Now;   // one clock reading, so the log a test reads is the day it was written
        _log = new CrashLog(() => _dir, () => now, "test");
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void AnAttendedLaunch_IsRecorded_ReleasesTheGuard_ThenTells_ThenExitsNonZero()
    {
        NewFailure().Handle(new InvalidOperationException("the shell config did not load"), attended: true);

        CollectionAssert.AreEqual(new[] { "release", "tell", "exit 1" }, _steps,
            "the guard goes before the message, so a relaunch while it is open starts for itself");
        StringAssert.Contains(File.ReadAllText(_log.CurrentPath), "the shell config did not load");
    }

    [TestMethod]
    public void TheMessage_SaysWhy_AndWhereTheDetailsAre()
    {
        NewFailure().Handle(new InvalidOperationException("the shell config did not load"), attended: true);

        StringAssert.Contains(_told!, "the shell config did not load");
        StringAssert.Contains(_told!, _log.CurrentPath);
    }

    [TestMethod]
    public void AnUnattendedLaunch_ExitsWithoutAMessage()
    {
        NewFailure().Handle(new InvalidOperationException("the shell config did not load"), attended: false);

        CollectionAssert.AreEqual(new[] { "release", "exit 1" }, _steps,
            "the daemon, a UI journey and a timing run have no one to dismiss a message");
        Assert.IsNull(_told);
    }

    private StartupFailure NewFailure() => new(
        _log,
        releaseInstance: () => _steps.Add(File.Exists(_log.CurrentPath) ? "release" : "release before the fault was recorded"),
        tell:            message => { _told = message; _steps.Add("tell"); },
        exit:            code => _steps.Add($"exit {code}"));
}
