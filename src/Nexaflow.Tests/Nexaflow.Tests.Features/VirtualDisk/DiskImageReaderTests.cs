using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.VirtualDisk.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>The DiscUtils-backed reader against real, freshly-built images — proving directory-scoped reads
/// (one directory / one file at a time), not a whole-volume walk.</summary>
[TestClass]
[CoversNode("vdisk-reader")]
public class DiskImageReaderTests
{
    [TestMethod]
    public void Vhd_Describe_ReportsPartitionedFatVolume()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        using var reader = DiskImageReader.Open(fix.VhdPath);

        var info = reader.Describe();
        Assert.AreEqual("VHD", info.Format);
        Assert.AreEqual("MBR", info.PartitionScheme);
        Assert.AreEqual(1, info.Volumes.Count);
        StringAssert.Contains(info.Volumes[0].FileSystem.ToUpperInvariant(), "FAT");
    }

    [TestMethod]
    public void Vhd_ListChildren_ReadsRootAndNestedDirectory()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        using var reader = DiskImageReader.Open(fix.VhdPath);

        var root = reader.ListChildren("").Select(e => e.Name).ToList();
        Assert.IsTrue(HasName(root, "readme.txt"));
        Assert.IsTrue(HasName(root, "docs"));

        var docs = reader.ListChildren("docs").Select(e => e.Name).ToList();
        Assert.IsTrue(HasName(docs, "guide.txt"));
    }

    [TestMethod]
    public void Vhd_StatEntry_DistinguishesFileDirAndMissing()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        using var reader = DiskImageReader.Open(fix.VhdPath);

        Assert.IsTrue(reader.StatEntry("docs") is { IsDirectory: true });
        Assert.IsTrue(reader.StatEntry("readme.txt") is { IsDirectory: false });
        Assert.IsNull(reader.StatEntry("does-not-exist.txt"));
    }

    [TestMethod]
    public void Vhd_OpenFile_StreamsContents()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        using var reader = DiskImageReader.Open(fix.VhdPath);

        var name = reader.ListChildren("")
            .First(e => !e.IsDirectory && e.Name.Contains("readme", StringComparison.OrdinalIgnoreCase))
            .Name;
        using var s = reader.OpenFile(name);
        using var sr = new StreamReader(s);
        StringAssert.Contains(sr.ReadToEnd(), "hello from the vhd");
    }

    [TestMethod]
    public void Iso_Describe_And_ListRoot()
    {
        using var fix = new DiskSampleFactory.Fixtures();
        using var reader = DiskImageReader.Open(fix.IsoPath);

        var info = reader.Describe();
        Assert.AreEqual("ISO 9660", info.Format);
        Assert.AreEqual(1, info.Volumes.Count);

        var root = reader.ListChildren("").Select(e => e.Name).ToList();
        Assert.IsTrue(HasName(root, "readme.txt"));
        Assert.IsTrue(HasName(root, "notes.txt"));
    }

    [TestMethod]
    public void CanRead_MatchesSupportedExtensions()
    {
        Assert.IsTrue(DiskImageReader.CanRead("x.vhdx"));
        Assert.IsTrue(DiskImageReader.CanRead("x.iso"));
        Assert.IsFalse(DiskImageReader.CanRead("x.txt"));
    }

    private static bool HasName(IEnumerable<string> names, string name) =>
        names.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
}
