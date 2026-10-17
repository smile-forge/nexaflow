namespace Nexaflow.IO.Common.Stores;

/// <summary>One file or folder in a store, as a listing gives it.</summary>
/// <param name="Id">What the store addresses it by: a path on a disk, an object id on a phone.</param>
public sealed record StoreEntry(string Id, string Name, bool IsFolder, long Size, DateTime? Modified);

/// <summary>The top of one branch of a store: a mounted volume, a phone's internal storage or its SD card.</summary>
public sealed record StoreRoot(string Id, string Name, string? FileSystem, long? Capacity, long? Free, bool IsReadOnly)
{
    public StoreEntry AsEntry() => new(Id, Name, IsFolder: true, Size: 0, Modified: null);
}

/// <summary>
/// A tree of folders and files reached through one API: a disk volume through the file system, a phone or camera
/// through MTP. What browses and copies — the shared file browser, <see cref="StoreTransfer"/> — works through this and
/// never knows which.
/// </summary>
/// <remarks>
/// Ids are the store's own and opaque to everyone else: a full path on a disk, an object id over MTP, where a name is
/// not unique and a path cannot be typed. A browser keeps the way it came (root, folder, folder) to say where it is.
/// <para>Every method may be called off the UI thread, and a store is used by one caller at a time.</para>
/// </remarks>
public interface IFileStore : IDisposable
{
    /// <summary>What the store is, for a heading: "Galaxy S21", "Mass storage".</summary>
    string Name { get; }

    /// <summary>Raised when what the store holds changes on its own — a phone unlocked and sharing its storage, a file
    /// added on the device. May be raised on any thread.</summary>
    event EventHandler? Changed;

    Task<IReadOnlyList<StoreRoot>> RootsAsync(CancellationToken ct);

    /// <summary>What a folder holds, as a person would see it: hidden system entries are left out.</summary>
    Task<IReadOnlyList<StoreEntry>> ListAsync(string folderId, CancellationToken ct);

    Task<Stream> OpenReadAsync(string fileId, CancellationToken ct);

    /// <summary>Writes a new file named <paramref name="name"/> into a folder from <paramref name="content"/>, which holds
    /// exactly <paramref name="length"/> bytes. The name is free: deciding what to call a copy is the caller's.</summary>
    Task<StoreEntry> WriteAsync(string folderId, string name, long length, DateTime? modified, Stream content, CancellationToken ct);

    Task<StoreEntry> CreateFolderAsync(string folderId, string name, CancellationToken ct);

    Task<StoreEntry> RenameAsync(StoreEntry entry, string name, CancellationToken ct);

    /// <summary>Deletes a file, or a folder and everything in it.</summary>
    Task DeleteAsync(StoreEntry entry, CancellationToken ct);

    /// <summary>
    /// The entry's path on this PC's file system, where the store is one: what lets a copy between two such stores take
    /// <see cref="FileTransferEngine"/>'s read-back past the cache, and a file open in the app's viewer where it is.
    /// Null over MTP.
    /// </summary>
    string? LocalPathOf(string id);
}
