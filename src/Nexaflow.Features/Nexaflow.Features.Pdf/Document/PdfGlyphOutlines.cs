using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.PdfFonts;
using UglyToad.PdfPig.Tokenization.Scanner;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// Glyph outlines for a document's fonts, in glyph space (one em to the unit, y up), read once per glyph.
/// <para>
/// An embedded font gives its own outlines. A font the document only names — the standard 14, or a font the writer
/// chose not to embed — is stood in for by the installed family of its name, or failing that by installed faces of the
/// kind it asks for (<see cref="PdfFaceKinds"/>: serif, sans or fixed pitch), each glyph stretched to the advance the
/// document gives it so the line keeps the length it was set to.
/// </para>
/// </summary>
internal sealed partial class PdfGlyphOutlines
{
    /// <summary>What stands in when no installed family answers to a font's name.</summary>
    private const string Fallback = "Arial";

    private readonly ConditionalWeakTable<IFont, FontGlyphs> _fonts = new();
    private readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), GlyphTypeface?> _faces = new();
    private readonly ConcurrentDictionary<(string Name, bool Bold, bool Italic, PdfFaceKind Kind), IReadOnlyList<GlyphTypeface>> _chains = new();

    /// <summary>
    /// The outline for <paramref name="code"/> in <paramref name="font"/>; empty for a glyph with no ink, and null
    /// when neither the font nor any stand-in has one.
    /// </summary>
    /// <param name="font">The font the glyph is set in.</param>
    /// <param name="scanner">The document's objects, which the font's descriptor is read from when it needs a stand-in.</param>
    /// <param name="code">The character code the page used.</param>
    /// <param name="text">What the code means, for finding a stand-in's glyph.</param>
    /// <param name="advance">The advance the document gives the glyph, in ems.</param>
    public PdfShape? Outline(IFont font, IPdfTokenScanner scanner, int code, string text, double advance)
    {
        var glyphs = _fonts.GetValue(font, static _ => new FontGlyphs());
        lock (glyphs)
        {
            if (glyphs.Outlines.TryGetValue(code, out var known)) return known;

            var outline = Embedded(font, code) ?? StandIn(font, glyphs, scanner, code, text, advance);
            glyphs.Outlines[code] = outline;
            return outline;
        }
    }

    private static PdfShape? Embedded(IFont font, int code)
    {
        if (!font.TryGetNormalisedPath(code, out var path)) return null;

        var writer = new PdfShape.Writer();
        foreach (var subpath in path)
        {
            foreach (var command in subpath.Commands)
            {
                switch (command)
                {
                    case PdfSubpath.Move move:
                        writer.MoveTo(move.Location.X, move.Location.Y);
                        break;
                    case PdfSubpath.Line line:
                        writer.LineTo(line.To.X, line.To.Y);
                        break;
                    case PdfSubpath.CubicBezierCurve cubic:
                        writer.CubicTo(cubic.FirstControlPoint.X, cubic.FirstControlPoint.Y,
                            cubic.SecondControlPoint.X, cubic.SecondControlPoint.Y, cubic.EndPoint.X, cubic.EndPoint.Y);
                        break;
                    case PdfSubpath.QuadraticBezierCurve quadratic:
                        writer.QuadraticTo(quadratic.ControlPoint.X, quadratic.ControlPoint.Y,
                            quadratic.EndPoint.X, quadratic.EndPoint.Y);
                        break;
                    case PdfSubpath.Close:
                        writer.Close();
                        break;
                }
            }
        }

        return writer.Take();
    }

    /// <summary>
    /// The glyph drawn by installed fonts standing in for one the document does not carry: each letter from the first face
    /// in <see cref="StandInFaces"/> that has it, stretched to the advance the document gives it.
    /// </summary>
    private PdfShape? StandIn(IFont font, FontGlyphs glyphs, IPdfTokenScanner scanner, int code, string text, double advance)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (string.IsNullOrWhiteSpace(text)) return PdfShape.Empty;

        var faces = StandInFaces(font, glyphs.Kind ??= PdfFaceKinds.Of(font, scanner));
        if (faces.Count == 0) return null;

        var writer = new PdfShape.Writer();
        var x = 0.0;
        var any = false;
        var single = text.EnumerateRunes().Count() == 1;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Glyph(faces, rune.Value, single ? code : -1) is not { } found) continue;
            var (face, index) = found;
            any = true;

            var outline = face.GetGlyphOutline(index, 1, 1);
            var width = face.AdvanceWidths[index];

            // An installed font's outline is y down from the baseline; glyph space is y up. The whole text of one code
            // (a ligature's letters, say) shares the code's advance.
            var stretch = advance > 0 && width > 0 && single ? advance / width : 1;
            Append(writer, outline, new Matrix(stretch, 0, 0, -1, x, 0));
            x += width * stretch;
        }

        return any ? writer.Take() : null;
    }

    /// <summary>
    /// The first face holding <paramref name="character"/>. A symbol face (Symbol, Wingdings) keeps its glyphs at
    /// U+F000 plus the code a document sets them by rather than at their Unicode meaning, so for a one-letter code it is
    /// asked for that too.
    /// </summary>
    private static (GlyphTypeface Face, ushort Index)? Glyph(IReadOnlyList<GlyphTypeface> faces, int character, int code)
    {
        foreach (var face in faces)
        {
            if (face.CharacterToGlyphMap.TryGetValue(character, out var index)) return (face, index);
            if (face.Symbol && code is >= 0 and <= 0xFF && face.CharacterToGlyphMap.TryGetValue(SymbolPage + code, out index))
                return (face, index);
        }

        return null;
    }

    /// <summary>Where a symbol face keeps the glyphs a document sets by single-byte codes.</summary>
    private const int SymbolPage = 0xF000;

    /// <summary>
    /// The faces that stand in for <paramref name="font"/>, nearest first: the installed family of its name, then families
    /// of the <paramref name="kind"/> it asks for covering the scripts a document most often names a font for without
    /// embedding it — Latin, symbols, CJK — so a letter missing from one is found in the next.
    /// </summary>
    private IReadOnlyList<GlyphTypeface> StandInFaces(IFont font, PdfFaceKind kind)
    {
        var details = font.Details;
        var bold = details?.IsBold ?? false;
        var italic = details?.IsItalic ?? false;
        var name = font.Name?.Data ?? details?.Name;

        return _chains.GetOrAdd((name ?? string.Empty, bold, italic, kind), key =>
        {
            var families = new List<string> { Family(key.Name) };
            families.AddRange(StandInFamilies(key.Kind));

            var faces = new List<GlyphTypeface>();
            foreach (var family in families)
                if (Face(family, key.Bold, key.Italic) is { } face && !faces.Contains(face))
                    faces.Add(face);
            return faces;
        });
    }

    /// <summary>The installed families that stand in, in turn, for a font of <paramref name="kind"/> whose own is not installed.</summary>
    internal static IReadOnlyList<string> StandInFamilies(PdfFaceKind kind) => kind switch
    {
        PdfFaceKind.Serif => SerifFallbacks,
        PdfFaceKind.Fixed => FixedFallbacks,
        _ => SansFallbacks,
    };

    /// <summary>Installed families that stand in, in turn, for a sans-serif font: Latin, then symbols, then CJK.</summary>
    private static readonly string[] SansFallbacks =
        [Fallback, "Segoe UI", "Segoe UI Symbol", "Yu Gothic", "MS Gothic", "Microsoft YaHei", "Malgun Gothic", "Nirmala UI"];

    /// <summary>Installed families that stand in, in turn, for a serif font.</summary>
    private static readonly string[] SerifFallbacks =
        ["Times New Roman", "Segoe UI", "Segoe UI Symbol", "Yu Mincho", "MS Mincho", "SimSun", "Batang", "Nirmala UI"];

    /// <summary>Installed families that stand in, in turn, for a fixed-pitch font.</summary>
    private static readonly string[] FixedFallbacks =
        ["Courier New", "Consolas", "Segoe UI Symbol", "MS Gothic", "SimSun", "Nirmala UI"];

    private static void Append(PdfShape.Writer writer, Geometry outline, Matrix through)
    {
        var path = PathGeometry.CreateFromGeometry(outline);
        foreach (var figure in path.Figures)
        {
            var start = through.Transform(figure.StartPoint);
            writer.MoveTo(start.X, start.Y);
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        Line(writer, through, line.Point);
                        break;
                    case PolyLineSegment lines:
                        foreach (var point in lines.Points) Line(writer, through, point);
                        break;
                    case BezierSegment cubic:
                        Cubic(writer, through, cubic.Point1, cubic.Point2, cubic.Point3);
                        break;
                    case PolyBezierSegment cubics:
                        for (var i = 0; i + 2 < cubics.Points.Count; i += 3)
                            Cubic(writer, through, cubics.Points[i], cubics.Points[i + 1], cubics.Points[i + 2]);
                        break;
                    case QuadraticBezierSegment quadratic:
                        Quadratic(writer, through, quadratic.Point1, quadratic.Point2);
                        break;
                    case PolyQuadraticBezierSegment quadratics:
                        for (var i = 0; i + 1 < quadratics.Points.Count; i += 2)
                            Quadratic(writer, through, quadratics.Points[i], quadratics.Points[i + 1]);
                        break;
                }
            }

            if (figure.IsClosed) writer.Close();
        }
    }

    private static void Line(PdfShape.Writer writer, Matrix through, Point to)
    {
        var p = through.Transform(to);
        writer.LineTo(p.X, p.Y);
    }

    private static void Cubic(PdfShape.Writer writer, Matrix through, Point a, Point b, Point c)
    {
        var p1 = through.Transform(a);
        var p2 = through.Transform(b);
        var p3 = through.Transform(c);
        writer.CubicTo(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y);
    }

    private static void Quadratic(PdfShape.Writer writer, Matrix through, Point a, Point b)
    {
        var p1 = through.Transform(a);
        var p2 = through.Transform(b);
        writer.QuadraticTo(p1.X, p1.Y, p2.X, p2.Y);
    }

    private GlyphTypeface? Face(string family, bool bold, bool italic) =>
        _faces.GetOrAdd((family, bold, italic), static key =>
        {
            var typeface = new Typeface(
                new FontFamily(key.Family),
                key.Italic ? FontStyles.Italic : FontStyles.Normal,
                key.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            return typeface.TryGetGlyphTypeface(out var face) ? face : null;
        });

    /// <summary>
    /// The installed family that stands in for a font the document only names. The standard 14 have metric-compatible
    /// Windows faces; anything else is looked for under its own name, its subset prefix and style suffix taken off.
    /// </summary>
    internal static string Family(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Fallback;

        var bare = SubsetPrefix().Replace(name, string.Empty);
        var root = bare.Split('-', ',')[0];

        return root switch
        {
            "Helvetica" or "Arial" or "ArialMT"                     => "Arial",
            "Times" or "Times-Roman" or "TimesNewRoman"
                or "TimesNewRomanPS" or "TimesNewRomanPSMT"          => "Times New Roman",
            "Courier" or "CourierNew" or "CourierNewPSMT"           => "Courier New",
            "Symbol"                                                => "Symbol",
            "ZapfDingbats"                                          => "Segoe UI Symbol",
            _                                                        => SpacedName(root),
        };
    }

    /// <summary>A PostScript name like <c>BookAntiqua</c> as the family Windows knows: <c>Book Antiqua</c>.</summary>
    private static string SpacedName(string root)
    {
        var trimmed = root.EndsWith("MT", StringComparison.Ordinal) || root.EndsWith("PS", StringComparison.Ordinal)
            ? root[..^2]
            : root;
        return CamelBoundary().Replace(trimmed, " $1");
    }

    [GeneratedRegex("^[A-Z]{6}\\+")]
    private static partial Regex SubsetPrefix();

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex CamelBoundary();

    private sealed class FontGlyphs
    {
        public Dictionary<int, PdfShape?> Outlines { get; } = [];

        /// <summary>The kind of face the font asks for, read once it first needs a stand-in.</summary>
        public PdfFaceKind? Kind { get; set; }
    }
}
