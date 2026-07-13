using System;
using System.IO;
using System.Text;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Iso9660;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using DiscUtils.Vhd;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>
/// Builds tiny, <b>real</b> disk-image fixtures with DiscUtils so the reader/handler/view-model tests
/// exercise genuine images (a partitioned FAT VHD and a Joliet ISO), not mocks. Lives in Tests.Features
/// rather than the dependency-free Tests.Fixtures library, which must not take a DiscUtils reference.
/// </summary>
internal static class DiskSampleFactory
{
    static DiskSampleFactory()
    {
        DiscUtils.Containers.SetupHelper.SetupContainers();
        DiscUtils.FileSystems.SetupHelper.SetupFileSystems();
    }

    /// <summary>A dynamic FAT VHD (MBR, one partition) with a root file and a nested <c>docs/</c> file.</summary>
    public static string CreateFatVhd(string dir, string name = "sample.vhd")
    {
        var path = Path.Combine(dir, name);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var disk = Disk.InitializeDynamic(stream, Ownership.None, 32 * 1024 * 1024);
        BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat);
        using var fs = FatFileSystem.FormatPartition(disk, 0, "TESTVOL");
        fs.CreateDirectory("docs");
        WriteFile(fs, "readme.txt", "hello from the vhd");
        WriteFile(fs, @"docs\guide.txt", "nested file body");
        return path;
    }

    /// <summary>A Joliet ISO with two root files.</summary>
    public static string CreateIso(string dir, string name = "sample.iso")
    {
        var path = Path.Combine(dir, name);
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "TESTISO" };
        builder.AddFile("readme.txt", Encoding.UTF8.GetBytes("hello from the iso"));
        builder.AddFile("notes.txt", Encoding.UTF8.GetBytes("second iso file"));
        builder.Build(path);
        return path;
    }

    private static void WriteFile(DiscFileSystem fs, string path, string body)
    {
        using var f = fs.OpenFile(path, FileMode.Create, FileAccess.ReadWrite);
        var bytes = Encoding.UTF8.GetBytes(body);
        f.Write(bytes, 0, bytes.Length);
    }

    /// <summary>A self-cleaning temp directory holding freshly-built VHD + ISO fixtures.</summary>
    public sealed class Fixtures : IDisposable
    {
        public string Dir { get; }
        public string VhdPath { get; }
        public string IsoPath { get; }

        public Fixtures()
        {
            Dir = Path.Combine(Path.GetTempPath(), "nexa-vdisk-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            VhdPath = CreateFatVhd(Dir);
            IsoPath = CreateIso(Dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
