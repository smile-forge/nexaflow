using Nexaflow.AiHost.Client;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// A host's error output: its last lines held for the record of its end, and — only while the app keeps logs — every
/// line written as it arrives to a file of its own in the logs folder.
/// </summary>
[TestClass]
[CoversNode("safety-log")]
public class HostErrorOutputTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup() => _dir = Path.Combine(TestTemp.CreateSubdirectory("nexaflow-hosterrors-").FullName, "logs");

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true); } catch (IOException) { }
    }

    private static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    [TestMethod]
    public void WithLoggingOff_OnlyTheLastLinesAreKept_AndNothingIsWritten()
    {
        using var output = new HostErrorOutput(directory: null, processId: 4242);

        for (int i = 1; i <= HostErrorOutput.KeptLines + 5; i++) output.Take($"line {i}");

        Assert.AreEqual(HostErrorOutput.KeptLines, output.Last.Count);
        Assert.AreEqual("line 6", output.Last[0], "the oldest go first");
        Assert.AreEqual($"line {HostErrorOutput.KeptLines + 5}", output.Last[^1]);
        Assert.IsFalse(Directory.Exists(_dir), "nothing is written anywhere while logging is off");
    }

    [TestMethod]
    public void WithLoggingOn_EveryLineIsWrittenAsItArrives_ToAFileOfItsOwn()
    {
        using var output = new HostErrorOutput(_dir, processId: 4242);
        Assert.IsFalse(Directory.Exists(_dir) && Directory.EnumerateFiles(_dir).Any(), "a host that writes nothing leaves no file");

        for (int i = 1; i <= HostErrorOutput.KeptLines + 5; i++) output.Take($"line {i}");

        var file = Directory.GetFiles(_dir, $"{HostErrorOutput.FilePrefix}-*.log").Single();
        var text = Read(file);
        StringAssert.Contains(text, "error output of AI host process 4242");
        StringAssert.Contains(text, "line 1" + Environment.NewLine, "every line, not only the last ones held");
        StringAssert.Contains(text, $"line {HostErrorOutput.KeptLines + 5}");
    }
}
