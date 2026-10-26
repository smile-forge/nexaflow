namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// What a location can do. The UI reads these before it offers an action rather than discovering a gap from a
/// failure: an action whose need is missing is hidden, as <see cref="VirtualBacking"/> hides actions for archive
/// entries. <see cref="AtomicRename"/> and <see cref="Watch"/> are optional because object stores have neither.
/// </summary>
[System.Flags]
public enum LocationCapabilities
{
    None = 0,

    /// <summary>A read may start anywhere: <see cref="ReadIntent.Random"/> gets a seekable stream that fetches
    /// blocks on demand, so a 4 GB log opens at its first page.</summary>
    RandomRead = 1 << 0,

    /// <summary>A rename replaces its target in one step, so a write lands whole or not at all.</summary>
    AtomicRename = 1 << 1,

    /// <summary>Changes are pushed: <see cref="ILocationSession.Watch"/> returns a watch rather than null.</summary>
    Watch = 1 << 2,

    /// <summary>Entries may be symbolic links, reported through <see cref="LocationEntry.SymlinkTarget"/>.</summary>
    Symlinks = 1 << 3,

    /// <summary>Entries carry POSIX mode, owner and group.</summary>
    Permissions = 1 << 4,

    /// <summary>A copy between two paths of one session runs on the server, without the bytes crossing the link.</summary>
    ServerSideCopy = 1 << 5,

    /// <summary>Names that differ only by case are different names, so comparisons are ordinal.</summary>
    CaseSensitive = 1 << 6,

    /// <summary><see cref="ILocationSession.Shell"/> opens a shell on the same host.</summary>
    Shell = 1 << 7,
}

/// <summary>What a reader is about to do with a stream, so a provider can choose how to fetch the bytes.</summary>
public enum ReadIntent
{
    /// <summary>Front to back, once — a parser, a copy.</summary>
    Sequential,

    /// <summary>Seeking about — a PDF, a hex view, a tail of a log. A provider with ranged reads fetches blocks on
    /// demand; one without spools the file first.</summary>
    Random,

    /// <summary>All of it, at once — a small document read into memory.</summary>
    Whole,
}

/// <summary>How <see cref="ILocationSession.BeginWriteAsync"/> treats a file that is already there.</summary>
public enum WriteMode
{
    /// <summary>Fails with <see cref="LocationError.Conflict"/> when the path already exists.</summary>
    CreateNew,

    /// <summary>Creates the file, or replaces its whole content — atomically where the location can rename.</summary>
    Replace,

    /// <summary>Creates the file, or adds to its end. Never atomic: an aborted append keeps what reached the file.</summary>
    Append,
}
