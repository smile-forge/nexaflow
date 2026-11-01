using System;
using System.IO;
using System.Linq;
using Nexaflow.IO.Common.Logging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// The file behind the shell's safety log and the AI host's: one per process, named for when and which, kept
/// <see cref="SessionLogFile.RetentionDays"/> days and stopped at <see cref="SessionLogFile.MaxBytes"/> — and never a
/// throw, since a log that fails is one the process runs on without.
/// </summary>
[TestClass]
[CoversNode("safety-log")]
public class SessionLogFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 14, 22, 33, TimeSpan.Zero);

    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup() => _dir = TestTemp.CreateSubdirectory("nexaflow-sessionlog-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private SessionLogFile Open(string prefix = "shell") => new(_dir, prefix, () => Now);

    private static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    [TestMethod]
    public void TheFile_IsNamedForItsPrefix_TheMomentItOpened_AndTheProcess()
    {
        using var log = Open();
        log.Write("hello");

        Assert.AreEqual($"shell-20261101-142233-{Environment.ProcessId}.log", Path.GetFileName(log.FilePath));
        Assert.IsTrue(log.IsOpen);
        StringAssert.Contains(Read(log.FilePath), "14:22:33.000  hello");
    }

    [TestMethod]
    public void AHeldLine_KeepsTheTimeItHappened()
    {
        using var log = Open();
        log.Write(Now.AddSeconds(-5), "held");

        StringAssert.Contains(Read(log.FilePath), "14:22:28.000  held");
    }

    [TestMethod]
    public void Opening_PrunesItsOwnPrefixPastTheRetention_AndNothingElse()
    {
        string Planted(string name, int daysOld)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, "old");
            File.SetLastWriteTimeUtc(path, Now.UtcDateTime.AddDays(-daysOld));
            return path;
        }
        var stale  = Planted("shell-20261020-090000-1.log", SessionLogFile.RetentionDays + 1);
        var recent = Planted("shell-20261024-090000-1.log", SessionLogFile.RetentionDays - 1);
        var other  = Planted("aihost-20261020-090000-1.log", SessionLogFile.RetentionDays + 1);

        using var _ = Open();

        Assert.IsFalse(File.Exists(stale), "a shell log past the retention goes");
        Assert.IsTrue(File.Exists(recent), "one inside it stays");
        Assert.IsTrue(File.Exists(other), "another prefix's files are not this log's to prune");
    }

    [TestMethod]
    public void PastTheCap_TheFileSaysSoOnce_AndTakesNothingMore()
    {
        using var log = Open();
        var line = new string('x', 64 * 1024);
        for (int i = 0; i < (SessionLogFile.MaxBytes / line.Length) + 8; i++) log.Write(line);
        log.Write("after the cap");

        var text = Read(log.FilePath);
        Assert.IsTrue(new FileInfo(log.FilePath).Length <= SessionLogFile.MaxBytes + 256, "the cap holds, give or take the line that says so");
        Assert.AreEqual(1, text.Split("cap reached").Length - 1, "the cap is announced once");
        Assert.IsFalse(text.Contains("after the cap"), "nothing is written past it");
    }

    [TestMethod]
    public void AFolderThatCannotBeMade_IsALogThatKeepsNothing_WithoutAThrow()
    {
        var blocker = Path.Combine(_dir, "not-a-folder");
        File.WriteAllText(blocker, "a file where the folder would go");

        using var log = new SessionLogFile(Path.Combine(blocker, "logs"), "shell", () => Now);
        log.Write("lost");

        Assert.IsFalse(log.IsOpen);
    }

    [TestMethod]
    public void AfterDispose_AWrite_IsIgnored()
    {
        var log = Open();
        log.Write("before");
        log.Dispose();
        log.Write("after");

        var text = Read(log.FilePath);
        StringAssert.Contains(text, "before");
        Assert.IsFalse(text.Contains("after"));
    }
}
