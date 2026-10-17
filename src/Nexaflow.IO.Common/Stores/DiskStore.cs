namespace Nexaflow.IO.Common.Stores;

/// <summary>
/// A store over this PC's file system: its roots are whatever folders or volumes it is given — a USB stick's mounted
/// volumes, the folder a copy lands in — and an id is a full path.
/// </summary>
public sealed class DiskStore : IFileStore
{
    const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;
    const int BufferSize = 1 << 20;

    readonly Func<CancellationToken, Task<IReadOnlyList<StoreRoot>>> _roots;

    /// <param name="roots">Read each time the roots are asked for, so a volume mounted or gone shows.</param>
    public DiskStore(string name, Func<CancellationToken, Task<IReadOnlyList<StoreRoot>>> roots)
    {
        Name = name;
        _roots = roots;
    }

    /// <summary>A store whose one root is <paramref name="folder"/>: where a copy to this PC lands, or what is copied from it.</summary>
    public static DiskStore Over(string folder) =>
        new(Path.GetFileName(Trimmed(folder)) is { Length: > 0 } leaf ? leaf : folder,
            _ => Task.FromResult<IReadOnlyList<StoreRoot>>([new StoreRoot(folder, folder, null, null, null, IsReadOnly: false)]));

    public string Name { get; }

    public IReadOnlyDictionary<string, string> Facts { get; } = new Dictionary<string, string>();

    /// <summary>A disk tells no one when it changes; a browser reads it again when asked.</summary>
    public event EventHandler? Changed { add { } remove { } }

    public Task<IReadOnlyList<StoreRoot>> RootsAsync(CancellationToken ct) => _roots(ct);

    /// <summary>The entry for a file or folder on this PC — what a copy from it starts with.</summary>
    public static StoreEntry EntryOf(string path) =>
        Directory.Exists(path) ? Entry(new DirectoryInfo(path)) : Entry(new FileInfo(path));

    public Task<IReadOnlyList<StoreEntry>> ListAsync(string folderId, CancellationToken ct) =>
        Task.Run<IReadOnlyList<StoreEntry>>(() =>
            [.. new DirectoryInfo(folderId).EnumerateFileSystemInfos()
                .Where(i => (i.Attributes & HiddenSystem) != HiddenSystem)
                .Select(Entry)], ct);

    public Task<Stream> OpenReadAsync(string fileId, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(fileId, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan));

    public async Task<StoreEntry> WriteAsync(string folderId, string name, long length, DateTime? modified, Stream content, CancellationToken ct)
    {
        string path = Path.Combine(folderId, name);
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
        {
            file.SetLength(length);
            await content.CopyToAsync(file, BufferSize, ct);
            await file.FlushAsync(ct);
            file.Flush(flushToDisk: true);
        }
        if (modified is { } when) File.SetLastWriteTime(path, when);
        return Entry(new FileInfo(path));
    }

    public Task<StoreEntry> CreateFolderAsync(string folderId, string name, CancellationToken ct) =>
        Task.Run(() => Entry(Directory.CreateDirectory(Path.Combine(folderId, name))), ct);

    public Task<StoreEntry> RenameAsync(StoreEntry entry, string name, CancellationToken ct) => Task.Run(() =>
    {
        string renamed = Path.Combine(Path.GetDirectoryName(Trimmed(entry.Id))!, name);
        if (entry.IsFolder)
        {
            Directory.Move(entry.Id, renamed);
            return Entry(new DirectoryInfo(renamed));
        }
        File.Move(entry.Id, renamed);
        return Entry(new FileInfo(renamed));
    }, ct);

    public Task DeleteAsync(StoreEntry entry, CancellationToken ct) => Task.Run(() =>
    {
        if (entry.IsFolder)
        {
            foreach (var file in Directory.EnumerateFiles(entry.Id, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(entry.Id, recursive: true);
        }
        else
        {
            File.SetAttributes(entry.Id, FileAttributes.Normal);
            File.Delete(entry.Id);
        }
    }, ct);

    public string? LocalPathOf(string id) => id;

    public void Dispose() { }

    static StoreEntry Entry(FileSystemInfo info) =>
        new(info.FullName, info.Name, info is DirectoryInfo, info is FileInfo f ? f.Length : 0, info.LastWriteTime);

    static string Trimmed(string path) => path.Length > 3 ? path.TrimEnd('\\') : path;
}
