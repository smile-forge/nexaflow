using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Nexaflow.Features.Compressed.Codecs;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using ModernNs = Nexaflow.Features.Compressed.Modern;
using SharpCompressNs = Nexaflow.Features.Compressed.SharpCompress;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>
/// Materialises sample files for the formats that need a library or external tool — bz2 / xz / zst / lz4 /
/// br (with and without tar), 7z and rar — into <c>test-samples/archive/</c>, then reads each back through
/// the VFS. The lib-based formats are written in-process via Convert; 7z / xz / tar.xz come from 7-Zip and
/// rar from WinRAR when those are installed (skipped, not failed, when they are not).
/// </summary>
[TestClass]
[CoversNode("compressed-backends")]
public class ExtendedArchiveFormatTests
{
    private const string ArchiveText = "# Archive sample\nText file inside a zip.\n";   // sample.zip → readme.txt
    private const string SoloText    = "single-stream sample payload";

    private static readonly string SevenZip = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
    private static readonly string WinRar = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinRAR", "Rar.exe");

    private static string _dir = "";

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        var sampleZip = TestSampleData.Path("archive", "sample.zip");   // also materialises the base set
        _dir = Path.GetDirectoryName(sampleZip)!;
        var vfs = MakeVfs();

        // In-process (Convert / codec composition) from the multi-file sample.zip.
        foreach (var name in new[] { "formats.tar.zst", "formats.tar.lz4", "formats.tar.br", "formats.tar.bz2" })
            Ensure(Path.Combine(_dir, name), dest => vfs.Repackage(sampleZip, dest, null));

        // Single-stream codecs from a one-file zip.
        var oneZip = Path.Combine(_dir, "_one.zip");
        if (!File.Exists(oneZip))
            using (var z = ZipFile.Open(oneZip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("note.txt").Open()))
                w.Write(SoloText);
        foreach (var name in new[] { "note.zst", "note.lz4", "note.br", "note.bz2", "note.gz" })
            Ensure(Path.Combine(_dir, name), dest => vfs.Repackage(oneZip, dest, null));

        // External tools for the formats with no in-process writer: 7z / xz / tar.xz / rar.
        var stage = Path.Combine(_dir, "_stage");
        Directory.CreateDirectory(Path.Combine(stage, "docs"));
        File.WriteAllText(Path.Combine(stage, "readme.txt"), ArchiveText);
        File.WriteAllText(Path.Combine(stage, "docs", "data.json"), "{ \"name\": \"sample\", \"values\": [1, 2, 3] }\n");

        if (File.Exists(SevenZip))
        {
            Ensure(Path.Combine(_dir, "formats.7z"), d => Tool(SevenZip, stage, "a", "-t7z", "-y", d, "readme.txt", "docs"));
            Ensure(Path.Combine(_dir, "note.xz"),    d => Tool(SevenZip, stage, "a", "-txz", "-y", d, "readme.txt"));
            Ensure(Path.Combine(_dir, "formats.tar.xz"), d =>
            {
                var tar = Path.Combine(stage, "_x.tar");
                Tool(SevenZip, stage, "a", "-ttar", "-y", tar, "readme.txt", "docs");
                Tool(SevenZip, stage, "a", "-txz", "-y", d, tar);
            });
        }
        if (File.Exists(WinRar))
            Ensure(Path.Combine(_dir, "formats.rar"), d => Tool(WinRar, stage, "a", "-ep1", "-r", d, "readme.txt", "docs"));

        // Drop the scratch inputs; keep only the format samples.
        TryDelete(oneZip);
        try { Directory.Delete(stage, recursive: true); } catch { /* best effort */ }
    }

    [TestMethod]
    [DataRow("formats.tar.zst", "formats.tar/readme.txt", ArchiveText, false)]
    [DataRow("formats.tar.lz4", "formats.tar/readme.txt", ArchiveText, false)]
    [DataRow("formats.tar.br",  "formats.tar/readme.txt", ArchiveText, false)]
    [DataRow("formats.tar.bz2", "formats.tar/readme.txt", ArchiveText, false)]
    [DataRow("note.zst", "note", SoloText, false)]
    [DataRow("note.lz4", "note", SoloText, false)]
    [DataRow("note.br",  "note", SoloText, false)]
    [DataRow("note.bz2", "note", SoloText, false)]
    [DataRow("note.gz",  "note", SoloText, false)]
    [DataRow("formats.7z",     "readme.txt",            ArchiveText, true)]
    [DataRow("note.xz",        "note",                  ArchiveText, true)]   // xz of readme.txt
    [DataRow("formats.tar.xz", "formats.tar/readme.txt", ArchiveText, true)]
    [DataRow("formats.rar",    "readme.txt",            ArchiveText, true)]
    public void Sample_ReadsThroughVfs(string fileName, string innerPath, string expected, bool requiresTool)
    {
        var path = Path.Combine(_dir, fileName);
        if (requiresTool && !File.Exists(path))
        {
            Assert.Inconclusive($"{fileName} not generated — 7-Zip / WinRAR not installed.");
            return;
        }
        Assert.IsTrue(File.Exists(path), $"Sample '{fileName}' was not generated.");

        var vfs = MakeVfs();
        var full = Path.Combine(path, innerPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.AreEqual(expected, vfs.ReadAllText(full), $"reading {fileName}");
    }

    private static void Ensure(string dest, Action<string> generate)
    {
        if (File.Exists(dest)) return;
        try { generate(dest); } catch { /* lib/tool hiccup — the read test will report it as missing */ }
    }

    private static void Tool(string exe, string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    private static VirtualFileSystem MakeVfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        vfs.RegisterHandler(new SharpCompressNs.SharpCompressHandler());
        vfs.RegisterHandler(new ModernNs.ModernCompressionHandler());
        vfs.RegisterHandler(new BrotliArchiveHandler());
        vfs.RegisterCodec(new GzipCodec());
        vfs.RegisterCodec(new BrotliCodec());
        vfs.RegisterCodec(new ModernNs.ZstdCodec());
        vfs.RegisterCodec(new ModernNs.Lz4Codec());
        vfs.RegisterCodec(new SharpCompressNs.Bzip2Codec());
        return vfs;
    }
}
