using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Fonts;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Painting;

/// <summary>
/// WPF as a medium a renderer-free layout is made in: every handle a builder holds is WPF's own object, or wraps one made
/// once, so measuring and drawing share one shaping and painting converts nothing.
///
/// <para>
/// One to a thread, as WPF itself is: what it keeps — the inks, the rulings, the faces, the glyphs cut from them — is kept for
/// everything laid on that thread, and nothing it keeps is reached from another.
/// </para>
/// </summary>
public sealed class WpfMedium : ILayoutMedium
{
    /// <summary>The medium everything laid on this thread is made in.</summary>
    public static WpfMedium OnThisThread => _onThisThread ??= new WpfMedium();

    [ThreadStatic]
    private static WpfMedium? _onThisThread;

    private readonly Dictionary<Color, WpfInk> _inks = [];

    public LayoutInk Ink(LayoutColor color)
    {
        var wpf = Color.FromArgb(color.A, color.R, color.G, color.B);
        if (_inks.TryGetValue(wpf, out var known)) return known;

        var brush = new SolidColorBrush(wpf);
        brush.Freeze();
        return _inks[wpf] = new WpfInk(brush);
    }

    /// <summary>
    /// An ink for a brush a theme already holds — a gradient, a picture, or a colour made elsewhere — the same ink each time it is
    /// asked for the same brush, since a picture a piece keeps is kept by the ink it was painted with.
    /// </summary>
    public WpfInk Ink(Brush brush) => _held.GetValue(brush, static held => new WpfInk(held.IsFrozen ? held : (Brush)held.GetAsFrozen()));

    private readonly ConditionalWeakTable<Brush, WpfInk> _held = [];

    public LayoutFace Face(string family, int weight = 400, bool italic = false) =>
        new WpfFace(new Typeface(new FontFamily(family), italic ? FontStyles.Italic : FontStyles.Normal,
                                 FontWeight.FromOpenTypeWeight(weight), FontStretches.Normal));

    /// <summary>A face for a typeface a theme already holds.</summary>
    public WpfFace Face(Typeface typeface) => new(typeface);

    public IShapedText Shape(string text, LayoutFace face, double size, LayoutInk ink, TextLines lines = TextLines.None, LayoutInk? dots = null,
                             IReadOnlyList<(int Start, int Length, LayoutFace Face)>? faces = null)
    {
        var set = Shape(text, ((WpfFace)face).Typeface, size, ((WpfInk)ink).Brush, Decorations(lines, ((WpfInk?)dots)?.Brush), CultureInfo.CurrentCulture);
        if (faces is not null)
            foreach (var (start, length, own) in faces) set.SetFontTypeface(((WpfFace)own).Typeface, start, length);

        return set;
    }

    public TextMeasure Measuring(LayoutFace face, double size) => new WpfMeasure(TextWidths.Set(((WpfFace)face).Typeface, size));

    public ShapedLine Line(string line, LayoutFace face, double size)
    {
        var run = new RunSetting(((WpfFace)face).Typeface, size);
        return new WpfLine(_formatter.FormatLine(new OneRun(line, run), 0, 0, new LineSetting(run), null));
    }

    private readonly TextFormatter _formatter = TextFormatter.Create(TextFormattingMode.Ideal);

    /// <summary>The same, said in WPF's own terms by something that already holds them.</summary>
    public WpfText Shape(string text, Typeface face, double size, Brush ink, TextDecorationCollection? decorations, CultureInfo culture)
    {
        var set = new WpfText(text, culture, face, size, ink);
        if (decorations is not null) set.SetTextDecorations(decorations);

        return set;
    }

    /// <summary>
    /// The rules drawn through or under words, made once for each way of ruling — strike, underline, then dots, as a document's
    /// builder makes them — rather than once for every run ruled.
    /// </summary>
    private TextDecorationCollection? Decorations(TextLines lines, Brush? dots)
    {
        if (lines == TextLines.None) return null;

        var key = (lines, lines.HasFlag(TextLines.Dotted) ? dots : null);
        if (_decorations.TryGetValue(key, out var made)) return made;

        made = [];
        if (lines.HasFlag(TextLines.Through)) made.Add(TextDecorations.Strikethrough);
        if (lines.HasFlag(TextLines.Under)) made.Add(TextDecorations.Underline);
        if (key.Item2 is { } ink) made.Add(Dots(ink));

        made.Freeze();
        return _decorations[key] = made;
    }

    private readonly Dictionary<(TextLines, Brush?), TextDecorationCollection> _decorations = [];

    /// <summary>A rule of dots, set a little below the letters so it reads as a hint rather than as a link.</summary>
    private static TextDecoration Dots(Brush ink)
    {
        var pen = new Pen(ink, 1) { DashStyle = new DashStyle([1, 2], 0) };
        pen.Freeze();

        return new TextDecoration { Location = TextDecorationLocation.Underline, Pen = pen, PenOffset = 2 };
    }

    public IShapedGlyphs Glyphs(LayoutFace face, double size, LayoutPoint origin, ushort[] indices, double[] advances,
                                LayoutVector[]? offsets = null)
    {
        var glyphs = ((WpfFace)face).Glyphs ?? throw new InvalidOperationException("a face with no font to place glyphs from");

        Point[]? shifted = null;
        if (offsets is { Length: > 0 })
        {
            shifted = new Point[offsets.Length];
            for (var at = 0; at < offsets.Length; at++) shifted[at] = new Point(offsets[at].X, offsets[at].Y);
        }

        return new WpfGlyphs(glyphs, size, new Point(origin.X, origin.Y), indices, advances, shifted);
    }

    /// <summary>The maths faces, opened from the resources the WPF font provider opens them from, once each.</summary>
    public LayoutFace Font(string file)
    {
        if (_fonts.TryGetValue(file, out var known)) return known;

        // The music font is loaded from beside the assembly by the outlines that draw it, and its face is that one.
        if (file == "Bravura.otf")
            return _fonts[file] = new WpfFace(SmuflOutlines.Face ?? throw new InvalidOperationException("Bravura could not be found"));

        var opened = ((WpfGlyphTypeface)WpfMathFontProvider.Instance.ReadFontFile(file)).Typeface;

        return _fonts[file] = new WpfFace(opened);
    }

    private readonly Dictionary<string, LayoutFace> _fonts = [];

    public IShapedGlyphs? Character(LayoutFace face, char character, double size, LayoutPoint origin)
    {
        var glyphs = ((WpfFace)face).Glyphs;
        if (glyphs is null || !glyphs.CharacterToGlyphMap.TryGetValue(character, out var index)) return null;

        return new WpfGlyphs(glyphs, size, origin.ToWpf(), [index], [glyphs.AdvanceWidths[index] * size], null);
    }

    /// <summary>
    /// Where one letter's ink reaches, as WPF's type engine sets it at a size of one — the box a formatted letter takes, less the
    /// room above its ink, scaled by the family's line spacing against its baseline as WPF reports both.
    /// </summary>
    public LetterInk Letter(LayoutFace face, char character)
    {
        var typeface = ((WpfFace)face).Typeface;
        var set = new FormattedText(character.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 1.0,
                                    Brushes.Black, 1);

        var depth = set.Height - set.Baseline + set.OverhangAfter;
        var above = set.Height + set.OverhangAfter - set.Extent;
        var height = (set.Baseline - above) / (typeface.FontFamily.LineSpacing / typeface.FontFamily.Baseline);

        return new LetterInk(set.Width, height, depth);
    }

    /// <summary>
    /// A glyph's outline, cut once for each face, glyph and size by laying it out from the top of its line and moving it up by its
    /// ascent so its origin is its baseline — then placed by a transform of its own, the cut shared by every copy.
    /// </summary>
    public LayoutShape? GlyphOutline(LayoutFace face, int codepoint, double size, LayoutPoint baseline)
    {
        if (Cut(face, codepoint, size) is not { } cut) return null;

        var placed = cut.Outline.Clone();
        placed.Transform = new TranslateTransform(baseline.X, baseline.Y);
        placed.Freeze();
        return WpfShape.Of(placed);
    }

    public double GlyphAdvance(LayoutFace face, int codepoint, double size) => Cut(face, codepoint, size)?.Advance ?? 0;

    private (Geometry Outline, double Advance)? Cut(LayoutFace face, int codepoint, double size)
    {
        var typeface = ((WpfFace)face).Typeface;
        if (_cuts.TryGetValue((typeface, codepoint, size), out var known)) return known;
        if (size <= 0) return null;

        var set = new FormattedText(char.ConvertFromUtf32(codepoint), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface,
                                    size, Brushes.Black, 1.0);
        var outline = set.BuildGeometry(new Point(0, -set.Baseline));
        outline.Freeze();

        return _cuts[(typeface, codepoint, size)] = (outline, set.WidthIncludingTrailingWhitespace);
    }

    private readonly Dictionary<(Typeface Face, int Codepoint, double Size), (Geometry Outline, double Advance)?> _cuts = [];

    public ShapeBuilder Outline(bool evenOdd = true) => new WpfShapeBuilder(evenOdd);

    public LayoutShape Rectangle(LayoutRect box, double radiusX = 0, double radiusY = 0) =>
        WpfShape.Of(new RectangleGeometry(box.ToWpf(), radiusX, radiusY));

    public LayoutShape Ellipse(LayoutPoint centre, double radiusX, double radiusY) =>
        WpfShape.Of(new EllipseGeometry(new Point(centre.X, centre.Y), radiusX, radiusY));

    public LayoutShape Ellipse(LayoutRect box) => WpfShape.Of(new EllipseGeometry(box.ToWpf()));

    public LayoutShape Line(LayoutPoint from, LayoutPoint to) => WpfShape.Of(new LineGeometry(from.ToWpf(), to.ToWpf()));

    public LayoutShape Group(IReadOnlyList<LayoutShape> shapes, bool evenOdd = true)
    {
        var group = new GeometryGroup { FillRule = evenOdd ? FillRule.EvenOdd : FillRule.Nonzero };
        foreach (var shape in shapes) group.Children.Add(((WpfShape)shape).Geometry);

        return WpfShape.Of(group);
    }

    /// <summary>
    /// The shape carried by the matrix — the shape itself with a transform of its own where it has none yet, as WPF code turns one, and
    /// otherwise a group round it carrying the transform.
    /// </summary>
    public LayoutShape Transformed(LayoutShape shape, LayoutMatrix matrix)
    {
        var geometry = ((WpfShape)shape).Geometry;
        var transform = new MatrixTransform(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY);

        if (geometry.Transform is null || geometry.Transform.Value.IsIdentity)
        {
            var moved = geometry.Clone();
            moved.Transform = transform;
            return WpfShape.Of(moved);
        }

        var group = new GeometryGroup { Transform = transform };
        group.Children.Add(geometry);

        return WpfShape.Of(group);
    }

    public LayoutInk Gradient(IReadOnlyList<(LayoutColor Color, double At)> stops, LayoutPoint from, LayoutPoint to)
    {
        var ink = new LinearGradientBrush { StartPoint = from.ToWpf(), EndPoint = to.ToWpf() };
        foreach (var (color, at) in stops) ink.GradientStops.Add(new GradientStop(Color.FromArgb(color.A, color.R, color.G, color.B), at));

        ink.Freeze();
        return new WpfInk(ink);
    }

    public LayoutShape Combine(LayoutShape a, LayoutShape b, ShapeCombine combine) =>
        WpfShape.Of(new CombinedGeometry(combine switch
        {
            ShapeCombine.Intersect => GeometryCombineMode.Intersect,
            ShapeCombine.Exclude => GeometryCombineMode.Exclude,
            ShapeCombine.Xor => GeometryCombineMode.Xor,
            _ => GeometryCombineMode.Union,
        }, ((WpfShape)a).Geometry, ((WpfShape)b).Geometry));

    public LayoutShape Widen(LayoutShape shape, double thickness) =>
        WpfShape.Of(((WpfShape)shape).Geometry.GetWidenedPathGeometry(new Pen(Brushes.Black, thickness)));

    public LayoutShape Resolved(LayoutShape shape)
    {
        var geometry = ((WpfShape)shape).Geometry;
        if (geometry is not (CombinedGeometry or GeometryGroup)) return shape;

        return WpfShape.Of(PathGeometry.CreateFromGeometry(geometry));
    }

    public LayoutStroke Stroke(LayoutInk? ink, double thickness, ReadOnlySpan<double> dashes = default, StrokeEnds? ends = null) =>
        new WpfStroke((WpfInk?)ink, thickness, dashes.IsEmpty ? null : new DoubleCollection(dashes.ToArray()), ends);

    public LayoutTurn Turn(LayoutMatrix matrix) => new WpfTurn(matrix);

    public LayoutTurn Rotation(double degrees, LayoutPoint about) => new WpfTurn(new RotateTransform(degrees, about.X, about.Y));

    /// <summary>A turn a theme or a typesetter already holds as WPF's own.</summary>
    public WpfTurn Turn(Transform transform) => new(transform);

    public LayoutGuides Guides(double[] across, double[] down) => new WpfGuides(new GuidelineSet(across, down));

    /// <summary>A picture a host handed over — the same handle each time it is handed the same picture.</summary>
    public LayoutPicture Picture(ImageSource picture) => _pictures.GetValue(picture, static source => new WpfPicture(source));

    private readonly ConditionalWeakTable<ImageSource, WpfPicture> _pictures = [];

    /// <summary>How many device pixels a unit of the layout is drawn across — one, since a layout is measured where it is shown.</summary>
    public const double Density = 1.0;

    public LayoutInk Faded(LayoutInk ink, double opacity)
    {
        if (opacity >= 1) return ink;

        var brush = ((WpfInk)ink).Brush;
        if (_faded.TryGetValue((brush, opacity), out var known)) return known;

        var faded = brush.Clone();
        faded.Opacity = Math.Max(0, opacity);
        faded.Freeze();
        return _faded[(brush, opacity)] = new WpfInk(faded);
    }

    private readonly Dictionary<(Brush Ink, double Opacity), WpfInk> _faded = [];

    public LayoutShape? TextOutline(IShapedText text, LayoutPoint origin) =>
        ((WpfText)text).BuildGeometry(origin.ToWpf()) is { } outline ? WpfShape.Of(outline) : null;

    public LayoutShape? Covering(IShapedText text, LayoutPoint at, int from, int count) =>
        ((WpfText)text).BuildHighlightGeometry(at.ToWpf(), from, count) is { } covered ? WpfShape.Of(covered) : null;

    public IReadOnlyList<LayoutFigure> Flattened(LayoutShape shape, double tolerance)
    {
        var flattened = ((WpfShape)shape).Geometry.GetFlattenedPathGeometry(tolerance, ToleranceType.Absolute);
        var figures = new List<LayoutFigure>(flattened.Figures.Count);
        var points = new List<LayoutPoint>();

        foreach (var figure in flattened.Figures)
        {
            points.Clear();
            points.Add(new LayoutPoint(figure.StartPoint.X, figure.StartPoint.Y));

            foreach (var segment in figure.Segments)
                switch (segment)
                {
                    case PolyLineSegment poly:
                        foreach (var point in poly.Points) points.Add(new LayoutPoint(point.X, point.Y));
                        break;

                    case LineSegment line:
                        points.Add(new LayoutPoint(line.Point.X, line.Point.Y));
                        break;
                }

            figures.Add(new LayoutFigure([.. points], figure.IsClosed));
        }

        return figures;
    }
}

/// <summary>The renderer-free layout's values said in WPF's terms, and back.</summary>
public static class WpfLayout
{
    public static Rect ToWpf(this LayoutRect rect) => rect.IsEmpty ? Rect.Empty : new Rect(rect.X, rect.Y, rect.Width, rect.Height);

    public static LayoutRect ToLayout(this Rect rect) => rect.IsEmpty ? LayoutRect.Empty : new LayoutRect(rect.X, rect.Y, rect.Width, rect.Height);

    public static Point ToWpf(this LayoutPoint point) => new(point.X, point.Y);

    public static LayoutPoint ToLayout(this Point point) => new(point.X, point.Y);

    public static LayoutMatrix ToLayout(this Matrix matrix) =>
        new(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY);

    public static Matrix ToWpf(this LayoutMatrix matrix) =>
        new(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY);
}

/// <summary>An ink, as the brush WPF paints it with.</summary>
public sealed class WpfInk(Brush brush) : LayoutInk
{
    public Brush Brush { get; } = brush;

    public override LayoutColor Color => Brush is SolidColorBrush { Color: var color }
        ? new LayoutColor(color.A, color.R, color.G, color.B)
        : default;

    public override bool Solid => Brush is SolidColorBrush;
}

/// <summary>A face, as the typeface WPF sets words in and the glyph typeface it places glyphs from.</summary>
public sealed class WpfFace : LayoutFace
{
    public WpfFace(Typeface typeface)
    {
        Typeface = typeface;
        Glyphs = typeface.TryGetGlyphTypeface(out var glyphs) ? glyphs : null;
    }

    /// <summary>A face for the font a typesetter's glyphs were already taken from.</summary>
    public WpfFace(GlyphTypeface glyphs)
    {
        Typeface = new Typeface(new FontFamily(glyphs.FontUri.ToString()), glyphs.Style, glyphs.Weight, glyphs.Stretch);
        Glyphs = glyphs;
    }

    public Typeface Typeface { get; }

    public GlyphTypeface? Glyphs { get; }

    public override string Family => Typeface.FontFamily.Source;

    public override int Weight => Typeface.Weight.ToOpenTypeWeight();

    public override bool Italic => Typeface.Style != FontStyles.Normal;
}

/// <summary>A run of words WPF set, and a run of words a renderer-free layout holds — one object, not two.</summary>
public sealed class WpfText(string text, CultureInfo culture, Typeface face, double size, Brush ink)
    : FormattedText(text, culture, FlowDirection.LeftToRight, face, size, ink, WpfMedium.Density), IShapedText
{
    public double WidthWithSpace => WidthIncludingTrailingWhitespace;

    public double Overhang => OverhangAfter;

    public double Room => MaxTextWidth;

    public LayoutAlign Align => TextAlignment switch
    {
        TextAlignment.Right => LayoutAlign.Right,
        TextAlignment.Center => LayoutAlign.Centre,
        TextAlignment.Justify => LayoutAlign.Justify,
        _ => LayoutAlign.Left,
    };

    public void Bound(double room, LayoutAlign align)
    {
        if (double.IsFinite(room))
        {
            var most = Math.Max(1, room);
            if (MaxTextWidth != most) MaxTextWidth = most;
        }

        var wpf = align switch
        {
            LayoutAlign.Right => TextAlignment.Right,
            LayoutAlign.Centre => TextAlignment.Center,
            LayoutAlign.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };

        if (TextAlignment != wpf) TextAlignment = wpf;
    }

    public LayoutRect Covers(LayoutPoint at, int from, int count) =>
        BuildHighlightGeometry(at.ToWpf(), from, count) is { } shape ? shape.Bounds.ToLayout() : LayoutRect.Empty;
}

/// <summary>Glyphs a typesetter placed, as WPF holds them and as a renderer-free layout does — one object.</summary>
public sealed class WpfGlyphs(GlyphTypeface face, double size, Point origin, IList<ushort> indices, IList<double> advances,
                                IList<Point>? offsets)
    : GlyphRun(face, 0, false, size, (float)WpfMedium.Density, indices, origin, advances, offsets, null, null, null, null, null),
      IShapedGlyphs
{
    public LayoutRect Covers => Rect.Offset(ComputeInkBoundingBox(), BaselineOrigin.X, BaselineOrigin.Y).ToLayout();
}

/// <summary>An outline, as the frozen geometry WPF draws.</summary>
public sealed class WpfShape : LayoutShape
{
    private WpfShape(Geometry geometry) => Geometry = geometry;

    /// <summary>An outline the medium has just made, frozen where it stands — nobody else holds it, so there is nothing to copy.</summary>
    public static WpfShape Of(Geometry geometry)
    {
        if (!geometry.IsFrozen) geometry.Freeze();
        return new(geometry);
    }

    public Geometry Geometry { get; }

    public override LayoutRect Bounds => Geometry.Bounds.ToLayout();

    public override bool Contains(LayoutPoint point) => Geometry.FillContains(point.ToWpf());

    public override bool Touches(LayoutRect area) =>
        Geometry.FillContainsWithDetail(new RectangleGeometry(area.ToWpf())) != IntersectionDetail.Empty;
}

internal sealed class WpfShapeBuilder : ShapeBuilder
{
    private readonly StreamGeometry _geometry = new();
    private readonly StreamGeometryContext _into;

    public WpfShapeBuilder(bool evenOdd)
    {
        _geometry.FillRule = evenOdd ? FillRule.EvenOdd : FillRule.Nonzero;
        _into = _geometry.Open();
    }

    /// <summary>Whether the figure being made has its edges drawn by a pen.</summary>
    private bool _stroked = true;

    public override void Begin(LayoutPoint at, bool filled = true, bool closed = true, bool stroked = true)
    {
        _stroked = stroked;
        _into.BeginFigure(at.ToWpf(), filled, closed);
    }

    public override void LineTo(LayoutPoint to, bool? stroked = null, bool smooth = false) => _into.LineTo(to.ToWpf(), stroked ?? _stroked, smooth);

    public override void PolyLineTo(IReadOnlyList<LayoutPoint> points, bool? stroked = null, bool smooth = false) =>
        _into.PolyLineTo([.. points.Select(point => point.ToWpf())], stroked ?? _stroked, smooth);

    public override void BezierTo(LayoutPoint first, LayoutPoint second, LayoutPoint to, bool? stroked = null, bool smooth = false) =>
        _into.BezierTo(first.ToWpf(), second.ToWpf(), to.ToWpf(), stroked ?? _stroked, smooth);

    public override void QuadraticTo(LayoutPoint control, LayoutPoint to, bool? stroked = null, bool smooth = false) =>
        _into.QuadraticBezierTo(control.ToWpf(), to.ToWpf(), stroked ?? _stroked, smooth);

    public override void ArcTo(LayoutPoint to, double radiusX, double radiusY, double rotation, bool large, bool clockwise,
                               bool? stroked = null, bool smooth = false) =>
        _into.ArcTo(to.ToWpf(), new Size(radiusX, radiusY), rotation, large,
                    clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, stroked ?? _stroked, smooth);

    public override LayoutShape Close()
    {
        _into.Close();
        _geometry.Freeze();
        return WpfShape.Of(_geometry);
    }
}

/// <summary>Words measured by the same cache every WPF builder measures words by.</summary>
internal sealed class WpfMeasure(TextWidths.In widths) : TextMeasure
{
    public override (double Width, double Advance) Of(ReadOnlySpan<char> text) => widths.Of(text);
}

/// <summary>One line's characters, in one face at one size, as WPF's type engine reads them to set a line.</summary>
internal sealed class OneRun(string text, TextRunProperties run) : TextSource
{
    public override TextRun GetTextRun(int textSourceCharacterIndex) =>
        textSourceCharacterIndex < text.Length
            ? new TextCharacters(text, textSourceCharacterIndex, text.Length - textSourceCharacterIndex, run)
            : new TextEndOfParagraph(1);

    public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit) =>
        new(textSourceCharacterIndexLimit,
            new CultureSpecificCharacterBufferRange(run.CultureInfo, new CharacterBufferRange(text, 0, textSourceCharacterIndexLimit)));

    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex) => textSourceCharacterIndex;
}

/// <summary>How a line's characters are set: one face, one size, in no colour of their own — only where they stand is asked.</summary>
internal sealed class RunSetting : TextRunProperties
{
    private readonly Typeface _face;
    private readonly double _size;
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public RunSetting(Typeface face, double size)
    {
        _face = face;
        _size = size;
        PixelsPerDip = WpfMedium.Density;
    }

    public override Typeface Typeface => _face;
    public override double FontRenderingEmSize => _size;
    public override double FontHintingEmSize => _size;
    public override TextDecorationCollection? TextDecorations => null;
    public override Brush ForegroundBrush => Brushes.Black;
    public override Brush? BackgroundBrush => null;
    public override CultureInfo CultureInfo => _culture;
    public override TextEffectCollection? TextEffects => null;
}

/// <summary>A line on its own: left to right, unwrapped, the first of its paragraph.</summary>
internal sealed class LineSetting(TextRunProperties run) : TextParagraphProperties
{


    public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
    public override TextAlignment TextAlignment => TextAlignment.Left;
    public override double LineHeight => 0;
    public override bool FirstLineInParagraph => true;
    public override TextRunProperties DefaultTextRunProperties => run;
    public override TextWrapping TextWrapping => TextWrapping.NoWrap;
    public override TextMarkerProperties? TextMarkerProperties => null;
    public override double Indent => 0;
    public override double DefaultIncrementalTab => 4 * run.FontRenderingEmSize;
}

/// <summary>One line set by WPF's type engine, asked where its letters stand.</summary>
internal sealed class WpfLine(System.Windows.Media.TextFormatting.TextLine line) : ShapedLine
{
    public override double Height => line.Height;

    public override LayoutRect Letter(int index)
    {
        var bounds = Rect.Empty;
        foreach (var piece in line.GetTextBounds(index, 1))
        {
            var box = piece.Rectangle;
            if (box.IsEmpty || box.Width <= 0 || box.Height <= 0) continue;
            bounds = bounds.IsEmpty ? box : Rect.Union(bounds, box);
        }

        return bounds.ToLayout();
    }

    public override void Dispose() => line.Dispose();
}

/// <summary>A picture a host handed over, as the image WPF draws.</summary>
public sealed class WpfPicture(ImageSource picture) : LayoutPicture
{
    public ImageSource Source { get; } = picture;

    public override double Width => Source.Width;

    public override double Height => Source.Height;

    public override LayoutPixels? Pixels(double widest)
    {
        if (Source is not BitmapSource picture) return null;

        if (picture.PixelWidth > widest)
            picture = new TransformedBitmap(picture, new ScaleTransform(widest / picture.PixelWidth, widest / picture.PixelWidth));

        var square = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        var stride = square.PixelWidth * 4;
        var pixels = new byte[stride * square.PixelHeight];
        square.CopyPixels(pixels, stride, 0);

        return new LayoutPixels(pixels, square.PixelWidth, square.PixelHeight);
    }
}

/// <summary>
/// A pen, built and frozen the first time it is painted — not when it is laid, since most of what is laid is never on screen —
/// with its own ink where it has one, and otherwise for the last ink it was handed, which is the theme's and hardly ever changes.
/// </summary>
public sealed class WpfStroke : LayoutStroke
{
    private readonly WpfInk? _ink;
    private readonly double _thickness;
    private readonly DoubleCollection? _dashes;
    private readonly StrokeEnds? _ends;
    private (Brush Ink, Pen Pen)? _built;

    public WpfStroke(WpfInk? ink, double thickness, DoubleCollection? dashes, StrokeEnds? ends)
    {
        _ink = ink;
        _thickness = thickness;
        _dashes = dashes;
        _ends = ends;
    }

    public override LayoutInk? Ink => _ink;

    public override double Thickness => _thickness;

    /// <summary>The dashes it is broken into, or null for a solid line.</summary>
    public DoubleCollection? Dashes => _dashes;

    /// <summary>The pen it strokes with where it is handed <paramref name="fallback"/> for an ink it has none of.</summary>
    public Pen PenFor(Brush fallback)
    {
        var ink = _ink?.Brush ?? fallback;
        if (_built is { } built && ReferenceEquals(built.Ink, ink)) return built.Pen;

        var pen = Built(ink);
        _built = (ink, pen);
        return pen;
    }

    private Pen Built(Brush ink)
    {
        var pen = new Pen(ink, _thickness);

        if (_ends is { } ends)
        {
            pen.StartLineCap = (PenLineCap)ends.StartCap;
            pen.EndLineCap = (PenLineCap)ends.EndCap;
            pen.DashCap = (PenLineCap)ends.DashCap;
            pen.LineJoin = (PenLineJoin)ends.Join;
            pen.MiterLimit = ends.MiterLimit;
        }

        if (_dashes is { Count: > 0 }) pen.DashStyle = new DashStyle(_dashes, _ends?.DashOffset ?? 0);
        pen.Freeze();

        return pen;
    }

    /// <summary>How its ends, corners and dashes are drawn, or null for a pen's own defaults.</summary>
    public StrokeEnds? Ends => _ends;
}

/// <summary>A turn, as the frozen transform WPF pushes.</summary>
public sealed class WpfTurn : LayoutTurn
{
    public WpfTurn(LayoutMatrix matrix)
    {
        var transform = new MatrixTransform(matrix.ToWpf());
        transform.Freeze();
        Transform = transform;
        Matrix = matrix;
    }

    public WpfTurn(Transform transform)
    {
        Transform = transform.IsFrozen ? transform : (Transform)transform.GetAsFrozen();
        Matrix = Transform.Value.ToLayout();
    }

    public Transform Transform { get; }

    public override LayoutMatrix Matrix { get; }
}

/// <summary>The pixel grid a piece's edges land on, as WPF's guideline set.</summary>
public sealed class WpfGuides(GuidelineSet guides) : LayoutGuides
{
    public GuidelineSet Set { get; } = Frozen(guides);

    private static GuidelineSet Frozen(GuidelineSet guides)
    {
        if (!guides.IsFrozen) guides.Freeze();
        return guides;
    }
}
