namespace Nexaflow.Features.Pdf.Document;

/// <summary>A kind of thing a document can ask for that reading or drawing it does not yet carry.</summary>
internal enum PdfUnsupportedKind
{
    /// <summary>A blend mode inside a pattern cell or a soft mask, where no page lies beneath it: drawn as Normal.</summary>
    BlendMode,
    /// <summary>A function-based shading (type 1).</summary>
    FunctionShading,
    /// <summary>A mesh shading (types 4–7).</summary>
    MeshShading,
    /// <summary>A PostScript XObject, which no conforming reader draws.</summary>
    PostScript,
    /// <summary>A glyph with no outline in its font and no system font to stand in for it.</summary>
    MissingGlyph,
    /// <summary>An image no decoder here could read.</summary>
    UndecodableImage,
    /// <summary>
    /// An uncoloured tiling pattern, whose colour comes from operands this reading does not receive: its cell is drawn in
    /// the colour the cell itself sets, or black.
    /// </summary>
    UncolouredPattern,
    /// <summary>An operator that failed on damaged content; what it would have drawn is missing, the rest is kept.</summary>
    DamagedContent,
}

/// <summary>One thing a page asked for that was not carried, and what it was.</summary>
/// <param name="Kind">The kind of thing.</param>
/// <param name="Detail">What exactly — a blend mode's name, a shading's type, an exception's message.</param>
internal readonly record struct PdfUnsupported(PdfUnsupportedKind Kind, string Detail);
