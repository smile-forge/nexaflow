using System.Reflection;
using System.Runtime.InteropServices;

namespace Nexaflow.WslHelper.Native;

/// <summary>
/// The C library calls the helper makes, bound to whichever C library the distro has: glibc's <c>libc.so.6</c>, or musl's
/// <c>libc.musl-&lt;arch&gt;.so.1</c> — never a bare <c>libc.so</c> first, which on glibc is a linker script that cannot be
/// loaded. A call that fails leaves its errno for <see cref="Marshal.GetLastPInvokeError"/>. Every path is passed as its
/// exact bytes (<see cref="PosixPathMarshaller"/>), so a name that is no UTF-8 is the name the distro holds.
/// </summary>
internal static unsafe partial class Libc
{
    private const string Library = "libc.so.6";

    static Libc() => NativeLibrary.SetDllImportResolver(typeof(Libc).Assembly, Resolve);

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return 0;
        string musl = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "libc.musl-aarch64.so.1" : "libc.musl-x86_64.so.1";
        foreach (string candidate in (string[])[Library, musl, "libc.so"])
            if (NativeLibrary.TryLoad(candidate, out nint handle)) return handle;
        return 0;
    }

    /// <summary>A system call by its number, which differs by architecture (<see cref="LinuxAbi"/>). Every argument is a
    /// whole register, as x86-64 and arm64 both pass them; -1 with errno set on failure.</summary>
    [LibraryImport(Library, EntryPoint = "syscall", SetLastError = true)]
    public static partial nint Syscall(nint number, nint first, nint second, nint third, nint fourth, nint fifth);

    [LibraryImport(Library, EntryPoint = "getuid")]
    public static partial uint GetUid();

    /// <summary>The <c>struct passwd</c> of a uid, in storage the next call reuses, or 0 when the uid names nobody.</summary>
    [LibraryImport(Library, EntryPoint = "getpwuid")]
    public static partial nint GetPasswdByUid(uint uid);

    /// <summary>The <c>struct group</c> of a gid, in storage the next call reuses, or 0 when the gid names none.</summary>
    [LibraryImport(Library, EntryPoint = "getgrgid")]
    public static partial nint GetGroupByGid(uint gid);

    [LibraryImport(Library, EntryPoint = "rename", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PosixPathMarshaller))]
    public static partial int Rename(string from, string to);

    [LibraryImport(Library, EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PosixPathMarshaller))]
    public static partial int Link(string existing, string created);

    [LibraryImport(Library, EntryPoint = "unlink", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PosixPathMarshaller))]
    public static partial int Unlink(string path);

    [LibraryImport(Library, EntryPoint = "rmdir", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PosixPathMarshaller))]
    public static partial int RemoveDirectory(string path);

    [LibraryImport(Library, EntryPoint = "mkdir", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PosixPathMarshaller))]
    public static partial int MakeDirectory(string path, uint mode);

    [LibraryImport(Library, EntryPoint = "chmod", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PosixPathMarshaller))]
    public static partial int ChangeMode(string path, uint mode);

    /// <summary><c>lchown</c>: a link's own owner, never its target's. <see cref="uint.MaxValue"/> leaves an id as it is.</summary>
    [LibraryImport(Library, EntryPoint = "lchown", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PosixPathMarshaller))]
    public static partial int ChangeOwner(string path, uint uid, uint gid);

    [LibraryImport(Library, EntryPoint = "readlink", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PosixPathMarshaller))]
    public static partial nint ReadLink(string path, byte* buffer, nint size);

    [LibraryImport(Library, EntryPoint = "inotify_init1", SetLastError = true)]
    public static partial int InotifyInit(int flags);

    [LibraryImport(Library, EntryPoint = "inotify_add_watch", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PosixPathMarshaller))]
    public static partial int InotifyAddWatch(int descriptor, string path, uint mask);

    [LibraryImport(Library, EntryPoint = "inotify_rm_watch", SetLastError = true)]
    public static partial int InotifyRemoveWatch(int descriptor, int watch);

    [LibraryImport(Library, EntryPoint = "read", SetLastError = true)]
    public static partial nint Read(int descriptor, byte* buffer, nint count);

    [LibraryImport(Library, EntryPoint = "poll", SetLastError = true)]
    public static partial int Poll(PollDescriptor* descriptors, nuint count, int timeoutMilliseconds);

    /// <summary>Gives the open file the permission bits named.</summary>
    [LibraryImport(Library, EntryPoint = "fchmod", SetLastError = true)]
    public static partial int FChangeMode(int descriptor, uint mode);
}

/// <summary><c>struct pollfd</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PollDescriptor
{
    public int Descriptor;
    public short Events;
    public short ReturnedEvents;
}
