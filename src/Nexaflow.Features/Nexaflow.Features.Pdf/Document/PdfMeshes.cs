using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using UglyToad.PdfPig.Functions;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Core;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// A mesh shading as Gouraud-shaded triangles in the shading's own space: each vertex placed with its colour, the colour
/// varying linearly across each triangle. A shading with a function keeps each vertex's parameter instead, looked up in
/// <see cref="Lookup"/> point by point, so its colours follow the function rather than a line between its corners.
/// </summary>
/// <param name="Points">x and y of each vertex, three vertices to a triangle.</param>
/// <param name="Values">Each vertex's colour — red, green and blue in 0..1 — or, with <paramref name="Lookup"/>, one
/// parameter in 0..1 across it.</param>
/// <param name="Lookup">The function's colours across its parameter; null where each vertex carries its colour.</param>
internal sealed record PdfMesh(float[] Points, float[] Values, Color[]? Lookup)
{
    /// <summary>How many values each vertex carries.</summary>
    public int Channels => Lookup is null ? 3 : 1;

    public int Triangles => Points.Length / 6;

    /// <summary>Where the triangles reach, in the shading's own space; empty for a mesh of none.</summary>
    public Rect Bounds { get; } = BoundsOf(Points);

    private static Rect BoundsOf(float[] points)
    {
        if (points.Length < 2) return Rect.Empty;
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        for (var i = 0; i + 1 < points.Length; i += 2)
        {
            left = Math.Min(left, points[i]);
            right = Math.Max(right, points[i]);
            top = Math.Min(top, points[i + 1]);
            bottom = Math.Max(bottom, points[i + 1]);
        }

        return new Rect(new Point(left, top), new Point(right, bottom));
    }
}

/// <summary>
/// Mesh shadings (ISO 32000-1, 8.7.4.5.5–8) read into <see cref="PdfMesh"/>es: free-form and lattice-form triangle
/// meshes (types 4 and 5) as they are given, Coons and tensor-product patch meshes (types 6 and 7) evaluated over a grid
/// of each patch fine enough that its curved edges and its colours across it read smoothly. Each vertex and each patch
/// starts on a byte boundary, as PDFium reads them.
/// </summary>
internal static class PdfMeshes
{
    /// <summary>How many steps a patch spanning the whole mesh is evaluated in, each way; smaller patches take fewer.</summary>
    private const int MostSteps = 24;

    /// <summary>The fewest steps a patch is evaluated in, each way, however small: enough for its curves to bend.</summary>
    private const int FewestSteps = 3;

    /// <summary>How many colours a function's parameter is looked up among.</summary>
    private const int LookupSize = 256;

    /// <summary>A mesh shading's triangles, or null where <paramref name="shading"/> is no mesh. Its colours are converted
    /// under <paramref name="intent"/>.</summary>
    public static PdfMesh? Read(Shading shading, RenderingIntent intent) => shading switch
    {
        FreeFormGouraudShading s => Read(new Format(s, s.BitsPerCoordinate, s.BitsPerComponent, s.BitsPerFlag, s.Decode, intent), s.Data.Span, FreeForm),
        LatticeFormGouraudShading s => Read(new Format(s, s.BitsPerCoordinate, s.BitsPerComponent, 0, s.Decode, intent), s.Data.Span,
            (ref Bits bits, Format format, Triangles into) => Lattice(ref bits, format, s.VerticesPerRow, into)),
        CoonsPatchMeshesShading s => Read(new Format(s, s.BitsPerCoordinate, s.BitsPerComponent, s.BitsPerFlag, s.Decode, intent), s.Data.Span,
            (ref Bits bits, Format format, Triangles into) => Patches(ref bits, format, tensor: false, into)),
        TensorProductPatchMeshesShading s => Read(new Format(s, s.BitsPerCoordinate, s.BitsPerComponent, s.BitsPerFlag, s.Decode, intent), s.Data.Span,
            (ref Bits bits, Format format, Triangles into) => Patches(ref bits, format, tensor: true, into)),
        _ => null,
    };

    private delegate void Reader(ref Bits bits, Format format, Triangles into);

    private static PdfMesh Read(Format format, ReadOnlySpan<byte> data, Reader read)
    {
        var bits = new Bits(data);
        var triangles = new Triangles(format.Channels);
        read(ref bits, format, triangles);
        return new PdfMesh([.. triangles.Points], [.. triangles.Values], format.Lookup);
    }

    /// <summary>Type 4: each vertex flagged as the start of a triangle or as one more on the triangle before.</summary>
    private static void FreeForm(ref Bits bits, Format format, Triangles into)
    {
        Vertex a = default, b = default, c = default;
        var gathered = 0;     // vertices of a triangle begun afresh, gathered so far
        var any = false;      // whether a triangle stands for the next to go on from
        while (bits.Has(format.FlagBits + format.VertexBits))
        {
            var flag = (int)bits.Read(format.FlagBits) & 3;
            var vertex = format.ReadVertex(ref bits);
            bits.Align();

            if (gathered is 1 or 2)
            {
                if (gathered == 1) b = vertex;
                else c = vertex;
                if (++gathered < 3) continue;
                gathered = 0;
                into.Add(a, b, c);
                any = true;
                continue;
            }

            switch (flag)
            {
                case 0:
                    a = vertex;
                    gathered = 1;
                    break;
                case 1 when any:
                    (a, b, c) = (b, c, vertex);
                    into.Add(a, b, c);
                    break;
                case 2 when any:
                    (a, b, c) = (a, c, vertex);
                    into.Add(a, b, c);
                    break;
            }
        }
    }

    /// <summary>Type 5: rows of vertices, each pair of rows a strip of quadrilaterals cut into two triangles each.</summary>
    private static void Lattice(ref Bits bits, Format format, int perRow, Triangles into)
    {
        if (perRow < 2) return;
        var above = new Vertex[perRow];
        var row = new Vertex[perRow];
        for (var rows = 0; ; rows++)
        {
            for (var i = 0; i < perRow; i++)
            {
                if (!bits.Has(format.VertexBits)) return;
                row[i] = format.ReadVertex(ref bits);
                bits.Align();
            }

            if (rows > 0)
                for (var i = 0; i + 1 < perRow; i++)
                {
                    into.Add(above[i], above[i + 1], row[i]);
                    into.Add(above[i + 1], row[i + 1], row[i]);
                }

            (above, row) = (row, above);
        }
    }

    /// <summary>
    /// Types 6 and 7: patches bounded by four cubic Bézier curves, colours at their corners, each patch after the first
    /// sharing an edge — and its two corner colours — with the one before it, as its flag says. A Coons patch's inner
    /// control points are those of the tensor-product patch it equals.
    /// </summary>
    private static void Patches(ref Bits bits, Format format, bool tensor, Triangles into)
    {
        var patches = new List<(Point[] Points, float[][] Colours)>();
        Point[]? before = null;
        float[][]? coloursBefore = null;
        while (bits.Has(format.FlagBits))
        {
            var flag = (int)bits.Read(format.FlagBits) & 3;
            if (flag != 0 && before is null) break;

            var points = new Point[16];
            var colours = new float[4][];
            int firstPoint = 0, firstColour = 0;
            if (flag != 0)
            {
                // The shared edge, in the order this patch runs it: the one before's right, top or left edge (12 points
                // round the boundary from its first corner), and the two colours at its ends.
                int[] edge = flag switch { 1 => [3, 4, 5, 6], 2 => [6, 7, 8, 9], _ => [9, 10, 11, 0] };
                for (var i = 0; i < 4; i++) points[i] = before![edge[i]];
                colours[0] = coloursBefore![flag];
                colours[1] = coloursBefore[(flag + 1) % 4];
                firstPoint = 4;
                firstColour = 2;
            }

            var count = tensor ? 16 : 12;
            if (!bits.Has((count - firstPoint) * 2 * format.CoordinateBits + (4 - firstColour) * format.ColourBits)) break;
            for (var i = firstPoint; i < count; i++) points[i] = format.ReadPoint(ref bits);
            for (var i = firstColour; i < 4; i++) colours[i] = format.ReadColour(ref bits);
            bits.Align();

            if (!tensor) Inside(points);
            patches.Add((points, colours));
            before = points;
            coloursBefore = colours;
        }

        // How finely a patch is evaluated goes by how much of the whole mesh it spans.
        var all = Rect.Empty;
        foreach (var (points, _) in patches)
            foreach (var point in points)
                all.Union(point);
        foreach (var (points, colours) in patches)
            Evaluate(points, colours, Steps(points, all), into);
    }

    /// <summary>Stream order — the boundary from the first corner, then for type 7 the four inside — to rows and columns
    /// of the patch's 4 × 4 control points (row the first parameter, column the second).</summary>
    private static readonly (int Row, int Column)[] Grid =
    [
        (0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0),
        (1, 1), (1, 2), (2, 2), (2, 1),
    ];

    /// <summary>A Coons patch's inner control points from its boundary (8.7.4.5.8), so it is evaluated as the tensor-product patch it is.</summary>
    private static void Inside(Point[] points)
    {
        Vector P(int row, int column) => (Vector)points[Array.IndexOf(Grid, (row, column))];
        Point Of(Vector v) => (Point)(v / 9);

        points[12] = Of(-4 * P(0, 0) + 6 * (P(0, 1) + P(1, 0)) - 2 * (P(0, 3) + P(3, 0)) + 3 * (P(3, 1) + P(1, 3)) - P(3, 3));
        points[13] = Of(-4 * P(0, 3) + 6 * (P(0, 2) + P(1, 3)) - 2 * (P(0, 0) + P(3, 3)) + 3 * (P(3, 2) + P(1, 0)) - P(3, 0));
        points[14] = Of(-4 * P(3, 3) + 6 * (P(3, 2) + P(2, 3)) - 2 * (P(3, 0) + P(0, 3)) + 3 * (P(0, 2) + P(2, 0)) - P(0, 0));
        points[15] = Of(-4 * P(3, 0) + 6 * (P(3, 1) + P(2, 0)) - 2 * (P(3, 3) + P(0, 0)) + 3 * (P(0, 1) + P(2, 3)) - P(0, 3));
    }

    private static int Steps(Point[] points, Rect all)
    {
        var reach = Rect.Empty;
        foreach (var point in points) reach.Union(point);
        var share = Math.Max(all.Width > 0 ? reach.Width / all.Width : 0, all.Height > 0 ? reach.Height / all.Height : 0);
        return Math.Clamp((int)Math.Ceiling(share * MostSteps), FewestSteps, MostSteps);
    }

    /// <summary>
    /// The patch evaluated on a grid of <paramref name="steps"/> × <paramref name="steps"/> cells, two triangles each:
    /// its surface the tensor product of cubic Bernstein polynomials over its control points, its colour bilinear between
    /// its corners — the first corner at (0, 0), then (0, 1), (1, 1) and (1, 0).
    /// </summary>
    private static void Evaluate(Point[] points, float[][] colours, int steps, Triangles into)
    {
        var control = new Point[4, 4];
        for (var i = 0; i < 16; i++) control[Grid[i].Row, Grid[i].Column] = points[i];

        var grid = new Vertex[steps + 1, steps + 1];
        Span<double> bu = stackalloc double[4];
        Span<double> bv = stackalloc double[4];
        for (var a = 0; a <= steps; a++)
        {
            var u = (double)a / steps;
            Bernstein(u, bu);
            for (var b = 0; b <= steps; b++)
            {
                var v = (double)b / steps;
                Bernstein(v, bv);
                double x = 0, y = 0;
                for (var i = 0; i < 4; i++)
                    for (var j = 0; j < 4; j++)
                    {
                        var weight = bu[i] * bv[j];
                        x += control[i, j].X * weight;
                        y += control[i, j].Y * weight;
                    }

                var colour = new float[colours[0].Length];
                for (var k = 0; k < colour.Length; k++)
                    colour[k] = (float)((1 - u) * (1 - v) * colours[0][k] + (1 - u) * v * colours[1][k]
                                        + u * v * colours[2][k] + u * (1 - v) * colours[3][k]);
                grid[a, b] = new Vertex(x, y, colour);
            }
        }

        for (var a = 0; a < steps; a++)
            for (var b = 0; b < steps; b++)
            {
                into.Add(grid[a, b], grid[a + 1, b], grid[a, b + 1]);
                into.Add(grid[a + 1, b], grid[a + 1, b + 1], grid[a, b + 1]);
            }
    }

    private static void Bernstein(double t, Span<double> into)
    {
        var s = 1 - t;
        into[0] = s * s * s;
        into[1] = 3 * t * s * s;
        into[2] = 3 * t * t * s;
        into[3] = t * t * t;
    }

    /// <summary>One vertex: where it is, in the shading's space, and its colour's values.</summary>
    private readonly record struct Vertex(double X, double Y, float[] Colour);

    /// <summary>The triangles gathered so far, as the mesh holds them.</summary>
    private sealed class Triangles(int channels)
    {
        public List<float> Points { get; } = [];

        public List<float> Values { get; } = [];

        public void Add(Vertex a, Vertex b, Vertex c)
        {
            foreach (var vertex in (ReadOnlySpan<Vertex>)[a, b, c])
            {
                Points.Add((float)vertex.X);
                Points.Add((float)vertex.Y);
                for (var k = 0; k < channels; k++) Values.Add(vertex.Colour[k]);
            }
        }
    }

    /// <summary>
    /// How a mesh's data is written — its bits per coordinate, colour component and flag, and the ranges they decode to —
    /// and its colours: a vertex's components converted out of the shading's colour space as they are read, or, where the
    /// shading has a function, its parameter kept to be looked up in <see cref="Lookup"/>.
    /// </summary>
    private sealed class Format
    {
        private readonly Shading _shading;
        private readonly int _coordinateBits;
        private readonly int _componentBits;
        private readonly double[] _decode;
        private readonly int _components;
        private readonly RenderingIntent _intent;

        public Format(Shading shading, int coordinateBits, int componentBits, int flagBits, double[] decode, RenderingIntent intent)
        {
            _shading = shading;
            _coordinateBits = coordinateBits;
            _componentBits = componentBits;
            _decode = decode;
            _intent = intent;
            FlagBits = flagBits;
            _components = shading.Functions is { Length: > 0 } ? 1 : shading.ColorSpace.NumberOfColorComponents;
            Lookup = shading.Functions is { Length: > 0 } ? LookupOf() : null;
        }

        public int FlagBits { get; }

        public int CoordinateBits => _coordinateBits;

        public int ColourBits => _components * _componentBits;

        public int VertexBits => 2 * _coordinateBits + ColourBits;

        public int Channels => Lookup is null ? 3 : 1;

        public Color[]? Lookup { get; }

        public Vertex ReadVertex(ref Bits bits)
        {
            var point = ReadPoint(ref bits);
            return new Vertex(point.X, point.Y, ReadColour(ref bits));
        }

        public Point ReadPoint(ref Bits bits) =>
            new(Decoded(bits.Read(_coordinateBits), _coordinateBits, 0), Decoded(bits.Read(_coordinateBits), _coordinateBits, 1));

        /// <summary>A vertex's colour: red, green and blue in 0..1, or its function's parameter brought to 0..1.</summary>
        public float[] ReadColour(ref Bits bits)
        {
            Span<double> components = stackalloc double[_components];
            for (var i = 0; i < _components; i++) components[i] = Decoded(bits.Read(_componentBits), _componentBits, 2 + i);

            if (Lookup is not null)
            {
                var (low, high) = Range(2);
                return [(float)(high > low ? Math.Clamp((components[0] - low) / (high - low), 0, 1) : 0)];
            }

            var colour = PdfColours.ToColour(_shading.ColorSpace.GetColor(components.ToArray(), _intent), 1, _intent);
            return [colour.R / 255f, colour.G / 255f, colour.B / 255f];
        }

        /// <summary>The function's colours from one end of the parameter's range to the other.</summary>
        private Color[] LookupOf()
        {
            var (low, high) = Range(2);
            var lookup = new Color[LookupSize];
            for (var i = 0; i < lookup.Length; i++)
            {
                var t = low + (high - low) * i / (lookup.Length - 1);
                lookup[i] = PdfColours.ToColour(_shading.ColorSpace.GetColor(_shading.Eval([t]), _intent), 1, _intent);
            }

            return lookup;
        }

        /// <summary>The raw value read in <paramref name="width"/> bits mapped onto the <paramref name="pair"/>-th range of <c>/Decode</c>.</summary>
        private double Decoded(ulong raw, int width, int pair)
        {
            var (low, high) = Range(pair);
            var most = width >= 64 ? ulong.MaxValue : (1UL << width) - 1;
            return low + raw * (high - low) / most;
        }

        private (double Low, double High) Range(int pair) =>
            _decode.Length >= 2 * pair + 2 ? (_decode[2 * pair], _decode[2 * pair + 1]) : (0, 1);
    }

    /// <summary>A mesh's data read high bit first, any number of bits at a time.</summary>
    private ref struct Bits(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private long _at;

        public readonly bool Has(int count) => _at + count <= (long)_data.Length * 8;

        public ulong Read(int count)
        {
            ulong value = 0;
            while (count > 0)
            {
                var offset = (int)(_at & 7);
                var take = Math.Min(count, 8 - offset);
                var part = (_data[(int)(_at >> 3)] >> (8 - offset - take)) & ((1 << take) - 1);
                value = (value << take) | (uint)part;
                _at += take;
                count -= take;
            }

            return value;
        }

        public void Align() => _at = (_at + 7) & ~7L;
    }
}
