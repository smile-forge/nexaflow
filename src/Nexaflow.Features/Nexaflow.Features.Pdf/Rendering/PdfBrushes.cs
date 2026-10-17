using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// The WPF brushes a page's paints become, made once per paint and frozen. Solid colours are shared by colour; pattern
/// cells and masks are drawn once however many times they are used.
/// </summary>
/// <param name="page">The page being laid, in device-independent pixels: what a mask drawn as a picture is cut to.</param>
internal sealed class PdfBrushes(Rect page)
{
    private static readonly Matrix Scale = new(PdfPageLayout.DipsPerPoint, 0, 0, PdfPageLayout.DipsPerPoint, 0, 0);

    /// <summary>
    /// How finely a luminosity mask is sampled, in pixels to a device-independent pixel. A mask is soft by nature — a
    /// gradient fading something out — and twice the layout's resolution keeps its edges clean at the zooms a page is read at.
    /// </summary>
    private const double MaskResolution = 2;

    /// <summary>The most pixels one luminosity mask is drawn into, whatever the page's size.</summary>
    private const double MaskPixels = 8_000_000;

    private readonly Dictionary<Color, SolidColorBrush> _solids = [];
    private readonly Dictionary<PdfGroupNode, Drawing> _drawings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfSoftMask, Brush?> _masks = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<PdfPicture, Dictionary<Color, BitmapSource>> _stencils = new();

    /// <summary>What <paramref name="paint"/> paints with; null when it paints nothing drawing can give.</summary>
    public Brush? Paint(PdfPaint paint) => paint switch
    {
        PdfSolidPaint solid     => Solid(solid.Colour),
        PdfShadingPaint shading => Gradient(shading.Shading, shading.ToPage * Scale, shading.Alpha),
        PdfTilingPaint tiling   => Tiling(tiling),
        _                       => null,
    };

    private SolidColorBrush Solid(Color colour)
    {
        if (_solids.TryGetValue(colour, out var known)) return known;
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        _solids[colour] = brush;
        return brush;
    }

    /// <summary>
    /// A shading as a gradient brush placed through <paramref name="toDips"/>. Axial shadings are linear gradients
    /// exactly; radial ones are WPF's focal-point radial gradient, which is PDF's two-circle shading exactly when one
    /// circle holds the other — the shape every producer writes — with the colour stops moved out to where the inner
    /// circle's edge falls.
    /// </summary>
    public Brush? Gradient(PdfShading shading, Matrix toDips, double alpha)
    {
        GradientBrush brush;
        var c = shading.Coordinates;
        switch (shading.Kind)
        {
            case PdfShadingKind.Axial:
                brush = new LinearGradientBrush(Stops(shading.Stops, 0, 1), new Point(c[0], c[1]), new Point(c[2], c[3]))
                {
                    MappingMode = BrushMappingMode.Absolute,
                };
                break;

            case PdfShadingKind.Radial:
                var (inner, outer, reversed) = c[5] >= c[2]
                    ? ((Point: new Point(c[0], c[1]), Radius: c[2]), (Point: new Point(c[3], c[4]), Radius: c[5]), false)
                    : ((Point: new Point(c[3], c[4]), Radius: c[5]), (Point: new Point(c[0], c[1]), Radius: c[2]), true);
                if (outer.Radius <= 0) return null;

                var start = inner.Radius / outer.Radius;
                var stops = reversed ? Reverse(shading.Stops) : shading.Stops;
                brush = new RadialGradientBrush(Stops(stops, start, 1))
                {
                    MappingMode = BrushMappingMode.Absolute,
                    Center = outer.Point,
                    RadiusX = outer.Radius,
                    RadiusY = outer.Radius,
                    GradientOrigin = inner.Point,
                };
                break;

            default:
                return null;
        }

        brush.SpreadMethod = GradientSpreadMethod.Pad;
        brush.Transform = new MatrixTransform(toDips);
        brush.Opacity = alpha;
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Where a shading that does not extend past its ends paints, in device-independent pixels: the band between an
    /// axial shading's end lines, or a radial shading's outer circle less the inner one. Null when it paints everywhere —
    /// a WPF gradient pads with its end colours, which is what extending means.
    /// </summary>
    public Geometry? Reach(PdfShading shading, Matrix toDips)
    {
        if (shading.ExtendStart && shading.ExtendEnd) return null;
        if (!toDips.HasInverse) return null;
        var c = shading.Coordinates;

        // Everywhere a shading could show, said in its own space: all of the page brought back through the matrix that
        // places the shading. A length fixed in the shading's own units reaches a different distance on every page, and
        // falls short of it where those units are small — a document that measures in EMUs, 12700 to a point.
        var fromDips = toDips;
        fromDips.Invert();
        var everywhere = PdfSpace.Transform(PdfSpace.Everywhere, fromDips);
        var far = Math.Sqrt((everywhere.Width * everywhere.Width) + (everywhere.Height * everywhere.Height));

        Geometry reach;
        if (shading.Kind == PdfShadingKind.Axial)
        {
            var from = new Point(c[0], c[1]);
            var to = new Point(c[2], c[3]);
            var along = to - from;
            if (along.LengthSquared == 0) return null;

            // The band runs across the shading's axis, past the page either side; it runs past the page along the axis too
            // where an end extends.
            var across = new Vector(-along.Y, along.X);
            across.Normalize();
            across *= far;
            var unit = along;
            unit.Normalize();
            var back = shading.ExtendStart ? from - unit * far : from;
            var forth = shading.ExtendEnd ? to + unit * far : to;

            var band = new StreamGeometry();
            using (var context = band.Open())
            {
                context.BeginFigure(back + across, true, true);
                context.LineTo(forth + across, false, false);
                context.LineTo(forth - across, false, false);
                context.LineTo(back - across, false, false);
            }

            reach = band;
        }
        else
        {
            var big = c[5] >= c[2];
            var outer = big ? (Center: new Point(c[3], c[4]), Radius: c[5]) : (Center: new Point(c[0], c[1]), Radius: c[2]);
            var inner = big ? (Center: new Point(c[0], c[1]), Radius: c[2]) : (Center: new Point(c[3], c[4]), Radius: c[5]);
            var outerExtends = big ? shading.ExtendEnd : shading.ExtendStart;
            var innerExtends = big ? shading.ExtendStart : shading.ExtendEnd;

            Geometry disc = outerExtends
                ? new RectangleGeometry(everywhere)
                : new EllipseGeometry(outer.Center, outer.Radius, outer.Radius);
            reach = innerExtends || inner.Radius <= 0
                ? disc
                : new CombinedGeometry(GeometryCombineMode.Exclude, disc,
                    new EllipseGeometry(inner.Center, inner.Radius, inner.Radius));
        }

        reach.Transform = new MatrixTransform(toDips);
        reach.Freeze();
        return reach;
    }

    private static GradientStopCollection Stops(IReadOnlyList<PdfColourStop> stops, double from, double to)
    {
        var made = new GradientStopCollection(stops.Count);
        foreach (var stop in stops) made.Add(new GradientStop(stop.Colour, from + (to - from) * stop.Offset));
        made.Freeze();
        return made;
    }

    private static PdfColourStop[] Reverse(IReadOnlyList<PdfColourStop> stops)
    {
        var reversed = new PdfColourStop[stops.Count];
        for (var i = 0; i < stops.Count; i++)
            reversed[i] = new PdfColourStop(1 - stops[stops.Count - 1 - i].Offset, stops[stops.Count - 1 - i].Colour);
        return reversed;
    }

    /// <summary>
    /// A tiling pattern: its cell drawn once and tiled at the pattern's step, through the pattern's placement. The cell is
    /// laid in device-independent pixels like everything else, so its placement is taken back out of them and on to the page.
    /// </summary>
    private Brush? Tiling(PdfTilingPaint tiling)
    {
        var step = new Vector(Math.Abs(tiling.Step.X), Math.Abs(tiling.Step.Y));
        if (step.X <= 0 || step.Y <= 0) return null;

        var cell = Drawing(tiling.Cell);
        var tile = new Rect(tiling.Bounds.Left * PdfPageLayout.DipsPerPoint, tiling.Bounds.Top * PdfPageLayout.DipsPerPoint,
            step.X * PdfPageLayout.DipsPerPoint, step.Y * PdfPageLayout.DipsPerPoint);

        // What one cell keeps of its drawing is its bounding box; the tile is the step, which may be larger (gaps) or smaller
        // (overlap, which a tile cannot show — the overlap is cut).
        var kept = new DrawingGroup { ClipGeometry = new RectangleGeometry(PdfSpace.Transform(tiling.Bounds, Scale)) };
        kept.Children.Add(cell);
        kept.Freeze();

        var unscale = Scale;
        unscale.Invert();
        var brush = new DrawingBrush(kept)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = tile,
            Transform = new MatrixTransform(unscale * tiling.ToPage * Scale),
            Opacity = tiling.Alpha,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// A soft mask as an opacity mask. An alpha mask is its drawing, whose alpha is exactly what an opacity mask reads. A
    /// luminosity mask is read as brightness, which no brush does, so its drawing is painted over its backdrop into a
    /// picture whose alpha is set from each pixel's luminosity.
    /// </summary>
    public Brush? Mask(PdfSoftMask mask)
    {
        if (_masks.TryGetValue(mask, out var known)) return known;

        var drawing = Drawing(mask.Group);
        var bounds = drawing.Bounds;
        Brush? brush = null;
        if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
        {
            brush = mask.Kind == PdfMaskKind.Alpha
                ? new DrawingBrush(drawing)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = bounds,
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = bounds,
                }
                : Rect.Intersect(bounds, page) is { IsEmpty: false, Width: > 0, Height: > 0 } shown
                    ? Luminosity(drawing, shown, mask.Backdrop)
                    : null;
            brush?.Freeze();
        }

        // A mask that draws nothing is its backdrop throughout: its alpha is zero, hiding everything it masks, and its
        // luminosity the backdrop's, which a mask reads as alpha.
        brush ??= mask.Kind == PdfMaskKind.Luminosity
            ? Solid(Color.FromArgb(Luma(mask.Backdrop.R, mask.Backdrop.G, mask.Backdrop.B), 0, 0, 0))
            : Solid(Colors.Transparent);

        _masks[mask] = brush;
        return brush;
    }

    private static ImageBrush Luminosity(Drawing drawing, Rect bounds, Color backdrop)
    {
        var resolution = Math.Min(MaskResolution, Math.Sqrt(MaskPixels / (bounds.Width * bounds.Height)));
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * resolution));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * resolution));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(backdrop), null, new Rect(0, 0, width, height));
            dc.PushTransform(new ScaleTransform(resolution, resolution));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawDrawing(drawing);
            dc.Pop();
            dc.Pop();
        }

        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        rendered.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            // The alpha is all a mask reads.
            var luma = Luma(pixels[i + 2], pixels[i + 1], pixels[i]);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
            pixels[i + 3] = luma;
        }

        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        picture.Freeze();
        return new ImageBrush(picture)
        {
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = bounds,
        };
    }

    /// <summary>A colour's luminosity as a mask takes it: Rec. 601 luma, as PDF's luminosity is the colour's in its own space.</summary>
    private static byte Luma(byte red, byte green, byte blue) => (byte)((red * 299 + green * 587 + blue * 114) / 1000);

    /// <summary>An image mask's one-bit picture, its set samples in <paramref name="colour"/>.</summary>
    public BitmapSource Coloured(PdfPicture stencil, Color colour)
    {
        var byColour = _stencils.GetValue(stencil, static _ => []);
        lock (byColour)
        {
            if (byColour.TryGetValue(colour, out var known)) return known;

            var bitmap = stencil.Bitmap;
            var stride = (bitmap.PixelWidth + 7) / 8;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);

            var coloured = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Indexed1,
                new BitmapPalette([Colors.Transparent, colour]), pixels, stride);
            coloured.Freeze();
            byColour[colour] = coloured;
            return coloured;
        }
    }

    /// <summary>A group — a pattern cell, a mask — drawn once, however many times it is used.</summary>
    private Drawing Drawing(PdfGroupNode group)
    {
        if (_drawings.TryGetValue(group, out var known)) return known;
        var drawing = new PdfLaying(this).Drawing(group);
        _drawings[group] = drawing;
        return drawing;
    }
}
