using System.IO;

namespace Nexaflow.Features.Font.Decoding;

/// <summary>
/// Decodes a wrapped/compressed web-font container into a raw sfnt (TrueType/OpenType) byte stream
/// that WPF's <see cref="System.Windows.Media.GlyphTypeface"/> / <c>Fonts.GetFontFamilies</c> can load.
/// The <see cref="FontSourceResolver"/> holds an ordered list of these, so a new container format
/// (e.g. WOFF2) is a drop-in: implement this and add it to the resolver's decoder list — nothing else
/// changes.
/// </summary>
public interface IFontFormatDecoder
{
    /// <summary>True when this decoder handles <paramref name="extension"/> (lower-case, incl. dot, e.g. ".woff").</summary>
    bool CanDecode(string extension);

    /// <summary>Reads the container from <paramref name="input"/> and returns raw sfnt bytes.</summary>
    byte[] DecodeToSfnt(Stream input);
}
