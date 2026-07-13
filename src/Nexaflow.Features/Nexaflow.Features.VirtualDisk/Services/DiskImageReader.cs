using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using Nexaflow.Features.VirtualDisk.Models;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.VirtualDisk.Services;

/// <summary>
/// Reads a virtual-disk image through DiscUtils, <b>a directory (or a file) at a time</b>. Everything here is
/// scoped and lazy: opening the image reads only the container header + partition table; a filesystem opens by
/// reading its superblock/MFT root; <see cref="ListChildren"/> reads one directory's records; <see cref="Open"/>
/// streams a file via its extents. Nothing walks the whole volume — the UI only ever needs the current view.
/// <para>
/// One instance is short-lived: it is created per VFS operation (inside a <c>DiskImageSession</c>) or per
/// inspector read, used, and disposed — so no disk handle is ever held across a tab switch. Not thread-safe;
/// callers use one instance from one thread at a time.
/// </para>
/// </summary>
public sealed class DiskImageReader : IDisposable
{
    /// <summary>Container extensions this reader can open (drives the VFS <c>CanHandle</c> and the filemap).
    /// WIM/XVA are recognised disk formats but not yet browsable here — deliberately omitted.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
        [".vhd", ".vhdx", ".vdi", ".vmdk", ".dmg", ".img", ".iso"];

    public static bool CanRead(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    // ── DiscUtils one-time provider registration ────────────────────────────────
    private static readonly object SetupLock = new();
    private static bool _setupDone;
    private static void EnsureSetup()
    {
        if (_setupDone) return;
        lock (SetupLock)
        {
            if (_setupDone) return;
            DiscUtils.Containers.SetupHelper.SetupContainers();
            DiscUtils.FileSystems.SetupHelper.SetupFileSystems();
            _setupDone = true;
        }
    }

    private sealed class Volume
    {
        public int Index;
        public VolumeInfo? Info;        // null for a pure-filesystem image (ISO) where Fs is pre-opened
        public DiscFileSystem? Fs;      // cached once opened
        public string FsName = "Unknown";
        public long Length;
    }

    private readonly Stream _stream;
    private readonly string _fileName;
    private bool _opened;
    // Fully qualified: the feature namespace 'Nexaflow.Features.VirtualDisk' otherwise shadows this type name.
    private DiscUtils.VirtualDisk? _disk;
    private string _format = "Unknown";
    private long _capacity;
    private string _partitionScheme = "None";
    private readonly List<Volume> _volumes = [];

    /// <summary>Takes ownership of <paramref name="stream"/> (disposed with this reader).</summary>
    public DiskImageReader(Stream stream, string fileName)
    {
        _stream = stream;
        _fileName = fileName;
    }

    /// <summary>Opens a reader over a real disk-image file (used by the inspector; owns the file stream).</summary>
    public static DiskImageReader Open(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), Path.GetFileName(path));

    private void EnsureOpen()
    {
        if (_opened) return;
        EnsureSetup();
        var ext = Path.GetExtension(_fileName).ToLowerInvariant();

        if (ext == ".iso")
        {
            var (fs, fsName) = OpenOpticalFilesystem(_stream);
            // Several DiscUtils filesystems (UDF, HFS+, …) throw on .Size — which is exactly what turned a
            // perfectly readable UDF disc into "unrecognised". Fall back to the image file's length.
            long size = SafeSize(fs, _stream.Length);
            _volumes.Add(new Volume { Index = 1, Fs = fs, FsName = fsName, Length = size });
            _format = fsName.Contains("UDF", StringComparison.OrdinalIgnoreCase) ? "UDF (ISO)" : "ISO 9660";
            _capacity = size;
            _partitionScheme = "None";
        }
        else
        {
            _disk = OpenDisk(ext, _stream);
            _format = FormatName(ext);
            _capacity = _disk.Capacity;
            _partitionScheme = _disk.Partitions switch
            {
                GuidPartitionTable => "GPT",
                BiosPartitionTable => "MBR",
                null               => "None",
                _                  => "Partitioned",
            };

            var mgr = new VolumeManager(_disk);
            var logical = mgr.GetLogicalVolumes();
            VolumeInfo[] infos = logical.Length > 0
                ? logical.Cast<VolumeInfo>().ToArray()
                : mgr.GetPhysicalVolumes().Cast<VolumeInfo>().ToArray();
            int i = 1;
            foreach (var info in infos)
                _volumes.Add(new Volume { Index = i++, Info = info, Length = info.Length });
        }
        _opened = true;
    }

    /// <summary>
    /// Opens the filesystem inside an <c>.iso</c>. A real ISO is often a UDF/ISO 9660 <b>bridge</b>: UDF holds
    /// the full content, ISO 9660 a small stub. We prefer UDF (DiscUtils' optical detector offers it first),
    /// but only when it <i>actually opens</i> and its root is readable — otherwise we fall back to ISO 9660, so
    /// a UDF variant DiscUtils can't parse still shows the disc rather than blanking it to "unrecognised".
    /// The stream is shared across attempts, so it's rewound before each.
    /// </summary>
    private static (DiscFileSystem Fs, string Name) OpenOpticalFilesystem(Stream stream)
    {
        IReadOnlyList<DiscUtils.FileSystemInfo> infos;   // qualified: System.IO.FileSystemInfo also in scope
        try { stream.Position = 0; infos = FileSystemManager.DetectFileSystems(stream); }
        catch { infos = []; }

        foreach (var info in infos)
        {
            try
            {
                stream.Position = 0;
                var fs = info.Open(stream);
                foreach (var _ in fs.GetFileSystemEntries("\\")) break;   // force a root read; throws if unreadable
                return (fs, info.Name);
            }
            catch { /* this filesystem can't be opened here — try the next detected one */ }
        }

        stream.Position = 0;
        return (new CDReader(stream, joliet: true), "ISO 9660");
    }

    private static DiscUtils.VirtualDisk OpenDisk(string ext, Stream s) => ext switch
    {
        ".vhd"          => new DiscUtils.Vhd.Disk(s, Ownership.None),
        ".vhdx"         => new DiscUtils.Vhdx.Disk(s, Ownership.None),
        ".vmdk"         => new DiscUtils.Vmdk.Disk(s, Ownership.None),
        ".vdi"          => new DiscUtils.Vdi.Disk(s, Ownership.None),
        ".dmg"          => new DiscUtils.Dmg.Disk(s, Ownership.None),
        ".img" or ".raw" => new DiscUtils.Raw.Disk(s, Ownership.None),
        _ => throw new NotSupportedException($"Unsupported disk format '{ext}'."),
    };

    private static string FormatName(string ext) => ext switch
    {
        ".vhd"  => "VHD",  ".vhdx" => "VHDX", ".vmdk" => "VMDK", ".vdi" => "VDI",
        ".dmg"  => "DMG",  ".img" or ".raw" => "Raw disk", _ => ext.TrimStart('.').ToUpperInvariant(),
    };

    private bool MultiVolume => _volumes.Count > 1;

    private DiscFileSystem OpenFs(Volume v)
    {
        if (v.Fs is not null) return v.Fs;
        var infos = FileSystemManager.DetectFileSystems(v.Info!);
        if (infos.Count == 0) throw new IOException($"No recognised filesystem on volume {v.Index}.");
        v.FsName = infos[0].Name;
        v.Fs = infos[0].Open(v.Info!);
        return v.Fs;
    }

    /// <summary>Splits a forward-slash inner path into (volume, filesystem-relative back-slash path).
    /// For a single-volume image the whole path is filesystem-relative; for multi-volume the leading
    /// <c>Volume N</c> segment selects the volume.</summary>
    private (Volume Vol, string Rel) Resolve(string innerPath)
    {
        EnsureOpen();
        innerPath = innerPath.Replace('\\', '/').Trim('/');
        if (!MultiVolume)
            return (_volumes[0], ToFsPath(innerPath));

        int slash = innerPath.IndexOf('/');
        var head = slash < 0 ? innerPath : innerPath[..slash];
        var rest = slash < 0 ? string.Empty : innerPath[(slash + 1)..];
        int idx = ParseVolume(head);
        var vol = _volumes.FirstOrDefault(v => v.Index == idx)
            ?? throw new FileNotFoundException($"No such volume: {head}");
        return (vol, ToFsPath(rest));
    }

    private static string ToFsPath(string p) =>
        p.Length == 0 ? "\\" : "\\" + p.Replace('/', '\\');

    private static int ParseVolume(string segment)
    {
        // "Volume 1" (browse folder name); tolerate a trailing suffix.
        const string prefix = "Volume ";
        if (segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = segment[prefix.Length..].TrimStart();
            int end = 0;
            while (end < rest.Length && char.IsDigit(rest[end])) end++;
            if (end > 0 && int.TryParse(rest[..end], out var n)) return n;
        }
        return -1;
    }

    private static string LastName(string fsPath) =>
        fsPath.Substring(fsPath.LastIndexOfAny(['\\', '/']) + 1);

    // ── Lazy browse surface (backs IArchiveSession's lazy members) ───────────────

    /// <summary>Direct children of one directory (<c>""</c> = image root). Returns an empty list rather than
    /// throwing when the filesystem can't be read, so the browser shows an empty folder, not an error.</summary>
    public IReadOnlyList<VirtualEntry> ListChildren(string dirPath)
    {
        EnsureOpen();
        dirPath = dirPath.Replace('\\', '/').Trim('/');

        if (MultiVolume && dirPath.Length == 0)
            return _volumes
                .Select(v => new VirtualEntry($"Volume {v.Index}", true, 0, 0, DateTime.Now))
                .ToList();

        try
        {
            var (vol, rel) = Resolve(dirPath);
            var fs = OpenFs(vol);
            var result = new List<VirtualEntry>();
            foreach (var d in fs.GetDirectories(rel))
                result.Add(new VirtualEntry(LastName(d), true, 0, 0, SafeTime(fs, d)));
            foreach (var f in fs.GetFiles(rel))
            {
                long len = SafeLen(fs, f);
                result.Add(new VirtualEntry(LastName(f), false, len, len, SafeTime(fs, f)));
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>Metadata for one inner path, or null if it doesn't exist / can't be read.</summary>
    public VirtualEntry? StatEntry(string entryPath)
    {
        EnsureOpen();
        entryPath = entryPath.Replace('\\', '/').Trim('/');
        if (entryPath.Length == 0) return new VirtualEntry(string.Empty, true, 0, 0, DateTime.Now);

        if (MultiVolume && !entryPath.Contains('/'))
        {
            int idx = ParseVolume(entryPath);
            return _volumes.Any(v => v.Index == idx)
                ? new VirtualEntry($"Volume {idx}", true, 0, 0, DateTime.Now)
                : null;
        }

        try
        {
            var (vol, rel) = Resolve(entryPath);
            var fs = OpenFs(vol);
            if (fs.DirectoryExists(rel)) return new VirtualEntry(LastName(rel), true, 0, 0, SafeTime(fs, rel));
            if (fs.FileExists(rel))
            {
                long len = SafeLen(fs, rel);
                return new VirtualEntry(LastName(rel), false, len, len, SafeTime(fs, rel));
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Opens one file for reading — a DiscUtils extent stream, so only the file's clusters are read.</summary>
    public Stream OpenFile(string entryPath)
    {
        var (vol, rel) = Resolve(entryPath);
        var fs = OpenFs(vol);
        if (!fs.FileExists(rel))
            throw new FileNotFoundException($"Not found in disk image: {entryPath}");
        return fs.OpenFile(rel, FileMode.Open, FileAccess.Read);
    }

    /// <summary>Rich metadata for the inspector — opens each volume's filesystem (superblock only).</summary>
    public DiskInfo Describe()
    {
        EnsureOpen();
        var vols = new List<DiskVolumeInfo>(_volumes.Count);
        foreach (var v in _volumes)
        {
            string fsName = v.FsName;
            string? label = null;
            long cap = v.Length, used = 0;
            try
            {
                var fs = OpenFs(v);
                fsName = v.FsName;
                label = fs.VolumeLabel;
                cap = fs.Size;
                used = fs.UsedSpace;
            }
            catch { /* undetectable filesystem — keep defaults */ }
            vols.Add(new DiskVolumeInfo(v.Index, fsName,
                string.IsNullOrWhiteSpace(label) ? null : label, cap, used));
        }
        return new DiskInfo(_format, _capacity, _partitionScheme, vols);
    }

    /// <summary>Every file/directory across every volume, forward-slash paths (volume-prefixed when
    /// multi-volume). Walks the whole image — used <b>only</b> for an explicit Extract-all, never for
    /// navigation. Callers still stream each file's bytes through <see cref="Open"/>.</summary>
    public IReadOnlyList<VirtualEntry> EnumerateAll()
    {
        EnsureOpen();
        var all = new List<VirtualEntry>();
        foreach (var v in _volumes)
        {
            DiscFileSystem fs;
            try { fs = OpenFs(v); } catch { continue; }
            string prefix = MultiVolume ? $"Volume {v.Index}/" : string.Empty;
            foreach (var dir in fs.GetDirectories("\\", "*", SearchOption.AllDirectories))
                all.Add(new VirtualEntry(prefix + Rel(dir), true, 0, 0, SafeTime(fs, dir)));
            foreach (var file in fs.GetFiles("\\", "*", SearchOption.AllDirectories))
            {
                long len = SafeLen(fs, file);
                all.Add(new VirtualEntry(prefix + Rel(file), false, len, len, SafeTime(fs, file)));
            }
        }
        return all;

        static string Rel(string fsPath) => fsPath.TrimStart('\\', '/').Replace('\\', '/');
    }

    private static long SafeLen(DiscFileSystem fs, string path)
    {
        try { return fs.GetFileLength(path); } catch { return 0; }
    }

    /// <summary>Filesystem size, or <paramref name="fallback"/> when the filesystem doesn't report it
    /// (UDF/HFS+/WIM/SquashFs all throw on <c>.Size</c>).</summary>
    private static long SafeSize(DiscFileSystem fs, long fallback)
    {
        try { return fs.Size; } catch { return fallback; }
    }

    private static DateTime SafeTime(DiscFileSystem fs, string path)
    {
        try { return fs.GetLastWriteTime(path); } catch { return DateTime.Now; }
    }

    public void Dispose()
    {
        foreach (var v in _volumes)
            try { v.Fs?.Dispose(); } catch { /* best effort */ }
        try { _disk?.Dispose(); } catch { /* best effort */ }
        try { _stream.Dispose(); } catch { /* best effort */ }
    }
}
