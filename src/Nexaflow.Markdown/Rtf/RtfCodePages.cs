using System.Collections.Concurrent;
using System.Text;

namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// Which characters the bytes of a run of RTF text are: the code page its font's character set names, or the one the
/// document declared, and the encoding that reads it.
///
/// <para>
/// <strong>A symbol font has no code page.</strong> Its bytes name glyphs of the font, not characters, and the font's own
/// table maps them from the private-use block — so byte <c>B7</c> in Symbol is <c>U+F0B7</c>, which drawn in Symbol is the
/// bullet a writer saw.
/// </para>
/// </summary>
public static class RtfCodePages
{
    /// <summary>What a document is read as where it declares nothing: Windows' Western code page.</summary>
    public const int Western = 1252;

    /// <summary>The character set a symbol font declares (<c>\fcharset2</c>).</summary>
    public const int SymbolCharset = 2;

    /// <summary>Where a symbol font's glyphs are mapped from: byte <c>n</c> is character <c>U+F000 + n</c>.</summary>
    private const int SymbolGlyphs = 0xF000;

    private static readonly ConcurrentDictionary<int, Encoding> Encodings = new();

    static RtfCodePages()
    {
        // The code pages Windows has and .NET does not ship by default — every one an RTF document can name.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// The code page a font's <c>\fcharsetN</c> names, or null where it names none of its own: the document's default
    /// (<c>\fcharset1</c>), a symbol font, or a set this does not know.
    /// </summary>
    public static int? OfCharset(int charset) => charset switch
    {
        0 => Western,
        77 => 10000,
        78 => 10001,
        79 => 10003,
        80 => 10008,
        81 => 10002,
        83 => 10005,
        84 => 10004,
        85 => 10006,
        86 => 10081,
        87 => 10021,
        88 => 10029,
        89 => 10007,
        128 => 932,
        129 => 949,
        130 => 1361,
        134 => 936,
        136 => 950,
        161 => 1253,
        162 => 1254,
        163 => 1258,
        177 => 1255,
        178 => 1256,
        186 => 1257,
        204 => 1251,
        222 => 874,
        238 => 1250,
        254 => 437,
        255 => 850,
        _ => null,
    };

    /// <summary>
    /// The characters <paramref name="bytes"/> are in <paramref name="codePage"/>, or — for a symbol font — the glyphs they
    /// name, mapped from the private-use block. A code page this machine cannot read is taken as <see cref="Western"/>
    /// rather than losing the text.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes, int codePage, bool symbol)
    {
        if (bytes.IsEmpty) return string.Empty;

        if (!symbol) return EncodingOf(codePage).GetString(bytes);

        var glyphs = new char[bytes.Length];
        for (var at = 0; at < bytes.Length; at++) glyphs[at] = (char)(SymbolGlyphs + bytes[at]);

        return new string(glyphs);
    }

    /// <summary>The encoding a code page is read with, made once.</summary>
    public static Encoding EncodingOf(int codePage) =>
        Encodings.GetOrAdd(codePage, static page =>
        {
            try
            {
                return Encoding.GetEncoding(page);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return Encoding.GetEncoding(Western);
            }
        });
}
