namespace Nexaflow.IO.Location.Wsl.Protocol;

/// <summary>
/// What the backend asks of the helper, with the payload each carries and what answers it. Paths are the distro's own,
/// absolute (<c>/home/adam</c>).
/// </summary>
internal enum WslOp : byte
{
    /// <summary>Int32 protocol version → <see cref="WslReply.HelloReply"/>, or <see cref="WslReply.Failed"/> when the
    /// helper speaks another.</summary>
    Hello = 1,

    /// <summary>String path → <see cref="WslReply.Entries"/> pages, then <see cref="WslReply.Done"/>; or
    /// <see cref="WslReply.Failed"/>. The folder's own entries, never <c>.</c> or <c>..</c>.</summary>
    List = 2,

    /// <summary>String path → <see cref="WslReply.Entry"/>, or <see cref="WslReply.Failed"/> with
    /// <see cref="WslFailure.NotFound"/>. A link is described as itself, with where it points.</summary>
    Stat = 3,

    /// <summary>String path → <see cref="WslReply.Done"/>: the folder and any missing parents; one already there is fine.</summary>
    CreateDirectory = 4,

    /// <summary>String path, bool recursive → <see cref="WslReply.Done"/>. A folder must be empty unless recursive
    /// (<see cref="WslFailure.NotEmpty"/>); a link is removed, never what it points at.</summary>
    Delete = 5,

    /// <summary>String from, string to, bool replace → <see cref="WslReply.Done"/>. One step: what is at <c>to</c> is
    /// replaced only when replace is true, and <see cref="WslFailure.Conflict"/> otherwise. A file replacing a file first takes
    /// the replaced one's permission bits, and its owner and group where the helper may set them — a save written beside the
    /// file and renamed over it keeps a script runnable.</summary>
    Rename = 6,

    /// <summary>String from, string to, bool replace → <see cref="WslReply.Done"/>: a file copied on the distro — a new copy
    /// with the source's permission bits, one over a file with that file's, as a write over it keeps them.
    /// <see cref="WslFailure.Conflict"/> when <c>to</c> exists and replace is false.</summary>
    Copy = 7,

    /// <summary>String path, bool recursive → <see cref="WslReply.Done"/>, then <see cref="WslReply.Changed"/> under this
    /// request's id for every change until <see cref="Unwatch"/>. Recursive takes in folders made under it later.</summary>
    Watch = 8,

    /// <summary>Int32 id of the <see cref="Watch"/> → <see cref="WslReply.Done"/>. An id already ended is fine.</summary>
    Unwatch = 9,

    /// <summary>Int32 id of a request still answering → no reply: it stops, and sends nothing more.</summary>
    Cancel = 10,

    /// <summary>String program, string folder, int32 count and that many string arguments, int32 count and that many string
    /// name and value pairs laid over the environment → <see cref="WslReply.Output"/> as the program writes, then
    /// <see cref="WslReply.Exited"/> and <see cref="WslReply.Done"/>; or <see cref="WslReply.Failed"/> with
    /// <see cref="WslFailure.NotFound"/> when the folder is not there or the program is not on a login shell's PATH. The
    /// program runs in the environment a login <c>sh</c> sets, with nothing on its stdin; a <see cref="Cancel"/> ends it and
    /// every process it started.</summary>
    Run = 11,

    /// <summary>String path, uint32 bits → <see cref="WslReply.Done"/>: the file given those permission bits, the twelve
    /// chmod(2) takes. A write gives the file it makes the bits it was asked for this way, on its hidden temp before the
    /// rename puts it in place.</summary>
    ChangeMode = 12,

    /// <summary>String path, int64 offset, int32 count → <see cref="WslReply.Data"/>: up to count bytes of the file from
    /// offset, at most <see cref="WslWire.MaxChunk"/>, fewer only at its end. How a file with no Windows path — one whose
    /// name is no UTF-8, which WSL's file server cannot name — is read.</summary>
    Read = 13,

    /// <summary>String path, byte <see cref="WireWrite"/>, int64 offset, bytes → <see cref="WslReply.Done"/>: the bytes
    /// written into the file, at most <see cref="WslWire.MaxChunk"/>. How a file with no Windows path is written.</summary>
    Write = 14,
}

/// <summary>How a <see cref="WslOp.Write"/> opens its file.</summary>
internal enum WireWrite : byte
{
    /// <summary>Makes the file, which must not be there (<see cref="WslFailure.Conflict"/>), and writes from its start.</summary>
    CreateNew = 0,

    /// <summary>Writes into the file there, at the offset.</summary>
    At = 1,

    /// <summary>Adds to the file's end, making it where it is not there.</summary>
    Append = 2,
}

/// <summary>What the helper answers, with the payload each carries.</summary>
internal enum WslReply : byte
{
    /// <summary>Int32 protocol version, uint32 uid the helper runs as, string home folder, string login shell.</summary>
    HelloReply = 1,

    /// <summary>Int32 count, then that many entries: one page of a listing.</summary>
    Entries = 2,

    /// <summary>One entry.</summary>
    Entry = 3,

    /// <summary>Nothing: the request is done.</summary>
    Done = 4,

    /// <summary>Byte <see cref="WslFailure"/>, string message: the request failed and is done.</summary>
    Failed = 5,

    /// <summary>String path that changed — the watched one when the helper cannot say which — under a watch's id.</summary>
    Changed = 6,

    /// <summary>Bool from stderr (false for stdout), bytes: what a <see cref="WslOp.Run"/>'s program wrote next.</summary>
    Output = 7,

    /// <summary>Int32 exit code: how a <see cref="WslOp.Run"/>'s program ended, after all it wrote.</summary>
    Exited = 8,

    /// <summary>Bytes: what a <see cref="WslOp.Read"/> read.</summary>
    Data = 9,
}

/// <summary>Why a request failed, as the backend reports it.</summary>
internal enum WslFailure : byte
{
    NotFound = 1,
    AccessDenied = 2,

    /// <summary>Something is already where the request would put something.</summary>
    Conflict = 3,

    /// <summary>A folder still holds something.</summary>
    NotEmpty = 4,

    /// <summary>Any other failure; the message says what.</summary>
    Other = 5,
}

/// <summary>What an entry is, as <c>lstat</c> reads it.</summary>
internal enum WireKind : byte
{
    File = 0,
    Directory = 1,
    Symlink = 2,

    /// <summary>A device, a pipe, a socket.</summary>
    Other = 3,
}

/// <summary>One file or folder as the helper reads it: <c>lstat</c>, and for a link, where it points and whether that
/// is a folder.</summary>
/// <param name="Name">As the distro holds it.</param>
/// <param name="Size">In bytes; 0 for a folder.</param>
/// <param name="ModifiedTicks">Last write, UTC, in <see cref="DateTime.Ticks"/>.</param>
internal sealed record WireEntry(string Name, WireKind Kind, long Size, long ModifiedTicks)
{
    /// <summary>Birth time, UTC ticks, or 0 where the file system keeps none.</summary>
    public long CreatedTicks { get; init; }

    /// <summary>The permission bits, set-id and sticky bits included (<c>st_mode &amp; 0xFFF</c>).</summary>
    public uint Mode { get; init; }

    /// <summary>True when <see cref="Uid"/> and <see cref="Gid"/> were read.</summary>
    public bool HasOwner { get; init; }

    public uint Uid { get; init; }

    public uint Gid { get; init; }

    /// <summary>The owner's name, or null where the uid names nobody.</summary>
    public string? Owner { get; init; }

    /// <summary>The group's name, or null where the gid names none.</summary>
    public string? Group { get; init; }

    /// <summary>Where a link points, as written in it; null for anything else.</summary>
    public string? LinkTarget { get; init; }

    /// <summary>True when a link points at a folder, so it opens as one.</summary>
    public bool LinkToDirectory { get; init; }
}
