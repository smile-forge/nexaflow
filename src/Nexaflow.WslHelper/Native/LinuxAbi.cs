using System.Runtime.InteropServices;

namespace Nexaflow.WslHelper.Native;

/// <summary>The numbers of the system calls the helper makes through <see cref="Libc.Syscall"/>, which differ between
/// x86-64 and arm64 — the two architectures it is published for.</summary>
internal static class LinuxAbi
{
    public static readonly nint Statx;
    public static readonly nint RenameAt2;
    public static readonly nint OpenAt;
    public static readonly nint GetDents64;
    public static readonly nint NewFstatAt;

    /// <summary>Where <c>struct stat</c> — as <see cref="NewFstatAt"/> fills it — holds <c>st_mode</c>, <c>st_uid</c> and
    /// <c>st_gid</c>, which the two architectures lay out apart; its device, inode, size and times sit alike on both.</summary>
    public static readonly (int Mode, int Uid, int Gid) StatIds;

    static LinuxAbi() => (Statx, RenameAt2, OpenAt, GetDents64, NewFstatAt, StatIds) = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => (332, 316, 257, 217, 262, (24, 28, 32)),
        Architecture.Arm64 => (291, 276, 56, 61, 79, (16, 24, 28)),
        var other => throw new PlatformNotSupportedException($"nexaflow-wsl-helper is built for x64 and arm64 Linux, not {other}"),
    };
}
