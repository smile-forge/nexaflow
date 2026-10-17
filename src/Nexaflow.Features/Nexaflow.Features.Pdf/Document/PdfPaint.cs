using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// What an area is painted with: one colour, a shading, or a tile repeated across it. The alpha a PDF graphics
/// state carries (<c>ca</c> / <c>CA</c>) is folded in here, so a drawable needs no separate opacity.
/// </summary>
internal abstract record PdfPaint
{
    /// <summary>How opaque the paint is, 0..1, from the graphics state's constant alpha.</summary>
    public abstract double Alpha { get; }
}

/// <summary>One colour, already converted out of whatever colour space the document named it in.</summary>
internal sealed record PdfSolidPaint(Color Colour) : PdfPaint
{
    public override double Alpha => Colour.A / 255.0;

    /// <summary>Opaque black, the colour every graphics state starts with.</summary>
    public static PdfSolidPaint Black { get; } = new(Colors.Black);
}

/// <summary>A smooth shading used as a paint (a shading pattern).</summary>
/// <param name="Shading">The shading itself, in its own space.</param>
/// <param name="ToPage">From the shading's space to page space.</param>
/// <param name="Alpha">The graphics state's constant alpha when the paint was set.</param>
internal sealed record PdfShadingPaint(PdfShading Shading, Matrix ToPage, double Alpha) : PdfPaint
{
    public override double Alpha { get; } = Alpha;
}

/// <summary>A tiling pattern: one cell's drawing repeated on a lattice.</summary>
/// <param name="Cell">What one cell draws, in pattern space.</param>
/// <param name="Bounds">The cell's bounding box in pattern space — what is kept of each repetition.</param>
/// <param name="Step">How far apart repetitions are, in pattern space.</param>
/// <param name="ToPage">From pattern space to page space.</param>
/// <param name="Alpha">The graphics state's constant alpha when the paint was set.</param>
internal sealed record PdfTilingPaint(PdfGroupNode Cell, Rect Bounds, Vector Step, Matrix ToPage, double Alpha) : PdfPaint
{
    public override double Alpha { get; } = Alpha;
}

/// <summary>How a line is stroked: the parts of a PDF graphics state that shape the stroke.</summary>
/// <param name="Width">Line width in the space the path is drawn in. Zero means the thinnest line the device can draw.</param>
/// <param name="Cap">How open ends and dashes finish.</param>
/// <param name="Join">How corners meet.</param>
/// <param name="MiterLimit">The ratio past which a mitered corner is bevelled instead.</param>
/// <param name="Dashes">Dash and gap lengths, in the same space as <paramref name="Width"/>; empty for a solid line.</param>
/// <param name="DashPhase">How far into the dash pattern the line starts.</param>
internal sealed record PdfPen(
    double Width, PenLineCap Cap, PenLineJoin Join, double MiterLimit, IReadOnlyList<double> Dashes, double DashPhase)
{
    /// <summary>A solid line one unit wide, mitered — every graphics state's starting pen.</summary>
    public static PdfPen Default { get; } = new(1, PenLineCap.Flat, PenLineJoin.Miter, 10, [], 0);
}

/// <summary>Which of a smooth shading's kinds this is, by the PDF <c>ShadingType</c> it was read from.</summary>
internal enum PdfShadingKind
{
    /// <summary>Type 2: colour varies along a line between two points.</summary>
    Axial,
    /// <summary>Type 3: colour varies between two circles.</summary>
    Radial,
}

/// <summary>A smooth shading, sampled into colour stops so it no longer needs the document to be drawn.</summary>
/// <param name="Kind">Axial or radial.</param>
/// <param name="Coordinates">
/// Axial: <c>x0 y0 x1 y1</c>. Radial: <c>x0 y0 r0 x1 y1 r1</c>. In the shading's own space.
/// </param>
/// <param name="Stops">Colours along the shading, at offsets 0..1 across its domain.</param>
/// <param name="ExtendStart">Whether the first colour carries on before the start.</param>
/// <param name="ExtendEnd">Whether the last colour carries on past the end.</param>
/// <param name="Background">What fills outside the shading's extent, when the shading names one.</param>
/// <param name="Bounds">The shading's own bounding box, when it names one, in its own space.</param>
internal sealed record PdfShading(
    PdfShadingKind Kind, IReadOnlyList<double> Coordinates, IReadOnlyList<PdfColourStop> Stops,
    bool ExtendStart, bool ExtendEnd, Color? Background, Rect? Bounds);

/// <summary>One colour along a shading.</summary>
/// <param name="Offset">Where, 0 at the shading's start and 1 at its end.</param>
/// <param name="Colour">The colour there.</param>
internal readonly record struct PdfColourStop(double Offset, Color Colour);

/// <summary>Which of a soft mask's two kinds this is.</summary>
internal enum PdfMaskKind
{
    /// <summary>The mask is the group's alpha.</summary>
    Alpha,
    /// <summary>The mask is the group's luminosity, painted over <see cref="PdfSoftMask.Backdrop"/>.</summary>
    Luminosity,
}

/// <summary>A soft mask: a drawing whose alpha or brightness says how much of what it masks shows through.</summary>
/// <param name="Kind">Whether the mask is read from alpha or luminosity.</param>
/// <param name="Group">The mask's drawing, in page space.</param>
/// <param name="Backdrop">For a luminosity mask, what the drawing is painted over.</param>
internal sealed record PdfSoftMask(PdfMaskKind Kind, PdfGroupNode Group, Color Backdrop);
