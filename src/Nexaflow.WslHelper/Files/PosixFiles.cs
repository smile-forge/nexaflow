using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Nexaflow.IO.Location.Wsl.Protocol;
using Nexaflow.WslHelper.Native;
using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Common.Locations;

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
/// The file system calls the helper makes beneath .NET, each on a path's exact bytes (<see cref="PosixNames"/>), which .NET's
/// own file APIs would decode as UTF-8 and lose: <c>statx</c> for what .NET cannot read (owner, group, birth time,
/// identity, a link as itself), <c>renameat2</c> for a rename that never replaces, <c>openat</c>, and <c>getdents64</c> to
/// read a folder's names as the bytes they are. All go through <c>syscall</c>, because neither statx nor renameat2 has a
/// wrapper in every C library. Where the kernel has no statx (WSL 1, old kernels) metadata comes from <c>newfstatat</c>,
/// without birth time.
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
    private const int StatSize = 144;            // struct stat: 144 bytes on x86-64, 128 on arm64
    private const int FolderBuffer = 32 * 1024;
    private const byte UnknownType = 0, FolderType = 4, RegularFileType = 8, LinkFileType = 10;   // d_type

    /// <summary>open(2)'s flags, as x86-64 and arm64 both number them.</summary>
    public const int ReadOnly = 0, WriteOnly = 1, Create = 0x40, Exclusive = 0x80, Truncate = 0x200, Append = 0x400;
    private const int CloseOnExec = 0x80000;

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
        return StatAt(path, followLink, out stat);
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
                if (length < buffer.Length) return PosixNames.FromBytes(buffer.AsSpan(0, (int)length));
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
        WireKind kind = KindOf(mode);
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

    // For a kernel without statx: newfstatat, whose struct stat holds all but the birth time.
    private static int StatAt(string path, bool followLink, out PosixStat stat)
    {
        byte* buffer = stackalloc byte[StatSize];
        int errno = 0;
        using (var name = new KernelPath(path))
        {
            fixed (byte* p = name.Bytes)
            {
                // errno is read before anything else runs: the runtime's own calls may set the last error in between.
                if (Libc.Syscall(LinuxAbi.NewFstatAt, CurrentFolder, (nint)p, (nint)buffer, followLink ? 0 : SymlinkNoFollow, 0) < 0)
                    errno = Marshal.GetLastPInvokeError();
            }
        }
        if (errno != 0)
        {
            stat = default;
            return errno;
        }

        // struct stat: st_dev @0, st_ino @8, st_size @48, st_mtime @88 on both; st_mode, st_uid and st_gid where LinuxAbi says.
        var raw = new ReadOnlySpan<byte>(buffer, StatSize);
        var (modeAt, uidAt, gidAt) = LinuxAbi.StatIds;
        uint mode = Field<uint>(raw, modeAt);
        WireKind kind = KindOf(mode);
        stat = new PosixStat(kind, mode & PermissionMask, kind == WireKind.Directory ? 0 : Field<long>(raw, 48), Time(raw, 88), 0,
                             HasIds: true, Field<uint>(raw, uidAt), Field<uint>(raw, gidAt), Field<ulong>(raw, 0), Field<ulong>(raw, 8));
        return 0;
    }

    private static WireKind KindOf(uint mode) => (mode & TypeMask) switch
    {
        DirectoryType => WireKind.Directory,
        RegularType => WireKind.File,
        LinkType => WireKind.Symlink,
        _ => WireKind.Other,
    };

    /// <summary>Opens the path as open(2) does with <paramref name="flags"/> (<see cref="ReadOnly"/> and the rest), making a
    /// file with <paramref name="mode"/> where it creates one. Returns 0 with the file, which the handle closes, or the errno.</summary>
    public static int Open(string path, int flags, uint mode, out SafeFileHandle? file)
    {
        file = null;
        nint descriptor;
        int errno = 0;
        using (var name = new KernelPath(path))
        {
            fixed (byte* p = name.Bytes)
            {
                descriptor = Libc.Syscall(LinuxAbi.OpenAt, CurrentFolder, (nint)p, flags | CloseOnExec, (nint)mode, 0);
                if (descriptor < 0) errno = Marshal.GetLastPInvokeError();
            }
        }
        if (errno != 0) return errno;
        file = new SafeFileHandle(descriptor, ownsHandle: true);
        return 0;
    }

    /// <summary>
    /// Adds a folder's entries to <paramref name="entries"/> — never <c>.</c> or <c>..</c> — each named by its exact bytes and
    /// of the kind the folder records, or <c>lstat</c> reads where the file system records none. Returns 0, or the errno.
    /// </summary>
    public static int ReadFolder(string folder, List<(string Name, WireKind Kind)> entries)
    {
        int errno = Open(folder, ReadOnly, 0, out var handle);
        if (errno != 0) return errno;
        using (handle)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(FolderBuffer);
            try
            {
                while (true)
                {
                    nint read;
                    fixed (byte* b = buffer)
                    {
                        read = Libc.Syscall(LinuxAbi.GetDents64, handle!.DangerousGetHandle(), (nint)b, buffer.Length, 0, 0);
                        if (read < 0) return Marshal.GetLastPInvokeError();
                    }
                    if (read == 0) return 0;

                    // struct linux_dirent64: d_ino @0, d_off @8, d_reclen @16 (u16), d_type @18, d_name @19, NUL-terminated.
                    for (int at = 0; at < read;)
                    {
                        var record = buffer.AsSpan(at, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(at + 16)));
                        at += record.Length;
                        var bytes = record[19..];
                        bytes = bytes[..bytes.IndexOf((byte)0)];
                        if (bytes is [(byte)'.'] or [(byte)'.', (byte)'.']) continue;

                        string name = PosixNames.FromBytes(bytes);
                        entries.Add((name, record[18] switch
                        {
                            FolderType => WireKind.Directory,
                            RegularFileType => WireKind.File,
                            LinkFileType => WireKind.Symlink,
                            UnknownType => Stat(DistroPath.Join(folder, name), followLink: false, out var stat) == 0 ? stat.Kind : WireKind.File,
                            _ => WireKind.Other,
                        }));
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>A path as the kernel takes it — its exact bytes (<see cref="PosixNames"/>), NUL-terminated — in a pooled
    /// buffer given back on dispose.</summary>
    private readonly struct KernelPath : IDisposable
    {
        public KernelPath(string path)
        {
            Bytes = ArrayPool<byte>.Shared.Rent(PosixNames.GetByteCount(path) + 1);
            Bytes[PosixNames.GetBytes(path, Bytes)] = 0;
        }

        public byte[] Bytes { get; }

        public void Dispose() => ArrayPool<byte>.Shared.Return(Bytes);
    }
}
