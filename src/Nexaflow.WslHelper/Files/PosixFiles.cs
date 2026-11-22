using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Nexaflow.IO.Location.Wsl.Protocol;
using Nexaflow.WslHelper.Native;

namespace Nexaflow.WslHelper.Files;

/// <summary>What <c>statx</c> reads of one path: its kind, its permission bits (<c>st_mode &amp; 0xFFF</c>), its size (0 for a
/// folder) and its times in UTC <see cref="DateTime.Ticks"/>.</summary>
/// <param name="CreatedTicks">Birth time, or 0 where the file system keeps none.</param>
/// <param name="HasIds">True when the owner, group, device and inode were read — always, but on a kernel without statx.</param>
internal readonly record struct PosixStat(WireKind Kind, uint Mode, long Size, long ModifiedTicks, long CreatedTicks,
                                          bool HasIds, uint Uid, uint Gid, ulong Device, ulong Inode)
{
    /// <summary>True when both name the one file.</summary>
    public bool IsSameFile(in PosixStat other) => HasIds && other.HasIds && Device == other.Device && Inode == other.Inode;
}

/// <summary>
/// The file system calls the helper makes beneath .NET: <c>statx</c> for what .NET cannot read (owner, group, birth time,
/// identity, a link as itself) and <c>renameat2</c> for a rename that never replaces. Both go through <c>syscall</c>,
/// because statx's layout is one on every architecture and neither has a wrapper in every C library. Where the kernel has
/// no statx (WSL 1, old kernels) metadata comes from .NET instead, without owner, group, birth time or identity, and
/// anything that is neither a folder nor a link reads as a file.
/// </summary>
internal static unsafe class PosixFiles
{
    private const int CurrentFolder = -100;      // AT_FDCWD
    private const int SymlinkNoFollow = 0x100;   // AT_SYMLINK_NOFOLLOW
    private const uint BasicStats = 0x7ff;       // STATX_BASIC_STATS
    private const uint BirthTime = 0x800;        // STATX_BTIME
    private const uint NoReplace = 1;            // RENAME_NOREPLACE
    private const uint TypeMask = 0xF000, DirectoryType = 0x4000, RegularType = 0x8000, LinkType = 0xA000;
    private const uint PermissionMask = 0xFFF;
    private const int StatxSize = 256;

    private static readonly long EpochTicks = DateTime.UnixEpoch.Ticks;
    private static readonly long MinSeconds = (DateTime.MinValue.Ticks - EpochTicks) / TimeSpan.TicksPerSecond;
    private static readonly long MaxSeconds = (DateTime.MaxValue.Ticks - EpochTicks) / TimeSpan.TicksPerSecond;

    private static volatile bool _statxMissing;

    /// <summary>Reads the path — a link as itself unless <paramref name="followLink"/>. Returns 0, or the errno.</summary>
    public static int Stat(string path, bool followLink, out PosixStat stat)
    {
        if (!_statxMissing)
        {
            int errno = Statx(path, followLink, out stat);
            if (errno != Errno.ENOSYS) return errno;
            _statxMissing = true;
        }
        return StatManaged(path, followLink, out stat);
    }

    /// <summary>Renames unless something is at <paramref name="to"/> (EEXIST). Returns 0, or the errno — EINVAL or ENOSYS
    /// where the kernel or the file system cannot refuse to replace.</summary>
    public static int RenameNoReplace(string from, string to)
    {
        using var source = new KernelPath(from);
        using var target = new KernelPath(to);
        fixed (byte* s = source.Bytes)
        fixed (byte* t = target.Bytes)
        {
            // errno is read before anything else runs: the runtime's own calls may set the last error in between.
            return Libc.Syscall(LinuxAbi.RenameAt2, CurrentFolder, (nint)s, CurrentFolder, (nint)t, (nint)NoReplace) < 0
                ? Marshal.GetLastPInvokeError()
                : 0;
        }
    }

    /// <summary>The text a link holds, as written in it, or null when the path is no link (any more).</summary>
    public static string? ReadLink(string path)
    {
        for (int size = 256; ;)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                nint length;
                fixed (byte* b = buffer)
                    length = Libc.ReadLink(path, b, buffer.Length);
                if (length < 0) return null;
                // A text that fills the buffer may have been cut short.
                if (length < buffer.Length) return Encoding.UTF8.GetString(buffer, 0, (int)length);
                size = buffer.Length * 2;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static int Statx(string path, bool followLink, out PosixStat stat)
    {
        byte* buffer = stackalloc byte[StatxSize];
        int errno = 0;
        using (var name = new KernelPath(path))
        {
            fixed (byte* p = name.Bytes)
            {
                // errno is read before anything else runs: the runtime's own calls may set the last error in between.
                if (Libc.Syscall(LinuxAbi.Statx, CurrentFolder, (nint)p, followLink ? 0 : SymlinkNoFollow,
                                 (nint)(BasicStats | BirthTime), (nint)buffer) < 0)
                    errno = Marshal.GetLastPInvokeError();
            }
        }
        if (errno != 0)
        {
            stat = default;
            return errno;
        }

        // struct statx: stx_mask @0, stx_uid @20, stx_gid @24, stx_mode @28, stx_ino @32, stx_size @40, stx_btime @80,
        // stx_mtime @112, stx_dev_major @136, stx_dev_minor @140.
        var statx = new ReadOnlySpan<byte>(buffer, StatxSize);
        uint answered = Field<uint>(statx, 0);
        uint mode = Field<ushort>(statx, 28);
        WireKind kind = (mode & TypeMask) switch
        {
            DirectoryType => WireKind.Directory,
            RegularType => WireKind.File,
            LinkType => WireKind.Symlink,
            _ => WireKind.Other,
        };
        stat = new PosixStat(
            kind,
            mode & PermissionMask,
            kind == WireKind.Directory ? 0 : (long)Field<ulong>(statx, 40),
            Time(statx, 112),
            (answered & BirthTime) != 0 ? Time(statx, 80) : 0,
            HasIds: true,
            Field<uint>(statx, 20),
            Field<uint>(statx, 24),
            ((ulong)Field<uint>(statx, 136) << 32) | Field<uint>(statx, 140),
            Field<ulong>(statx, 32));
        return 0;
    }

    private static T Field<T>(ReadOnlySpan<byte> statx, int offset) where T : unmanaged => MemoryMarshal.Read<T>(statx[offset..]);

    // struct statx_timestamp: i64 tv_sec, u32 tv_nsec. Clamped to what DateTime holds.
    private static long Time(ReadOnlySpan<byte> statx, int offset)
    {
        long seconds = Math.Clamp(Field<long>(statx, offset), MinSeconds, MaxSeconds);
        return EpochTicks + seconds * TimeSpan.TicksPerSecond + Field<uint>(statx, offset + 8) / 100;
    }

    // For a kernel without statx: what .NET reads.
    private static int StatManaged(string path, bool followLink, out PosixStat stat)
    {
        stat = default;
        try
        {
            var file = new FileInfo(path);
            string? target = file.LinkTarget;
            if (target is not null)
            {
                if (followLink)
                {
                    FileSystemInfo? final = file.ResolveLinkTarget(returnFinalTarget: true);
                    return final is null ? Errno.ENOENT : StatManaged(final.FullName, followLink: false, out stat);
                }
                stat = new PosixStat(WireKind.Symlink, (uint)file.UnixFileMode, Encoding.UTF8.GetByteCount(target),
                                     file.LastWriteTimeUtc.Ticks, 0, false, 0, 0, 0, 0);
                return 0;
            }
            if (Directory.Exists(path))
            {
                var folder = new DirectoryInfo(path);
                stat = new PosixStat(WireKind.Directory, (uint)folder.UnixFileMode, 0, folder.LastWriteTimeUtc.Ticks, 0, false, 0, 0, 0, 0);
                return 0;
            }
            if (file.Exists)
            {
                stat = new PosixStat(WireKind.File, (uint)file.UnixFileMode, file.Length, file.LastWriteTimeUtc.Ticks, 0, false, 0, 0, 0, 0);
                return 0;
            }
            return Errno.ENOENT;
        }
        catch (UnauthorizedAccessException)
        {
            return Errno.EACCES;
        }
        catch (IOException e) when (e.HResult > 0)
        {
            return e.HResult;
        }
    }

    /// <summary>A path as the kernel takes it — UTF-8, NUL-terminated — in a pooled buffer given back on dispose.</summary>
    private readonly struct KernelPath : IDisposable
    {
        public KernelPath(string path)
        {
            Bytes = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(path.Length) + 1);
            Bytes[Encoding.UTF8.GetBytes(path, Bytes)] = 0;
        }

        public byte[] Bytes { get; }

        public void Dispose() => ArrayPool<byte>.Shared.Return(Bytes);
    }
}
