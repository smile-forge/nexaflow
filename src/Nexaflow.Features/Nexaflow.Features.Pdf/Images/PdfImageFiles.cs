using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Rendering;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Images;

/// <summary>A picture file of one of a document's images: its bytes, its extension, and when it last changed, local time.</summary>
internal sealed record PdfImageFile(byte[] Bytes, string Extension, DateTime Modified);

/// <summary>
/// A document's images as picture files: each named for its object, read as the document now draws it — the picture put in
/// its place, the JPEG the document holds as it is, or the image decoded to a PNG with its transparency — and replaced by an
/// edit of the document, which undo takes back as any other.
/// <para>
/// An image is one of the document's while a page of it draws it. Image masks and images written inline are not files.
/// </para>
/// </summary>
internal sealed partial class PdfImageFiles : IDisposable
{
    /// <summary>How many bytes of files read from the document are kept, so a listing and the reads after it decode each once.</summary>
    private const long KeptBytes = 64L * 1024 * 1024;

    private readonly PdfDocumentSession _session;
    private readonly Func<PdfPageSource?> _pages;
    private readonly Func<Action, Task> _onUi;
    private readonly DateTime _opened = DateTime.Now;
    private readonly LinkedList<(PdfImageKey Key, PdfImageFile File)> _kept = new();
    private long _keptBytes;

    /// <param name="session">The document whose images these are.</param>
    /// <param name="pages">Its pages, as the reader reads them — what finds the images and decodes them.</param>
    /// <param name="onUi">Runs an edit of the document on the thread its edits are made on.</param>
    public PdfImageFiles(PdfDocumentSession session, Func<PdfPageSource?> pages, Func<Action, Task> onUi)
    {
        _session = session;
        _pages = pages;
        _onUi = onUi;
        Base = session.Base.Id;
        _session.Changed += OnArrangementChanged;
    }

    /// <summary>The source named without a prefix: the document's own file, as it was when these files were made.</summary>
    public PdfSourceId Base { get; }

    /// <summary>A picture was put in place of an image, or taken out again by undo. Raised on the thread that edited.</summary>
    public event Action? ImagesChanged;

    /// <summary>The document's images are no longer followed.</summary>
    public void Dispose() => _session.Changed -= OnArrangementChanged;

    private void OnArrangementChanged(PdfPageArrangement previous, PdfPageArrangement next)
    {
        if (!ReferenceEquals(previous.Images, next.Images)
            && (previous.Images.Count != next.Images.Count
                || previous.Images.Any(entry => !next.Images.TryGetValue(entry.Key, out var now) || !ReferenceEquals(now, entry.Value))))
            ImagesChanged?.Invoke();
    }

    /// <summary>
    /// What the file of <paramref name="image"/> is called: <c>image-12.png</c> for object 12 of the document's own file,
    /// a generation other than 0 after it (<c>image-12-1</c>), and the source before it for a PDF inserted (<c>s3-image-12</c>).
    /// </summary>
    public string NameOf(PdfImageKey image, string extension)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"image-{image.Image.ObjectNumber}");
        if (image.Image.Generation != 0) name += string.Create(CultureInfo.InvariantCulture, $"-{image.Image.Generation}");
        if (image.Source != Base) name = string.Create(CultureInfo.InvariantCulture, $"s{image.Source.Value}-") + name;
        return name + extension;
    }

    /// <summary>The image a file named <paramref name="name"/> is, whatever its extension; false for a name no image has.</summary>
    public bool TryParse(string name, out PdfImageKey image)
    {
        image = default;
        var match = NamePattern().Match(Path.GetFileNameWithoutExtension(name));
        if (!match.Success
            || !long.TryParse(match.Groups["object"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return false;

        var generation = match.Groups["generation"].Success
            ? int.Parse(match.Groups["generation"].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : 0;
        var source = match.Groups["source"].Success
            ? new PdfSourceId(int.Parse(match.Groups["source"].Value, NumberStyles.None, CultureInfo.InvariantCulture))
            : Base;
        image = new PdfImageKey(source, new IndirectReference(number, generation));
        return true;
    }

    [GeneratedRegex(@"^(?:s(?<source>\d{1,9})-)?image-(?<object>\d{1,18})(?:-(?<generation>\d{1,5}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>Every image the document's pages draw, once each, in the order the pages first draw them.</summary>
    public async Task<IReadOnlyList<PdfImageKey>> AllAsync(CancellationToken ct)
    {
        if (_pages() is not { } pages) return [];

        var all = new List<PdfImageKey>();
        var seen = new HashSet<PdfImageKey>();
        for (var index = 0; index < pages.PageCount; index++)
            foreach (var image in await pages.ImagesAsync(index, ct).ConfigureAwait(false))
                if (seen.Add(image.Key)) all.Add(image.Key);
        return all;
    }

    /// <summary>The file of <paramref name="image"/> as the document now draws it; null when no page of it draws the image.</summary>
    public async Task<PdfImageFile?> FileAsync(PdfImageKey image, CancellationToken ct)
    {
        if (_session.Arrangement.Images.TryGetValue(image, out var replaced))
            return new PdfImageFile(replaced.Bytes, replaced.Extension, replaced.Made);
        if (_pages() is not { } pages || await FirstDrawingAsync(pages, image, ct).ConfigureAwait(false) is not { } index) return null;
        if (Kept(image) is { } kept) return kept;

        var jpeg = await _session.Source(image.Source).ReadAsync(scope => PlainJpeg(scope.Document, image.Image), ct).ConfigureAwait(false);
        PdfImageFile? file = null;
        if (jpeg is not null)
        {
            file = new PdfImageFile(jpeg, ".jpg", _opened);
        }
        else if (await pages.ImagePictureAsync(index, image, ct).ConfigureAwait(false) is { } picture)
        {
            file = new PdfImageFile(await Task.Run(() => Png(picture), ct).ConfigureAwait(false), ".png", _opened);
        }

        if (file is not null) Keep(image, file);
        return file;
    }

    /// <summary>
    /// <paramref name="picture"/>, a JPEG or PNG file, put in the place of <paramref name="image"/> — an edit of the document.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes are no JPEG or PNG file this machine can decode.</exception>
    /// <exception cref="InvalidOperationException">The image's document is no longer one this document is made from.</exception>
    public async Task ReplaceAsync(PdfImageKey image, byte[] picture, CancellationToken ct)
    {
        var replacement = PdfImageReplacement.Of(picture)
                          ?? throw new InvalidDataException("Only a JPEG or PNG picture can be put in the place of a document's image.");
        // Decoded here, off the thread the edit is made on, so the pages drawing it next find it decoded.
        if (await Task.Run(() => replacement.Picture, ct).ConfigureAwait(false) is null)
            throw new InvalidDataException("The picture cannot be decoded.");

        await _onUi(() => _session.ReplaceImage(image, replacement)).ConfigureAwait(false);
    }

    /// <summary>The first page that draws <paramref name="image"/>, reading the pages in order until one does; null for none.</summary>
    private static async Task<int?> FirstDrawingAsync(PdfPageSource pages, PdfImageKey image, CancellationToken ct)
    {
        for (var index = 0; index < pages.PageCount; index++)
            if ((await pages.ImagesAsync(index, ct).ConfigureAwait(false)).Any(drawn => drawn.Key == image)) return index;
        return null;
    }

    /// <summary>
    /// The JPEG file the document holds for <paramref name="image"/>, when it is one a picture file can be as it is: encoded
    /// with DCT alone, in grey or RGB, with no mask or decode array of its own. Null for any other image.
    /// </summary>
    private static byte[]? PlainJpeg(PdfDocument document, IndirectReference image)
    {
        if (document.Structure.GetObject(image)?.Data is not StreamToken stream) return null;

        var dictionary = stream.StreamDictionary;
        var dct = dictionary.TryGet(NameToken.Filter, out var filter)
                  && (filter is NameToken name && name.Equals(NameToken.DctDecode)
                      || filter is ArrayToken { Length: 1 } filters && filters.Data[0] is NameToken only && only.Equals(NameToken.DctDecode));
        if (!dct || dictionary.ContainsKey(NameToken.Smask) || dictionary.ContainsKey(NameToken.Mask) || dictionary.ContainsKey(NameToken.Decode))
            return null;
        if (!dictionary.TryGet(NameToken.ColorSpace, out var space)
            || space is not NameToken colours
            || !(colours.Equals(NameToken.Devicergb) || colours.Equals(NameToken.Devicegray)))
            return null;

        var bytes = stream.Data.ToArray();
        return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF ? bytes : null;
    }

    /// <summary><paramref name="picture"/> written as a PNG, its transparency kept.</summary>
    private static byte[] Png(BitmapSource picture)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private PdfImageFile? Kept(PdfImageKey image)
    {
        lock (_kept)
        {
            for (var node = _kept.First; node is not null; node = node.Next)
            {
                if (node.Value.Key != image) continue;
                _kept.Remove(node);
                _kept.AddFirst(node);
                return node.Value.File;
            }
        }

        return null;
    }

    private void Keep(PdfImageKey image, PdfImageFile file)
    {
        lock (_kept)
        {
            if (_kept.Any(entry => entry.Key == image)) return;
            _kept.AddFirst((image, file));
            _keptBytes += file.Bytes.Length;
            while (_keptBytes > KeptBytes && _kept.Count > 1)
            {
                _keptBytes -= _kept.Last!.Value.File.Bytes.Length;
                _kept.RemoveLast();
            }
        }
    }
}
