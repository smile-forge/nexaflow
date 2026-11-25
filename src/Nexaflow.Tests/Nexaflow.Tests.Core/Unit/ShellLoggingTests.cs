using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.Logging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The process's logs as the app opens them over a launch's folders: one factory, each entry reaching every destination, each
/// destination keeping to its own — a fault to the crash log in the configuration folder, the session's account to its log under
/// the local folder while safety logging is on, binding errors to their file only under UI automation.
/// </summary>
[TestClass]
[CoversNode("crash-log")]
[CoversNode("safety-log")]
public class ShellLoggingTests
{
    private AppDataFolders _folders = null!;

    [TestInitialize]
    public void Setup()
    {
        var root = TestTemp.CreateSubdirectory("nexaflow-logging-").FullName;
        _folders = new AppDataFolders(Path.Combine(root, "config"), Path.Combine(root, "local"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(Path.GetDirectoryName(_folders.BaseDir)!, recursive: true); } catch (IOException) { }
    }

    private static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    private string[] CrashLogs() =>
        Directory.Exists(_folders.BaseDir) ? Directory.GetFiles(_folders.BaseDir, "crash*.log") : [];

    [TestMethod]
    public void AFault_ReachesTheCrashLog_AndTheSessionsAccount()
    {
        using var logging = ShellLogging.Open(_folders, "1.2.3.4", uiTest: false, profileStartup: false);
        logging.Session.Apply(true);
        var log = logging.Factory.CreateLogger<ShellLoggingTests>();

        log.LogError(new InvalidOperationException("boom"), "unhandled on the UI thread");

        Assert.AreEqual(logging.Crash.CurrentPath, CrashLogs().Single(), "the crash log is in the configuration folder");
        StringAssert.Contains(File.ReadAllText(logging.Crash.CurrentPath), "boom");
        StringAssert.Contains(Read(logging.Session.CurrentPath!), "fault recorded in " + Path.GetFileName(logging.Crash.CurrentPath));
        Assert.AreEqual(Path.Combine(_folders.LocalDir, "logs"), logging.Session.Folder, "the session's log is under the local folder");
    }

    [TestMethod]
    public void AStep_ReachesOnlyTheSessionsAccount()
    {
        using var logging = ShellLogging.Open(_folders, "1.2.3.4", uiTest: false, profileStartup: false);
        logging.Session.Apply(true);

        logging.Factory.CreateLogger<ShellLoggingTests>().LogInformation("startup");

        Assert.AreEqual(0, CrashLogs().Length, "a step is no fault");
        StringAssert.Contains(Read(logging.Session.CurrentPath!), "startup");
    }

    [TestMethod]
    public void BindingErrors_AreWrittenDown_OnlyUnderUiAutomation()
    {
        foreach (var uiTest in new[] { false, true })
        {
            using var logging = ShellLogging.Open(_folders, "1.2.3.4", uiTest, profileStartup: false);
            logging.Factory.CreateLogger(BindingErrorLogProvider.Category)
                   .LogWarning(new EventId(40), "{BindingError}", "BindingExpression path error: 'Missing' property not found");

            Assert.AreEqual(uiTest, File.Exists(Path.Combine(_folders.BaseDir, BindingErrorLogProvider.FileName)), $"uiTest: {uiTest}");
        }
    }

    [TestMethod]
    public void TheProfiler_IsThere_OnlyWhenTheLaunchAskedForIt()
    {
        using (var plain = ShellLogging.Open(_folders, "1.2.3.4", uiTest: false, profileStartup: false))
            Assert.IsNull(plain.Timings);

        using var profiled = ShellLogging.Open(_folders, "1.2.3.4", uiTest: false, profileStartup: true);
        Assert.IsNotNull(profiled.Timings);
    }

    [TestMethod]
    public void Ending_WritesOutACollapsedBurst_AndClosesTheSessionsLog()
    {
        var logging = ShellLogging.Open(_folders, "1.2.3.4", uiTest: false, profileStartup: false);
        logging.Session.Apply(true);
        var log = logging.Factory.CreateLogger<ShellLoggingTests>();
        Exception fault;
        try { throw new InvalidOperationException("burst"); }
        catch (Exception ex) { fault = ex; }
        for (int i = 0; i < CrashLogProvider.FullTracesPerBurst + 2; i++) log.LogError(fault, "unhandled on the UI thread");
        var sessionPath = logging.Session.CurrentPath!;

        logging.Dispose();

        StringAssert.Contains(File.ReadAllText(logging.Crash.CurrentPath), "2 further identical occurrence(s) suppressed");
        Assert.IsNull(logging.Session.CurrentPath, "the session's log is closed");
        Assert.IsTrue(File.Exists(sessionPath));
    }
}
