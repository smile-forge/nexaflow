namespace Nexaflow.IO.Hdf5;

/// <summary>
/// What a link in the file leads to. <see cref="UnresolvedLink"/> is a soft or external link whose target is
/// missing; <see cref="Unreadable"/> is an object the reader found but could not decode.
/// </summary>
public enum Hdf5ObjectKind { Group, Dataset, NamedDatatype, UnresolvedLink, Unreadable }

/// <summary>How a dataset's elements are stored in the file.</summary>
public enum Hdf5Layout { Compact, Contiguous, Chunked, Virtual }

/// <summary>
/// A dataset's element type, shape and storage. <see cref="ChunkShape"/> is empty unless the layout is
/// chunked; <see cref="FillValue"/> is the formatted value unwritten elements read as, or null when the file
/// defines none.
/// </summary>
public sealed record Hdf5DatasetInfo(
    Hdf5Type Type, Hdf5Dataspace Space, Hdf5Layout Layout, IReadOnlyList<ulong> ChunkShape, string? FillValue);

/// <summary>
/// One object in the file, addressed by its absolute <see cref="Path"/> (<c>/</c> for the root group).
/// <see cref="Dataset"/> is set for datasets; <see cref="Problem"/> says why an unresolved or unreadable
/// object could not be followed.
/// </summary>
public sealed record Hdf5Object(string Path, string Name, Hdf5ObjectKind Kind)
{
    public Hdf5DatasetInfo? Dataset { get; init; }
    public string? Problem { get; init; }
}

/// <summary>
/// One attribute and its formatted value. <see cref="Value"/> holds at most the first
/// <see cref="Hdf5Attribute.PreviewElements"/> elements, with <see cref="IsTruncated"/> set when there were more;
/// <see cref="Problem"/> replaces the value when it could not be read.
/// </summary>
public sealed record Hdf5Attribute(string Name, Hdf5Type? Type, Hdf5Dataspace? Space, string Value, bool IsTruncated)
{
    public const int PreviewElements = 64;

    public string? Problem { get; init; }
}

/// <summary>
/// A page of a group's members. <see cref="HasMore"/> means members exist past this page;
/// <see cref="Problem"/> means listing stopped early because a member could not be decoded — the items read
/// before it are still here.
/// </summary>
public sealed record Hdf5Listing(IReadOnlyList<Hdf5Object> Items, bool HasMore, string? Problem);
