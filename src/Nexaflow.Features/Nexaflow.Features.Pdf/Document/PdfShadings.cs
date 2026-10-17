using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using UglyToad.PdfPig.Functions;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>A document's smooth shadings, sampled into <see cref="PdfShading"/>s that no longer need the document.</summary>
internal static class PdfShadings
{
    /// <summary>
    /// How many colours a shading whose function is not a straight line is sampled into — enough that a stitched or
    /// sampled function's steps land within a pixel of where they belong across a full page.
    /// </summary>
    private const int Samples = 64;

    /// <summary>
    /// <paramref name="shading"/> as a <see cref="PdfShading"/>, or null with the unsupported kind it is when it is
    /// not an axial or radial shading.
    /// </summary>
    public static PdfShading? TryRead(Shading shading, out PdfUnsupported? unsupported)
    {
        unsupported = null;
        switch (shading)
        {
            case AxialShading axial when axial.Coords is { Length: >= 4 }:
                return new PdfShading(
                    PdfShadingKind.Axial, axial.Coords[..4], Stops(shading, axial.Domain, axial.Functions),
                    Extends(axial.Extend, 0), Extends(axial.Extend, 1), Background(shading), Box(shading));

            case RadialShading radial when radial.Coords is { Length: >= 6 }:
                return new PdfShading(
                    PdfShadingKind.Radial, radial.Coords[..6], Stops(shading, radial.Domain, radial.Functions),
                    Extends(radial.Extend, 0), Extends(radial.Extend, 1), Background(shading), Box(shading));

            case { ShadingType: ShadingType.FunctionBased }:
                unsupported = new PdfUnsupported(PdfUnsupportedKind.FunctionShading, "type 1");
                return null;

            default:
                unsupported = new PdfUnsupported(PdfUnsupportedKind.MeshShading, $"type {(int)shading.ShadingType}");
                return null;
        }
    }

    private static bool Extends(bool[]? extend, int index) => extend is not null && extend.Length > index && extend[index];

    private static IReadOnlyList<PdfColourStop> Stops(Shading shading, double[]? domain, PdfFunction[]? functions)
    {
        var start = domain is { Length: >= 2 } ? domain[0] : 0;
        var end   = domain is { Length: >= 2 } ? domain[1] : 1;
        var count = IsLinear(functions) ? 2 : Samples;

        var stops = new PdfColourStop[count];
        Span<double> input = stackalloc double[1];
        for (var i = 0; i < count; i++)
        {
            var offset = (double)i / (count - 1);
            input[0] = start + (end - start) * offset;
            stops[i] = new PdfColourStop(offset, Colour(shading.ColorSpace, shading.Eval(input.ToArray())));
        }

        return stops;
    }

    /// <summary>
    /// Whether the shading's colour is a straight line between its ends — one exponential function with exponent 1, the
    /// commonest shading there is — so its two ends say everything.
    /// </summary>
    private static bool IsLinear(PdfFunction[]? functions) =>
        functions is [{ FunctionType: FunctionTypes.Exponential } function] &&
        function.GetDictionary() is { } dictionary && dictionary.TryGet(Exponent, out NumericToken exponent) && exponent.Double == 1;

    private static readonly NameToken Exponent = NameToken.Create("N");

    private static Color? Background(Shading shading) =>
        shading.Background is { Length: > 0 } background ? Colour(shading.ColorSpace, background) : null;

    private static Rect? Box(Shading shading) =>
        shading.BBox is { } box ? new Rect(new Point(box.Left, box.Bottom), new Point(box.Right, box.Top)) : null;

    private static Color Colour(ColorSpaceDetails space, double[] components)
    {
        return PdfColours.ToColour(space.GetColor(components), 1);
    }
}
