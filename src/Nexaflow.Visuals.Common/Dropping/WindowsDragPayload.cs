using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Nexaflow.Interaction;

namespace Nexaflow.Visuals.Common.Dropping;

/// <summary>
/// A Windows data object — a drag's, or the clipboard's — as an <see cref="IDragPayload"/>: the one place the formats
/// other applications use are turned into <see cref="DragKinds"/>. Each is worked out once and kept, since a hover asks
/// on every move of the pointer.
/// </summary>
public sealed class WindowsDragPayload : IDragPayload
{
    private const string Descriptors = "FileGroupDescriptorW";
    private const string Png = "PNG";

    /// <summary>How Windows says whether files on the clipboard were cut or copied: a little-endian <see cref="DragDropEffects"/>.</summary>
    internal const string PreferredDropEffect = "Preferred DropEffect";

    private static readonly string[] LinkFormats = ["UniformResourceLocatorW", "UniformResourceLocator", "text/x-moz-url"];

    private readonly Dictionary<string, bool> _contains = new(StringComparer.Ordinal);
    private IDataObject? _data;
    private string[]? _files;
    private IReadOnlyList<CarriedFile>? _contents;
    private string? _link, _text;
    private bool _readLink, _readText;

    public WindowsDragPayload(IDataObject data) => _data = data;

    internal bool Wraps(IDataObject data) => ReferenceEquals(_data, data);

    /// <summary>The drag is over: Windows takes the object back, so it is neither held nor read again.</summary>
    internal void Release() => _data = null;

    public bool Contains(string kind)
    {
        if (_data is null) return false;
        if (_contains.TryGetValue(kind, out var known)) return known;

        return _contains[kind] = kind switch
        {
            DragKinds.Files        => Files().Length > 0,
            DragKinds.FilesCut     => Files().Length > 0 && Cut(),
            DragKinds.FileContents => Contents().Count > 0,
            DragKinds.Link         => Link() is not null,
            DragKinds.Text         => Present(DataFormats.UnicodeText) || Present(DataFormats.Text),
            DragKinds.Html         => Present(DataFormats.Html),
            DragKinds.Image        => Present(Png) || Present(DataFormats.Bitmap),
            _                      => Present(kind),
        };
    }

    public object? Read(string kind)
    {
        if (!Contains(kind)) return null;

        return kind switch
        {
            DragKinds.Files        => Files(),
            DragKinds.FilesCut     => true,
            DragKinds.FileContents => Contents(),
            DragKinds.Link         => Link(),
            DragKinds.Text         => Text(),
            DragKinds.Html         => Get(DataFormats.Html) as string,
            DragKinds.Image        => Picture(),
            _                      => Get(kind),
        };
    }

    private string[] Files()
        => _files ??= Present(DataFormats.FileDrop) && Get(DataFormats.FileDrop) is string[] paths ? paths : [];

    /// <summary>Whether the source said its files are to be moved where they land: Explorer's cut, and ours.</summary>
    private bool Cut()
    {
        if (!Present(PreferredDropEffect) || Get(PreferredDropEffect) is not MemoryStream effect) return false;
        var bytes = new byte[4];
        effect.Position = 0;
        return effect.Read(bytes, 0, 4) == 4 && ((DragDropEffects)BitConverter.ToInt32(bytes, 0)).HasFlag(DragDropEffects.Move);
    }

    private IReadOnlyList<CarriedFile> Contents()
    {
        if (_contents is not null) return _contents;
        if (_data is not { } data || Files().Length > 0 || !Present(Descriptors) || Get(Descriptors) is not MemoryStream table)
            return _contents = [];

        var files = FileContentsReader.Read(data, table.ToArray());

        // A link dragged out of a browser also offers itself as a .url file of its own making: that is the link.
        var onlyTheLink = files.Count == 1 && Path.GetExtension(files[0].Name).Equals(".url", StringComparison.OrdinalIgnoreCase)
                          && Link() is not null;
        return _contents = onlyTheLink ? [] : files;
    }

    private string? Link()
    {
        if (_readLink) return _link;
        _readLink = true;

        foreach (var format in LinkFormats)
            if (Present(format) && WebLinks.Decode(Get(format)) is { } address)
                return _link = address;

        return _link = WebLinks.SingleAddress(Text());
    }

    private string? Text()
    {
        if (_readText) return _text;
        _readText = true;

        return _text = Present(DataFormats.UnicodeText) ? Get(DataFormats.UnicodeText) as string
                     : Present(DataFormats.Text)        ? Get(DataFormats.Text) as string
                     : null;
    }

    private byte[]? Picture()
    {
        // As the source encoded it where it did: the bitmap format loses transparency.
        if (Present(Png) && Get(Png) is MemoryStream encoded) return encoded.ToArray();
        if (Get(DataFormats.Bitmap) is not BitmapSource bitmap) return null;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        return png.ToArray();
    }

    private bool Present(string format)
    {
        try { return _data?.GetDataPresent(format) == true; }
        catch (COMException) { return false; }
    }

    private object? Get(string format)
    {
        // A source offering a format and failing to render it is a source without it.
        try { return _data?.GetData(format); }
        catch (Exception failure) when (failure is COMException or OutOfMemoryException or NotSupportedException) { return null; }
    }
}
