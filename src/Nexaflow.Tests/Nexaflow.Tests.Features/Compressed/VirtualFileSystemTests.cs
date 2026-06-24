using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>
/// Commit-1 VFS coverage: real-path access is byte-identical to <see cref="System.IO"/>, container
/// splitting behaves with and without handlers, and the archive resolution machinery works end-to-end
/// against an in-memory fake handler (real format handlers arrive in later commits).
/// </summary>
[TestClass]
public class VirtualFileSystemTests
{
    // ── Pass-through byte identity (uses the real process singleton; no handlers needed) ──

    public static IEnumerable<object[]> RealFiles =>
        TestSampleData.Files("text")
            .Concat(TestSampleData.Files("json"))
            .Concat(TestSampleData.Files("binary"))
            .Select(p => new object[] { p });

    [TestMethod]
    [DynamicData(nameof(RealFiles))]
    public void RealFile_PassThrough_IsByteIdentical(string path)
    {
        var vfs = VirtualFileSystem.Instance;

        Assert.IsTrue(vfs.Exists(path));
        Assert.IsFalse(vfs.IsDirectory(path));
        Assert.IsFalse(vfs.IsContainer(path));
        Assert.AreEqual(new FileInfo(path).Length, vfs.GetLength(path));

        CollectionAssert.AreEqual(File.ReadAllBytes(path), vfs.ReadAllBytes(path));

        using var s = vfs.OpenRead(path);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        CollectionAssert.AreEqual(File.ReadAllBytes(path), ms.ToArray());
    }

    [TestMethod]
    public void RealDirectory_Enumerate_MatchesSystemIo()
    {
        var dir = TestSampleData.Path("text");
        var expected = new DirectoryInfo(dir).EnumerateFileSystemInfos()
            .Select(f => f.Name).OrderBy(n => n).ToList();
        var actual = VirtualFileSystem.Instance.EnumerateEntries(dir)
            .Select(e => e.Name).OrderBy(n => n).ToList();
        CollectionAssert.AreEqual(expected, actual);
        Assert.IsTrue(VirtualFileSystem.Instance.IsDirectory(dir));
    }

    [TestMethod]
    public void WriteAllText_WithBomEncoding_EmitsPreamble()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexa-vfs-bom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "x.txt");
            VirtualFileSystem.Instance.WriteAllText(path, "hi", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var bytes = File.ReadAllBytes(path);
            Assert.IsTrue(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "UTF-8 BOM should be written (matching File.WriteAllText).");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── SplitOutermostContainer ──────────────────────────────────────────────

    [TestMethod]
    public void Split_RealFile_NoHandlers_ReturnsPathAndNull()
    {
        var path = TestSampleData.Files("text").First();
        var (container, inner) = VirtualFileSystem.Instance.SplitOutermostContainer(path);
        Assert.AreEqual(path, container);
        Assert.IsNull(inner);
    }

    [TestMethod]
    public void Split_NonExistentPath_ReturnsPathAndNull()
    {
        var path = @"D:\does\not\exist\file.zip\inner\x.txt";
        var (container, inner) = VirtualFileSystem.Instance.SplitOutermostContainer(path);
        Assert.AreEqual(path, container);
        Assert.IsNull(inner);
    }

    [TestMethod]
    public void Split_InsideFakeContainer_FindsOutermostBoundary()
    {
        using var fix = new FakeFixture();
        var inside = Path.Combine(fix.ContainerPath, "inner", "y.txt");

        var (container, inner) = fix.Vfs.SplitOutermostContainer(inside);
        Assert.AreEqual(fix.ContainerPath, container);
        Assert.AreEqual(@"inner\y.txt", inner);
        Assert.IsTrue(fix.Vfs.IsContainer(fix.ContainerPath));
    }

    [TestMethod]
    public void Split_ContainerFileItself_IsNotTreatedAsInside()
    {
        using var fix = new FakeFixture();
        var (container, inner) = fix.Vfs.SplitOutermostContainer(fix.ContainerPath);
        Assert.AreEqual(fix.ContainerPath, container);
        Assert.IsNull(inner);   // the archive file, not a path *inside* it
    }

    // ── Archive resolution end-to-end via the fake handler ───────────────────

    [TestMethod]
    public void Container_EnumerateTopLevel_SynthesisesDirsAndFiles()
    {
        using var fix = new FakeFixture();
        var names = fix.Vfs.EnumerateEntries(fix.ContainerPath).Select(e => e.Name).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "inner", "readme.txt" }, names.ToList());

        var innerDir = fix.Vfs.EnumerateEntries(Path.Combine(fix.ContainerPath, "inner"))
            .Select(e => e.Name).ToList();
        CollectionAssert.AreEqual(new[] { "y.txt" }, innerDir);
    }

    [TestMethod]
    public void Container_OpenInnerEntry_ReturnsSeekableContent()
    {
        using var fix = new FakeFixture();
        var leaf = Path.Combine(fix.ContainerPath, "inner", "y.txt");

        Assert.IsTrue(fix.Vfs.Exists(leaf));
        Assert.IsFalse(fix.Vfs.IsDirectory(leaf));
        Assert.AreEqual(FakeArchiveHandler.InnerBody.Length, fix.Vfs.GetLength(leaf));
        Assert.AreEqual("hello inner", fix.Vfs.ReadAllText(leaf));

        using var s = fix.Vfs.OpenRead(leaf);
        Assert.IsTrue(s.CanSeek);
        s.Seek(6, SeekOrigin.Begin);
        Assert.AreEqual((int)'i', s.ReadByte());
    }

    /// <summary>An isolated VFS with one fake handler over a real placeholder file.</summary>
    private sealed class FakeFixture : IDisposable
    {
        public VirtualFileSystem Vfs { get; }
        public string ContainerPath { get; }
        private readonly string _dir;

        public FakeFixture()
        {
            Vfs = new VirtualFileSystem();
            Vfs.RegisterHandler(new FakeArchiveHandler());
            _dir = Path.Combine(Path.GetTempPath(), "nexa-vfs-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ContainerPath = Path.Combine(_dir, "sample.fake");
            File.WriteAllText(ContainerPath, "placeholder container bytes");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }
    }

    /// <summary>A trivial in-memory "archive": ignores the container bytes, always exposes the same two
    /// entries. Enough to exercise listing, resolution and extraction without a real format library.</summary>
    private sealed class FakeArchiveHandler : IArchiveHandler
    {
        public const string InnerBody = "hello inner";
        private const string ReadmeBody = "readme body";

        public string Name => "Fake";
        public ArchiveCapabilities Capabilities => ArchiveCapabilities.List | ArchiveCapabilities.Extract;
        public bool CanHandle(string fileName) => fileName.EndsWith(".fake", StringComparison.OrdinalIgnoreCase);

        public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
        {
            container.Dispose();   // we don't read the placeholder bytes
            return new Session();
        }

        private sealed class Session : IArchiveSession
        {
            public IReadOnlyList<VirtualEntry> Entries { get; } =
            [
                new VirtualEntry("readme.txt", false, ReadmeBody.Length, ReadmeBody.Length, DateTime.Now),
                new VirtualEntry("inner/y.txt", false, InnerBody.Length, InnerBody.Length, DateTime.Now),
            ];

            public Stream OpenEntry(string entryPath)
            {
                var body = entryPath.Replace('\\', '/').TrimEnd('/').Equals("inner/y.txt", StringComparison.OrdinalIgnoreCase)
                    ? InnerBody : ReadmeBody;
                return new MemoryStream(Encoding.UTF8.GetBytes(body));
            }

            public void Dispose() { }
        }
    }
}
