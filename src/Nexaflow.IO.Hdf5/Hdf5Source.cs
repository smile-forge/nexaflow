using Nexaflow.IO.Hdf5.PureHdf;
using PureHDF;

namespace Nexaflow.IO.Hdf5;

/// <summary>Opens HDF5 files for reading.</summary>
public static class Hdf5Source
{
    /// <summary>
    /// Opens a file on disk. It is shared for read, write and delete, so an open viewer never stops another
    /// program from saving, renaming or removing the file.
    /// </summary>
    public static IHdf5Source Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
        return Open(stream, leaveOpen: false);
    }

    /// <summary>Opens a seekable stream — a file inside a container reaches here through the VFS.</summary>
    public static IHdf5Source Open(Stream stream, bool leaveOpen = false)
    {
        if (!stream.CanSeek) throw new ArgumentException("HDF5 is read by offset; the stream must be seekable.", nameof(stream));
        try
        {
            return new PureHdfSource(H5File.Open(stream, leaveOpen));
        }
        catch (Exception ex)
        {
            if (!leaveOpen) stream.Dispose();
            throw new Hdf5Exception(ex.Message, ex);
        }
    }
}
