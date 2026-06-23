using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Core.Unit.IO;

[TestClass]
public class FileSplitterTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nexa-split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteSource(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content); // UTF-8, no BOM, no newline translation
        return path;
    }

    private static byte[] Concat(SplitResult result)
        => result.OutputFiles.SelectMany(File.ReadAllBytes).ToArray();

    [TestMethod]
    public async Task ByLineCount_SplitsAndReassemblesExactly()
    {
        var content = string.Concat(Enumerable.Range(1, 10).Select(i => $"line{i}\n"));
        var src = WriteSource("ten.txt", content);

        var result = await FileSplitter.SplitAsync(src, new SplitOptions { Mode = SplitMode.ByLineCount, LinesPerPart = 3 });

        Assert.AreEqual(4, result.OutputFiles.Count);   // 3 + 3 + 3 + 1
        Assert.AreEqual(10, result.TotalLines);
        CollectionAssert.AreEqual(File.ReadAllBytes(src), Concat(result));
    }

    [TestMethod]
    public async Task BySize_KeepsPartsUnderLimitAndReassemblesExactly()
    {
        var content = string.Concat(Enumerable.Range(1, 20).Select(i => $"row {i:D2}\n")); // each line 7 bytes
        var src = WriteSource("rows.txt", content);
        const int max = 30;

        var result = await FileSplitter.SplitAsync(src, new SplitOptions { Mode = SplitMode.BySize, MaxBytesPerPart = max });

        foreach (var f in result.OutputFiles)
            Assert.IsTrue(new FileInfo(f).Length <= max, $"part {f} exceeds {max} bytes");
        CollectionAssert.AreEqual(File.ReadAllBytes(src), Concat(result));
    }

    [TestMethod]
    public async Task ByRegex_StartsNewPartOnMatchAndReassemblesExactly()
    {
        var src = WriteSource("doc.txt", "# A\n1\n2\n# B\n3\n");

        var result = await FileSplitter.SplitAsync(src, new SplitOptions { Mode = SplitMode.ByRegex, BoundaryPattern = "^# " });

        Assert.AreEqual(2, result.OutputFiles.Count);
        Assert.AreEqual("# A\n1\n2\n", File.ReadAllText(result.OutputFiles[0]));
        Assert.AreEqual("# B\n3\n",    File.ReadAllText(result.OutputFiles[1]));
        CollectionAssert.AreEqual(File.ReadAllBytes(src), Concat(result));
    }

    [TestMethod]
    public async Task BySize_OversizedSingleLine_GoesWholeIntoOwnPart()
    {
        var src = WriteSource("big.txt", "short\n" + new string('x', 100) + "\nshort\n");

        var result = await FileSplitter.SplitAsync(src, new SplitOptions { Mode = SplitMode.BySize, MaxBytesPerPart = 10 });

        CollectionAssert.AreEqual(File.ReadAllBytes(src), Concat(result));
        Assert.IsTrue(result.OutputFiles.Count >= 3);
    }

    [TestMethod]
    public async Task InvalidOptions_Throw()
    {
        var src = WriteSource("x.txt", "a\n");
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => FileSplitter.SplitAsync(src, new SplitOptions { Mode = SplitMode.BySize, MaxBytesPerPart = 0 }));
    }
}
