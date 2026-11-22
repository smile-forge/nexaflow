using System.Runtime.InteropServices;

namespace Nexaflow.WslHelper.Native;

/// <summary>The numbers of the system calls the helper makes through <see cref="Libc.Syscall"/>, which differ between
/// x86-64 and arm64 — the two architectures it is published for.</summary>
internal static class LinuxAbi
{
    public static readonly nint Statx;
    public static readonly nint RenameAt2;

    static LinuxAbi() => (Statx, RenameAt2) = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => (332, 316),
        Architecture.Arm64 => (291, 276),
        var other => throw new PlatformNotSupportedException($"nexaflow-wsl-helper is built for x64 and arm64 Linux, not {other}"),
    };
}
