using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// What reading one document's pages shares between them: glyph outlines, decoded images and pattern cells are read
/// once however many pages use them, and the layers the document hides stay hidden on every page.
/// </summary>
internal sealed class PdfReadingCaches
{
    /// <summary>
    /// How many bytes of decoded pixels are kept for reuse. An image a document repeats — a letterhead, a logo — is
    /// decoded once while it stays among the most recently used; a photo book does not keep every page it has shown.
    /// </summary>
    private const long PictureBudget = 256L * 1024 * 1024;

    private readonly Dictionary<IndirectReference, LinkedListNode<(IndirectReference Key, PictureEntry Entry)>> _pictures = [];
    private readonly LinkedList<(IndirectReference Key, PictureEntry Entry)> _recentPictures = new();
    private long _pictureBytes;
    private readonly ConditionalWeakTable<StreamToken, PdfGroupNode> _cells = new();

    /// <summary>Glyph outlines, by font and code.</summary>
    public PdfGlyphOutlines Glyphs { get; } = new();

    /// <summary>Which layers show — read from the catalog once the document is open, before any page is read.</summary>
    public PdfOptionalContent OptionalContent { get; set; } = PdfOptionalContent.None;

    /// <summary>
    /// What cancels the page being read. Pages of one document are read one at a time, under the lock that guards the
    /// document, and the reader sets this for each before it starts.
    /// </summary>
    public System.Threading.CancellationToken Cancellation { get; set; }

    /// <summary>
    /// The picture the image XObject at <paramref name="reference"/> decodes to, decoding it with <paramref name="decode"/>
    /// when it is not among those kept; a failure is kept too, so an undecodable logo on every page is tried once. An image
    /// with no reference of its own (a direct object) is decoded each time.
    /// </summary>
    public PdfPicture? Picture(IndirectReference? reference, System.Func<(PdfPicture? Picture, string? Failure)> decode, out string? failure)
    {
        if (reference is not { } key)
        {
            var (made, why) = decode();
            failure = why;
            return made;
        }

        lock (_pictures)
        {
            if (_pictures.TryGetValue(key, out var known))
            {
                _recentPictures.Remove(known);
                _recentPictures.AddFirst(known);
                failure = known.Value.Entry.Failure;
                return known.Value.Entry.Picture;
            }
        }

        var (picture, reason) = decode();
        var entry = new PictureEntry(picture, reason);
        lock (_pictures)
        {
            if (!_pictures.ContainsKey(key))
            {
                _pictures[key] = _recentPictures.AddFirst((key, entry));
                _pictureBytes += entry.Bytes;
                while (_pictureBytes > PictureBudget && _recentPictures.Count > 1)
                {
                    var oldest = _recentPictures.Last!;
                    _recentPictures.RemoveLast();
                    _pictures.Remove(oldest.Value.Key);
                    _pictureBytes -= oldest.Value.Entry.Bytes;
                }
            }
        }

        failure = reason;
        return picture;
    }

    /// <summary>The cell of the coloured tiling pattern <paramref name="stream"/>, read with <paramref name="read"/> once.</summary>
    public PdfGroupNode Cell(StreamToken stream, System.Func<PdfGroupNode> read) =>
        _cells.GetValue(stream, _ => read());

    private sealed record PictureEntry(PdfPicture? Picture, string? Failure)
    {
        /// <summary>What the decoded pixels hold in memory.</summary>
        public long Bytes { get; } = Picture is { Bitmap: var b }
            ? (long)b.PixelWidth * b.PixelHeight * Math.Max(1, b.Format.BitsPerPixel) / 8
            : 0;
    }
}
