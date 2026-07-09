using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Core.Unit.IO;

/// <summary>Unit tests for the safe recursive directory move (copy-then-delete).</summary>
[TestClass]
public class DirectoryMoverTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-mover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task MoveAsync_CopiesNestedTree_ThenDeletesSource()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(src, "sub", "deep"));
        File.WriteAllText(Path.Combine(src, "top.txt"), "top");
        File.WriteAllText(Path.Combine(src, "sub", "mid.txt"), "mid");
        File.WriteAllText(Path.Combine(src, "sub", "deep", "leaf.txt"), "leaf");

        var dest = Path.Combine(_root, "dest");
        await DirectoryMover.MoveAsync(src, dest);

        Assert.IsFalse(Directory.Exists(src), "source is removed after a successful move");
        Assert.AreEqual("top",  File.ReadAllText(Path.Combine(dest, "top.txt")));
        Assert.AreEqual("mid",  File.ReadAllText(Path.Combine(dest, "sub", "mid.txt")));
        Assert.AreEqual("leaf", File.ReadAllText(Path.Combine(dest, "sub", "deep", "leaf.txt")));
    }

    [TestMethod]
    public async Task MoveAsync_CopiesFileHeldOpenElsewhere()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        var openFile = Path.Combine(src, "open.txt");
        File.WriteAllText(openFile, "content");

        var dest = Path.Combine(_root, "dest");
        using (new FileStream(openFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            await DirectoryMover.MoveAsync(src, dest);
        }

        Assert.AreEqual("content", File.ReadAllText(Path.Combine(dest, "open.txt")));
    }

    [TestMethod]
    public async Task MoveAsync_ThrowsWhenDestinationExists()
    {
        var src  = Path.Combine(_root, "src");
        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dest);

        await Assert.ThrowsExactlyAsync<IOException>(() => DirectoryMover.MoveAsync(src, dest));
        Assert.IsTrue(Directory.Exists(src), "source is untouched when the move is rejected");
    }

    [TestMethod]
    public async Task MoveAsync_ThrowsWhenSourceMissing()
    {
        var src  = Path.Combine(_root, "ghost");
        var dest = Path.Combine(_root, "dest");
        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() => DirectoryMover.MoveAsync(src, dest));
    }

    [TestMethod]
    public async Task MoveAsync_HonoursCancellation()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.txt"), "a");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => DirectoryMover.MoveAsync(src, Path.Combine(_root, "dest"), cts.Token));
    }
}
