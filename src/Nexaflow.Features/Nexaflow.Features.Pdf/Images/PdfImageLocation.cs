using System.IO;
using System.Runtime.CompilerServices;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Features.Pdf.Images;

/// <summary>
/// A document's images as a location of the runtime's file system, for as long as its tab is open: a folder of picture
/// files, one for each image its pages draw, that the image editor — or anything else that reads and writes files —
/// opens and saves as any other. A save puts the picture in the image's place, an edit of the document; the file is
/// only ever replaced, never moved, deleted or added to.
/// </summary>
internal sealed class PdfImageLocation : ILocationSession, ILocationSessionSource
{
    /// <summary>
    /// What the location can do: a file is read from its bytes, held whole; a watch hears of a change to the images, and of
    /// the location withdrawn — its document saved as another file, or closed; and a write is an edit of the document, undone
    /// as its others are and kept only when it is saved.
    /// </summary>
    public const LocationCapabilities Capabilities =
        LocationCapabilities.RandomRead | LocationCapabilities.Watch | LocationCapabilities.StagedWrites;

    /// <summary>The kind of location it is, as its address names it: <c>pdf://report.pdf/image-12.png</c>.</summary>
    public const string Kind = "pdf";

    private readonly PdfImageFiles _files;

    /// <summary>The watches held on the location's paths.</summary>
    private readonly List<ImageWatch> _watches = [];

    /// <summary>The location of <paramref name="files"/>; a change to the document's images is told to every watch.</summary>
    public PdfImageLocation(PdfImageFiles files)
    {
        _files = files;
        _files.ImagesChanged += Tell;
    }

    /// <summary>The location as the file system routes it: at <c>::{id}</c>, called <paramref name="document"/>.</summary>
    public LocationMount Mount(string id, string document) => new(new ConnectionProfile(id, Kind, document, document), Capabilities, this);

    /// <summary>
    /// The location is no longer published — its document saved as another file, or closed: every watch is told, so whatever
    /// holds one of its files finds it gone, and the document's images are no longer followed.
    /// </summary>
    public void Withdrawn()
    {
        _files.ImagesChanged -= Tell;
        Tell();
    }

    /// <summary>Tells every watch its path changed — the location cannot say which image a change was to without reading it.</summary>
    private void Tell()
    {
        ImageWatch[] watches;
        lock (_watches) watches = [.. _watches];
        foreach (var watch in watches)
            watch.Raise();
    }

    public ValueTask<ILocationSession> GetSessionAsync(CancellationToken ct) => ValueTask.FromResult<ILocationSession>(this);

    public async IAsyncEnumerable<LocationEntry> EnumerateAsync(string path, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!IsRoot(path)) throw new LocationException(LocationError.NotFound, path, "A document's images are one folder.");

        foreach (var image in await _files.AllAsync(ct).ConfigureAwait(false))
            if (await _files.FileAsync(image, ct).ConfigureAwait(false) is { } file)
                yield return Entry(image, file);
    }

    public async ValueTask<LocationEntry?> GetEntryAsync(string path, CancellationToken ct)
    {
        if (IsRoot(path)) return new LocationEntry(string.Empty, IsDirectory: true, 0, DateTime.Now);
        return Image(path) is { } image && await _files.FileAsync(image, ct).ConfigureAwait(false) is { } file ? Entry(image, file) : null;
    }

    public async ValueTask<Stream> OpenReadAsync(string path, ReadIntent intent, CancellationToken ct) =>
        new MemoryStream((await Existing(path, ct).ConfigureAwait(false)).File.Bytes, writable: false);

    public async ValueTask<ILocationWrite> BeginWriteAsync(string path, WriteMode mode, LocationEntry? expected, CancellationToken ct)
    {
        if (mode == WriteMode.Append)
            throw new LocationException(LocationError.AccessDenied, path, "A document's image is replaced whole, never added to.");
        if (Image(path) is null)
            throw new LocationException(LocationError.AccessDenied, path, "A document's images are replaced, never added to.");

        var (image, file) = await Existing(path, ct).ConfigureAwait(false);
        if (mode == WriteMode.CreateNew)
            throw new LocationException(LocationError.Conflict, path, "The image is already there.");
        if (expected is not null && !expected.IsSameVersion(Entry(image, file)))
            throw new LocationException(LocationError.Conflict, path, "The image changed since it was read.");

        return new ImageWrite(_files, image, path);
    }

    public ValueTask MoveAsync(string from, string to, bool replace, CancellationToken ct) =>
        ValueTask.FromException(new LocationException(LocationError.AccessDenied, from, "A document's images are replaced, never moved."));

    public ValueTask DeleteAsync(string path, bool recursive, CancellationToken ct) =>
        ValueTask.FromException(new LocationException(LocationError.AccessDenied, path, "A document's images are replaced, never deleted."));

    public ValueTask CreateDirectoryAsync(string path, CancellationToken ct) =>
        ValueTask.FromException(new LocationException(LocationError.AccessDenied, path, "A document's images are one folder."));

    public ILocationWatch? Watch(string path, bool recursive)
    {
        var watch = new ImageWatch(this, path);
        lock (_watches) _watches.Add(watch);
        return watch;
    }

    public ILocationShell? Shell => null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private LocationEntry Entry(PdfImageKey image, PdfImageFile file) =>
        new(_files.NameOf(image, file.Extension), IsDirectory: false, file.Bytes.Length, file.Modified);

    private async Task<(PdfImageKey Image, PdfImageFile File)> Existing(string path, CancellationToken ct) =>
        Image(path) is { } image && await _files.FileAsync(image, ct).ConfigureAwait(false) is { } file
            ? (image, file)
            : throw new LocationException(LocationError.NotFound, path, "No page of the document draws that image.");

    /// <summary>The image a provider path names: a file of the one folder; null for anything else.</summary>
    private PdfImageKey? Image(string path)
    {
        var name = path.TrimStart('/');
        return name.Length > 0 && !name.Contains('/') && _files.TryParse(name, out var image) ? image : null;
    }

    private static bool IsRoot(string path) => path.Trim('/').Length == 0;

    /// <summary>A watch on a path of the location, raised with that path, until disposed.</summary>
    private sealed class ImageWatch(PdfImageLocation location, string path) : ILocationWatch
    {
        public event Action<string>? Changed;

        public void Raise() => Changed?.Invoke(path);

        public void Dispose()
        {
            lock (location._watches) location._watches.Remove(this);
        }
    }

    /// <summary>A save of an image: the picture written, then put in the image's place on commit — or nothing, if never committed.</summary>
    private sealed class ImageWrite(PdfImageFiles files, PdfImageKey image, string path) : ILocationWrite
    {
        private readonly MemoryStream _content = new();
        private bool _committed;

        public Stream Content => _content;

        public async ValueTask CommitAsync(CancellationToken ct)
        {
            if (_committed) return;
            try
            {
                await files.ReplaceAsync(image, _content.ToArray(), ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException gone)
            {
                throw new LocationException(LocationError.NotFound, path, "The image's document is no longer open as it was.", gone);
            }

            _committed = true;
        }

        public ValueTask DisposeAsync()
        {
            _content.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
