using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>The installed typeface a run of a PDF font's text is set in for a reader to select — one per family.</summary>
internal static class PdfTypefaces
{
    private static readonly ConcurrentDictionary<string, Typeface> Faces = new();

    /// <summary>The face standing in for <paramref name="fontName"/>, by the same rules glyph outlines stand in by.</summary>
    public static Typeface For(string fontName) =>
        Faces.GetOrAdd(PdfGlyphOutlines.Family(fontName), static family =>
            new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal));
}
