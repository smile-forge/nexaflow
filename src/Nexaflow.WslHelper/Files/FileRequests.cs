using System.Buffers;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using Nexaflow.IO.Location.Wsl.Protocol;
using Nexaflow.WslHelper.Native;

namespace Nexaflow.WslHelper.Files;

/// <summary>
/// The requests on files and folders — listing, describing, making, deleting, renaming and copying — each made against the
/// distro's own file system, with POSIX meaning: a link is handled as itself, never as what it points at.
/// </summary>
internal sealed class FileRequests(EntryReader entries, ReplyChannel replies)
{
    // rwxrwxrwx, which the umask narrows, as mkdir(1) makes a folder.
    private const uint AnyoneMayOpen = 0x1FF;

    // rw-rw-rw-, which the umask narrows, as a program makes a file.
    private const uint AnyoneMayWrite = 0x1B6;

    private const int CopyBuffer = 128 * 1024;

    /// <summary>Sends the folder's entries in pages of <see cref="WslWire.PageSize"/>, then Done.</summary>
    public async Task ListAsync(int id, string path, CancellationToken ct)
    {
        if (Require(path, followLink: true).Kind != WireKind.Directory)
            throw new HelperFailure(WslFailure.NotFound, $"{path} is not a folder");

        var page = new List<WireEntry>(WslWire.PageSize);
        foreach (var (name, kind) in Children(path))
        {
            ct.ThrowIfCancellationRequested();
            WireEntry? entry = entries.Read(DistroPath.Join(path, name), name, out int errno);
            if (entry is null)
            {
                // Gone since the folder was read.
                if (errno is Errno.ENOENT or Errno.ENOTDIR) continue;
                // One that cannot be read — in a folder that may be read but not searched — is named with its kind alone.
                entry = new WireEntry(name, kind, 0, 0);
            }
            page.Add(entry);
            if (page.Count == WslWire.PageSize)
            {
                await SendPageAsync(id, page, ct).ConfigureAwait(false);
                page.Clear();
            }
        }
        if (page.Count > 0) await SendPageAsync(id, page, ct).ConfigureAwait(false);
        await replies.SendAsync(new WslWriter(WslReply.Done, id), ct).ConfigureAwait(false);
    }

    /// <summary>Sends the entry describing the path itself.</summary>
    public async Task StatAsync(int id, string path, CancellationToken ct)
    {
        WireEntry entry = entries.Read(path, DistroPath.NameOf(path), out int errno) ?? throw HelperFailure.FromErrno(errno, path);
        await replies.SendAsync(new WslWriter(WslReply.Entry, id).Entry(entry), ct).ConfigureAwait(false);
    }

    /// <summary>Makes the folder and any missing parents; one already there is fine, a file in the way is a conflict.</summary>
    public static void CreateDirectory(string path)
    {
        // The missing folders, the deepest at the bottom, up to the first one there.
        var missing = new Stack<string>();
        for (string at = path; ; at = DistroPath.ParentOf(at))
        {
            int errno = PosixFiles.Stat(at, followLink: true, out var stat);
            if (errno == 0)
            {
                if (stat.Kind != WireKind.Directory) throw new HelperFailure(WslFailure.Conflict, $"{at}: a file is in the way");
                break;
            }
            // ENOTDIR: something further up is not a folder, which the walk up reaches.
            if (errno is not (Errno.ENOENT or Errno.ENOTDIR)) throw HelperFailure.FromErrno(errno, at);
            missing.Push(at);
        }

        while (missing.TryPop(out string? folder))
        {
            if (Libc.MakeDirectory(folder, AnyoneMayOpen) == 0) continue;
            int errno = Marshal.GetLastPInvokeError();
            if (errno != Errno.EEXIST) throw HelperFailure.FromErrno(errno, folder);
            // Made meanwhile by someone else — fine when it is a folder; a dangling link or a file is in the way.
            if (PosixFiles.Stat(folder, followLink: true, out var made) != 0 || made.Kind != WireKind.Directory)
                throw new HelperFailure(WslFailure.Conflict, $"{folder}: a file is in the way");
        }
    }

    /// <summary>Removes a file or a link (never what it points at), or a folder — with everything in it when
    /// <paramref name="recursive"/>, else only when empty.</summary>
    public static void Delete(string path, bool recursive)
    {
        if (Require(path, followLink: false).Kind != WireKind.Directory)
        {
            if (Libc.Unlink(path) != 0) throw HelperFailure.FromErrno(Marshal.GetLastPInvokeError(), path);
            return;
        }
        if (recursive)
        {
            DeleteTree(path);
            return;
        }
        if (Libc.RemoveDirectory(path) == 0) return;
        int errno = Marshal.GetLastPInvokeError();
        // POSIX lets rmdir answer EEXIST for a folder that is not empty.
        throw errno is Errno.ENOTEMPTY or Errno.EEXIST
            ? new HelperFailure(WslFailure.NotEmpty, $"{path} is not empty")
            : HelperFailure.FromErrno(errno, path);
    }

    // Everything in the folder, then the folder: a link inside it is removed as a link, never descended through.
    private static void DeleteTree(string folder)
    {
        foreach (var (name, kind) in Children(folder))
        {
            string child = DistroPath.Join(folder, name);
            if (kind == WireKind.Directory) DeleteTree(child);
            else if (Libc.Unlink(child) != 0 && Marshal.GetLastPInvokeError() is var errno && errno != Errno.ENOENT)
                throw HelperFailure.FromErrno(errno, child);
        }
        if (Libc.RemoveDirectory(folder) != 0) throw HelperFailure.FromErrno(Marshal.GetLastPInvokeError(), folder);
    }

    /// <summary>Renames in one step: what is at <paramref name="to"/> is replaced only when <paramref name="replace"/>, and
    /// a conflict otherwise.</summary>
    public static void Rename(string from, string to, bool replace)
    {
        if (replace)
        {
            CarryOverReplaced(from, to);
            if (Libc.Rename(from, to) != 0) throw ReplaceFailure(Marshal.GetLastPInvokeError(), from, to);
            return;
        }

        int errno = PosixFiles.RenameNoReplace(from, to);
        if (errno == 0) return;
        if (errno is not (Errno.EINVAL or Errno.ENOSYS)) throw HelperFailure.FromErrno(errno, errno == Errno.EEXIST ? to : from);

        // The kernel or the file system cannot refuse to replace (WSL 1, drvfs, older kernels). Anything but a folder is
        // linked at its new name, which fails rather than replaces, then unlinked from its old one.
        if (Require(from, followLink: false).Kind != WireKind.Directory)
        {
            if (Libc.Link(from, to) == 0)
            {
                if (Libc.Unlink(from) == 0) return;
                errno = Marshal.GetLastPInvokeError();
                Libc.Unlink(to);
                throw HelperFailure.FromErrno(errno, from);
            }
            errno = Marshal.GetLastPInvokeError();
            if (errno is Errno.EEXIST) throw new HelperFailure(WslFailure.Conflict, $"{to} already exists");
            if (errno is Errno.ENOENT) throw HelperFailure.FromErrno(errno, from);
        }

        // What cannot be linked — a folder, or a file on a file system without hard links — is looked for, then renamed.
        if (PosixFiles.Stat(to, followLink: false, out _) == 0) throw new HelperFailure(WslFailure.Conflict, $"{to} already exists");
        if (Libc.Rename(from, to) != 0) throw ReplaceFailure(Marshal.GetLastPInvokeError(), from, to);
    }

    /// <summary>Copies a file on the distro, its permission bits kept.</summary>
    public static void Copy(string from, string to, bool replace)
    {
        PosixStat source = Require(from, followLink: true);
        if (source.Kind != WireKind.File)
        {
            throw new HelperFailure(WslFailure.Other, source.Kind == WireKind.Directory
                ? $"{from} is a folder; only files copy"
                : $"{from} is not a regular file; only files copy");
        }
        bool replacing = PosixFiles.Stat(to, followLink: true, out var target) == 0;
        if (replacing && target.IsSameFile(source))
            throw new HelperFailure(WslFailure.Conflict, $"{to} is {from} itself");

        using SafeFileHandle input  = Open(from, PosixFiles.ReadOnly, 0);
        using SafeFileHandle output = Open(to, PosixFiles.WriteOnly | PosixFiles.Create | (replace ? PosixFiles.Truncate : PosixFiles.Exclusive),
                                           source.Mode);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBuffer);
        try
        {
            for (long at = 0; ;)
            {
                int read = RandomAccess.Read(input, buffer, at);
                if (read == 0) break;
                RandomAccess.Write(output, buffer.AsSpan(0, read), at);
                at += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        // A new copy is the source's, bits and all; one over a file keeps that file's, as a write over it does.
        uint bits = replacing && target.Kind == WireKind.File ? target.Mode : source.Mode;
        if (Libc.FChangeMode((int)output.DangerousGetHandle(), bits) != 0)
            throw HelperFailure.FromErrno(Marshal.GetLastPInvokeError(), to);
    }

    /// <summary>Up to <paramref name="count"/> bytes of the file from <paramref name="offset"/> — fewer only at its end — sent
    /// as one <see cref="WslReply.Data"/>.</summary>
    public async Task ReadAsync(int id, string path, long offset, int count, CancellationToken ct)
    {
        count = Math.Clamp(count, 0, WslWire.MaxChunk);
        using SafeFileHandle file = Open(path, PosixFiles.ReadOnly, 0);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(count, 1));
        try
        {
            int read = 0;
            while (read < count)
            {
                int got = RandomAccess.Read(file, buffer.AsSpan(read, count - read), offset + read);
                if (got == 0) break;
                read += got;
            }
            await replies.SendAsync(new WslWriter(WslReply.Data, id).Bytes(buffer.AsSpan(0, read)), ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Writes <paramref name="data"/> into the file as <paramref name="how"/> says; a file it makes is made as open(2)
    /// makes one, which the umask narrows.</summary>
    public static void Write(string path, WireWrite how, long offset, ReadOnlySpan<byte> data)
    {
        int flags = how switch
        {
            WireWrite.CreateNew => PosixFiles.WriteOnly | PosixFiles.Create | PosixFiles.Exclusive,
            WireWrite.Append    => PosixFiles.WriteOnly | PosixFiles.Create | PosixFiles.Append,
            _                   => PosixFiles.WriteOnly,
        };
        using SafeFileHandle file = Open(path, flags, AnyoneMayWrite);
        // Opened to append, the file takes the bytes at its end whatever offset is given.
        RandomAccess.Write(file, data, how == WireWrite.Append ? 0 : offset);
    }

    // A file opened as open(2) opens it, or the failure that stopped it — a file already there being a conflict.
    private static SafeFileHandle Open(string path, int flags, uint mode)
    {
        int errno = PosixFiles.Open(path, flags, mode, out var file);
        return errno == 0 ? file! : throw HelperFailure.FromErrno(errno, path);
    }

    /// <summary>Gives the file the permission bits named — the twelve chmod(2) takes — following a link, as chmod does.</summary>
    public static void ChangeMode(string path, uint bits)
    {
        if (Libc.ChangeMode(path, bits & 0xFFF) != 0) throw HelperFailure.FromErrno(Marshal.GetLastPInvokeError(), path);
    }

    // A file replacing a file first takes the replaced one's permission bits, and its owner and group where this user may
    // set them: a save written beside the file and renamed over it keeps a script runnable and a shared file shared. The
    // owner goes first, because chown clears the set-id bits chmod then puts back.
    private static void CarryOverReplaced(string from, string to)
    {
        if (PosixFiles.Stat(to, followLink: false, out var replaced) != 0 || replaced.Kind != WireKind.File) return;
        PosixStat source = Require(from, followLink: false);
        if (source.Kind != WireKind.File || source.IsSameFile(replaced)) return;

        bool reowned = false;
        if (replaced.HasIds && (replaced.Uid != source.Uid || replaced.Gid != source.Gid))
        {
            reowned = TryChangeOwner(from, replaced.Uid, replaced.Gid)
                      // Only root gives a file away; its owner may still hand it to a group they belong to.
                      || (replaced.Gid != source.Gid && TryChangeOwner(from, uint.MaxValue, replaced.Gid));
        }
        if ((reowned || replaced.Mode != source.Mode) && Libc.ChangeMode(from, replaced.Mode) != 0)
            throw HelperFailure.FromErrno(Marshal.GetLastPInvokeError(), from);
    }

    // False when this user may not; any other failure is the rename's.
    private static bool TryChangeOwner(string path, uint uid, uint gid)
    {
        if (Libc.ChangeOwner(path, uid, gid) == 0) return true;
        int errno = Marshal.GetLastPInvokeError();
        return errno == Errno.EPERM ? false : throw HelperFailure.FromErrno(errno, path);
    }

    // rename(2) over a folder that still holds something answers ENOTEMPTY, or EEXIST as POSIX allows.
    private static HelperFailure ReplaceFailure(int errno, string from, string to) => errno switch
    {
        Errno.ENOTEMPTY or Errno.EEXIST => new HelperFailure(WslFailure.NotEmpty, $"{to} is a folder that is not empty"),
        Errno.ENOTDIR => new HelperFailure(WslFailure.Conflict, $"{from} is a folder and {to} is not"),
        Errno.EXDEV => new HelperFailure(WslFailure.Other, $"{to} is on another file system than {from}"),
        _ => HelperFailure.FromErrno(errno, from),
    };

    private static PosixStat Require(string path, bool followLink)
    {
        int errno = PosixFiles.Stat(path, followLink, out var stat);
        return errno == 0 ? stat : throw HelperFailure.FromErrno(errno, path);
    }

    // Each entry's name, by its exact bytes, and its kind as the folder itself records it — what is left to say of one that
    // cannot be read.
    private static List<(string Name, WireKind Kind)> Children(string folder)
    {
        var children = new List<(string Name, WireKind Kind)>();
        int errno = PosixFiles.ReadFolder(folder, children);
        return errno == 0 ? children : throw HelperFailure.FromErrno(errno, folder);
    }

    private Task SendPageAsync(int id, List<WireEntry> page, CancellationToken ct)
    {
        var reply = new WslWriter(WslReply.Entries, id).Int32(page.Count);
        foreach (var entry in page) reply.Entry(entry);
        return replies.SendAsync(reply, ct);
    }
}
