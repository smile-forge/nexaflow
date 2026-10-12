using System.Buffers.Binary;

using System.Text;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// A <c>\pict</c> group as it is read: what kind of picture it is, how big its writer asked for it to be, and its bytes —
/// written in hex, or raw after a <c>\bin</c>.
/// </summary>
internal sealed class RtfPicture
{
    private readonly List<byte> _bytes = [];
    private int _high = -1;

    private string? _type;
    private int _mapping;
    private int _bitsPerPixel = 1;
    private int _rowBytes;
    private int? _width, _height, _goalWidth, _goalHeight;
    private int _scaleX = 100, _scaleY = 100;

    /// <summary>What a control word inside the picture says about it.</summary>
    public void Said(string word, int? n)
    {
        switch (word)
        {
            case "pngblip": _type = "image/png"; break;
            case "jpegblip": _type = "image/jpeg"; break;
            case "emfblip": _type = "image/emf"; break;
            case "wmetafile": _type = "image/wmf"; _mapping = n ?? 8; break;
            case "dibitmap": _type = "image/bmp"; break;
            case "wbitmap": _type = Ddb; break;
            case "wbmbitspixel": _bitsPerPixel = n ?? 1; break;
            case "wbmwidthbytes": _rowBytes = n ?? 0; break;
            case "picw": _width = n; break;
            case "pich": _height = n; break;
            case "picwgoal": _goalWidth = n; break;
            case "pichgoal": _goalHeight = n; break;
            case "picscalex": _scaleX = n ?? 100; break;
            case "picscaley": _scaleY = n ?? 100; break;
        }
    }

    /// <summary>A stretch of the picture's bytes in hex, the space and line breaks between them read past.</summary>
    public void Hex(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };

            if (digit < 0) continue;

            if (_high < 0) _high = digit;
            else
            {
                _bytes.Add((byte)((_high << 4) | digit));
                _high = -1;
            }
        }
    }

    /// <summary>A stretch of the picture's bytes, raw — one character to a byte, as the source holds them.</summary>
    public void Bytes(ReadOnlySpan<char> raw)
    {
        foreach (var c in raw) _bytes.Add((byte)c);
    }

    /// <summary>
    /// The picture, ready to decode — or null for one whose kind nothing on Windows reads (a QuickDraw or OS/2 picture) or
    /// that has no bytes. Sizes are in <paramref name="twipsPerEm"/>, the ordinary text size.
    /// </summary>
    public EmbeddedPicture? Built(double twipsPerEm)
    {
        if (_type is null || _bytes.Count == 0) return null;

        var bytes = _bytes.ToArray();
        var type = _type;
        PictureMetafile? metafile = null;

        switch (_type)
        {
            case "image/bmp":
                bytes = Bitmap(bytes);
                break;

            case Ddb:
                if (Device(bytes) is not { } device) return null;
                (type, bytes) = ("image/bmp", device);
                break;

            case "image/wmf":
                metafile = new PictureMetafile(_mapping, _width ?? 0, _height ?? 0);
                break;
        }

        var (wide, tall) = Size();

        return new EmbeddedPicture(type, bytes, wide / twipsPerEm, tall / twipsPerEm) { Metafile = metafile };
    }

    /// <summary>A device-dependent bitmap, which is what <c>\wbitmap</c> marks before it is made into one.</summary>
    private const string Ddb = "ddb";

    /// <summary>
    /// How big the writer asked for it to be, in twips: the size they set it to, scaled as they scaled it — or, for a
    /// metafile with no size set, its own extent; null for a bitmap drawn at its own size.
    /// </summary>
    private (double? Wide, double? Tall) Size()
    {
        double? wide = _goalWidth is > 0 ? _goalWidth.Value : null;
        double? tall = _goalHeight is > 0 ? _goalHeight.Value : null;

        // A metafile's own extent is in hundredths of a millimetre.
        if (_type is "image/wmf" or "image/emf")
        {
            wide ??= _width is > 0 ? _width.Value * 1440.0 / 2540 : null;
            tall ??= _height is > 0 ? _height.Value * 1440.0 / 2540 : null;
        }

        return (wide * _scaleX / 100, tall * _scaleY / 100);
    }

    /// <summary>A packed bitmap (<c>\dibitmap</c>) with the file header a reader of BMP files wants in front of it.</summary>
    private static byte[] Bitmap(byte[] packed)
    {
        if (packed.Length < 16) return packed;

        var header = BinaryPrimitives.ReadInt32LittleEndian(packed);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(packed.AsSpan(14));
        var compression = packed.Length >= 20 ? BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(16)) : 0;
        var used = packed.Length >= 36 ? BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(32)) : 0;

        var masks = header == 40 && compression == 3 ? 12 : 0;
        var colours = used > 0 ? used : bits <= 8 ? 1 << bits : 0;

        return Filed(packed, 14 + header + masks + (colours * 4));
    }

    /// <summary>
    /// A device-dependent bitmap (<c>\wbitmap</c>) made into a packed one: its rows are top first, and a one-bit one is
    /// black and white. Null for a depth with no fixed colours to give it.
    /// </summary>
    private byte[]? Device(byte[] bits)
    {
        if (_width is not > 0 || _height is not > 0) return null;
        if (_bitsPerPixel is not (1 or 24 or 32)) return null;

        var width = _width.Value;
        var height = _height.Value;
        var stride = _rowBytes > 0 ? _rowBytes : ((width * _bitsPerPixel) + 15) / 16 * 2;

        // A packed bitmap's rows are padded to four bytes, where a device's are padded to two.
        var padded = ((width * _bitsPerPixel) + 31) / 32 * 4;
        var palette = _bitsPerPixel == 1 ? 8 : 0;
        var packed = new byte[40 + palette + (padded * height)];

        BinaryPrimitives.WriteInt32LittleEndian(packed, 40);
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(8), -height);
        BinaryPrimitives.WriteUInt16LittleEndian(packed.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(packed.AsSpan(14), (ushort)_bitsPerPixel);

        if (palette > 0) BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(44), 0x00FFFFFF);

        for (var row = 0; row < height; row++)
        {
            var from = row * stride;
            if (from >= bits.Length) break;

            var length = Math.Min(Math.Min(stride, padded), bits.Length - from);
            bits.AsSpan(from, length).CopyTo(packed.AsSpan(40 + palette + (row * padded)));
        }

        return Filed(packed, 14 + 40 + palette);
    }

    /// <summary>A packed bitmap behind a BMP file header saying where its bits start.</summary>
    private static byte[] Filed(byte[] packed, int bitsAt)
    {
        var file = new byte[14 + packed.Length];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), bitsAt);
        packed.CopyTo(file, 14);

        return file;
    }
}

/// <summary>Reading pictures, as their groups close.</summary>
internal sealed partial class RtfReading
{
    /// <summary>
    /// The picture <paramref name="group"/> opened has closed: everything written in it, from its opening brace to its
    /// closing one, becomes one picture in the paragraph — drawn as what its bytes decode to, and as nothing where they
    /// decode to nothing.
    /// </summary>
    private void Pictured(RtfGroup group, RtfPicture picture)
    {
        // A paragraph ending inside a picture is not something a writer produces; what is left of it stays machinery.
        if (group.Line != _lines || group.Begins < 0 || group.Begins >= _line.Count) return;

        var inside = new List<ContentNode>(_line.Count - group.Begins + 1);
        for (var at = group.Begins; at < _line.Count; at++) inside.Add(_line[at].Node);

        if (picture.Built(TwipsPerEm) is { } built)
        {
            inside.Add(ContentNode.Holding(MarkdownKinds.Image, Roles.Derived, built));
            if (group.Shaped) _shapePictured = true;
        }

        _line.RemoveRange(group.Begins, _line.Count - group.Begins);
        _line.Add(new Inline(ContentNode.Branch(MarkdownKinds.Image, inside, Roles.Element, Begins(inside)),
                             Drawn: true, Look: null, group.Link));
    }

    // ── Shapes ──────────────────────────────────────────────────────────────

    /// <summary>The name of the shape property being read, which says what its value is.</summary>
    private readonly StringBuilder _property = new();

    /// <summary>Whether the shape being read has drawn its picture, so the words in it are that picture's again.</summary>
    private bool _shapePictured;

    /// <summary>Whether the shape property being read is the picture the shape shows (<c>pib</c>), rather than its fill or its outline's.</summary>
    private bool Showing() => _property.ToString().Trim().Equals("pib", StringComparison.Ordinal);
}
