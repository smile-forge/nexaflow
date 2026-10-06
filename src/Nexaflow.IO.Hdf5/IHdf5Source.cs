namespace Nexaflow.IO.Hdf5;

/// <summary>
/// Read access to one open HDF5 file: its hierarchy, attributes and windows of dataset elements. Every member
/// is safe to call from any thread; calls on one source are served one at a time. This is the seam a backend
/// implements — an editing backend adds its own contract beside this one rather than widening it.
/// </summary>
public interface IHdf5Source : IDisposable
{
    Hdf5Object Root { get; }

    /// <summary>
    /// Up to <paramref name="take"/> members of a group after skipping <paramref name="skip"/>, in the order
    /// the file stores them. Throws <see cref="Hdf5Exception"/> when the path is not a group.
    /// </summary>
    Hdf5Listing ListChildren(string groupPath, int skip, int take, CancellationToken ct = default);

    /// <summary>The object at an absolute path, or null when no link has that path.</summary>
    Hdf5Object? Stat(string path);

    /// <summary>Every attribute of the object at a path, each read up to its preview limit.</summary>
    IReadOnlyList<Hdf5Attribute> Attributes(string path, CancellationToken ct = default);

    /// <summary>
    /// The elements of one selection of a dataset. Throws <see cref="Hdf5Exception"/> with the reader's own
    /// message when the elements cannot be decoded (an unavailable filter, an unsupported byte order).
    /// </summary>
    Hdf5Block ReadBlock(string datasetPath, Hdf5Selection selection, CancellationToken ct = default);
}

/// <summary>A file, object or read the HDF5 reader refused; the message is the reader's own.</summary>
public sealed class Hdf5Exception(string message, Exception? inner = null) : Exception(message, inner);
