using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing.Portable;

/// <summary>
/// A laid tree taken apart into the calls that made it, so the same tree can be made again two ways — into the WPF tree with
/// WPF's own marks, and into the renderer-free tree with a medium's handles — doing the same work in the same order.
///
/// <para>
/// What either way costs over the other is then what the abstraction costs, measured on every piece, mark, word and outline
/// real documents lay, rather than argued from a sample. Everything a theme or a host holds before a builder runs — its
/// brushes, faces, pictures — is resolved once when the recipe is taken, as a style resolves them once; everything a builder
/// makes as it lays — words set, glyphs placed, outlines drawn, pens and turns — is made again on every replay, both ways.
/// </para>
/// </summary>
internal sealed class LaidReplay
{
    private readonly Step[] _steps;
    private readonly (int[] Members, bool Vertical)[] _runs;
    private readonly WpfMedium _medium;

    /// <summary>What the recipe could not take as it was laid — counted, so a difference in the picture can be put down to it.</summary>
    public IReadOnlyDictionary<string, int> Unfaithful => _unfaithful;

    private readonly Dictionary<string, int> _unfaithful = [];

    public int Pieces => _steps.Length;

    public int Marks => _steps.Sum(step => step.Marks.Length);

    public IReadOnlyDictionary<string, int> MarkKinds =>
        _steps.SelectMany(step => step.Marks).GroupBy(mark => mark.Name).ToDictionary(kind => kind.Key, kind => kind.Count());

    private LaidReplay(WpfMedium medium, Step[] steps, (int[] Members, bool Vertical)[] runs)
    {
        _medium = medium;
        _steps = steps;
        _runs = runs;
    }

    public static LaidReplay Of(LayoutTree tree, WpfMedium medium)
    {
        var taking = new Taking(medium);
        var steps = new Step[tree.Count];

        for (var at = 0; at < tree.Count; at++)
        {
            ref readonly var stored = ref tree.Piece(at);
            var paint = tree.PaintOf(at);

            steps[at] = new Step(
                tree.KindOf(at), tree.PartOf(at), new Point(stored.Offset.X, stored.Offset.Y), stored.Stops, stored.Box,
                stored.Extent, paint is null ? null : taking.Paint(paint),
                [.. tree.MarksOf(at).ToArray().Select(taking.Mark)],
                tree.RegionOf(at) is { } region ? taking.Outline(region) : null,
                tree.WordsOf(at) is { } words ? new WordsRecipe(taking.Text(words.Glyphs, words.At, null), words.Maps, words.Writes) : null);
        }

        var runs = new (int[] Members, bool Vertical)[tree.RunCount];
        for (var run = 0; run < tree.RunCount; run++)
        {
            var members = tree.Run(run).ToArray();
            runs[run] = (members, members.Length > 0 && tree.RunOf(members[0], vertical: true) == run);
        }

        var replay = new LaidReplay(medium, steps, runs);
        foreach (var (what, count) in taking.Unfaithful) replay._unfaithful[what] = count;

        return replay;
    }

    // ── Made again, the WPF way ────────────────────────────────────────────────

    public LayoutTree Wpf()
    {
        var build = new LayoutBuilder(_steps.Length);
        var texts = new Dictionary<object, FormattedText>(ReferenceEqualityComparer.Instance);

        for (var at = 0; at < _steps.Length;) at = Wpf(build, at, texts);
        foreach (var (members, vertical) in _runs) build.Runs(members, vertical);

        return build.Seal();
    }

    private int Wpf(LayoutBuilder build, int at, Dictionary<object, FormattedText> texts)
    {
        var step = _steps[at];
        var paint = step.Paint?.Wpf();

        build.Open(step.Kind, step.Part, step.At, step.Stops, gathers: paint is not { Applies: true }, paints: paint);
        build.Covers(step.Box);

        foreach (var mark in step.Marks) build.Draw(mark.Wpf(texts));
        if (step.Region is { } region) build.Occupies(region.Wpf());
        if (step.Words is { } words) build.Words(new LayoutWords(words.Text.Set(texts), words.Text.At, words.Maps, words.Writes));

        var next = at + 1;
        while (next < at + step.Extent) next = Wpf(build, next, texts);

        build.Close();
        return next;
    }

    // ── Made again, through the medium ─────────────────────────────────────────

    public PortableTree Portable()
    {
        ILayoutMedium medium = _medium;
        var build = new PortableBuilder(medium, _steps.Length);
        var texts = new Dictionary<object, IShapedText>(ReferenceEqualityComparer.Instance);

        for (var at = 0; at < _steps.Length;) at = Portable(build, medium, at, texts);
        foreach (var (members, vertical) in _runs) build.Runs(members, vertical);

        return build.Seal();
    }

    private int Portable(PortableBuilder build, ILayoutMedium medium, int at, Dictionary<object, IShapedText> texts)
    {
        var step = _steps[at];
        var paint = step.Paint?.Portable(medium);

        build.Open(step.Kind, step.Part, new LayoutPoint(step.At.X, step.At.Y), (PortableStops)step.Stops,
                   gathers: paint is not { Applies: true }, paints: paint);
        build.Covers(step.Box.ToLayout());

        foreach (var mark in step.Marks) build.Draw(mark.Portable(medium, texts));
        if (step.Region is { } region) build.Occupies(region.Portable(medium));
        if (step.Words is { } words)
            build.Words(new PortableWords(words.Text.Shaped(medium, texts), new LayoutPoint(words.Text.At.X, words.Text.At.Y),
                                          words.Maps, words.Writes));

        var next = at + 1;
        while (next < at + step.Extent) next = Portable(build, medium, next, texts);

        build.Close();
        return next;
    }

    // ── The recipe ─────────────────────────────────────────────────────────────

    private sealed record Step(string Kind, ISourcePart? Part, Point At, Stops Stops, Rect Box, int Extent, PaintRecipe? Paint,
                               MarkRecipe[] Marks, OutlineRecipe? Region, WordsRecipe? Words);

    private sealed record WordsRecipe(TextRecipe Text, bool Maps, bool Writes);

    /// <summary>What a theme and a host hold before anything is laid: resolved once, both ways.</summary>
    private sealed class Taking(WpfMedium medium)
    {
        private readonly Dictionary<Brush, (Brush Wpf, WpfInk Ink)> _inks = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Typeface, WpfFace> _faces = [];
        private readonly Dictionary<GlyphTypeface, WpfFace> _fonts = [];
        private readonly Dictionary<FormattedText, TextRecipe> _texts = new(ReferenceEqualityComparer.Instance);

        public readonly Dictionary<string, int> Unfaithful = [];

        public (Brush Wpf, WpfInk Ink)? Ink(Brush? brush)
        {
            if (brush is null) return null;
            if (_inks.TryGetValue(brush, out var known)) return known;

            return _inks[brush] = (brush, medium.Ink(brush));
        }

        public WpfFace Face(Typeface face) => _faces.TryGetValue(face, out var known) ? known : _faces[face] = medium.Face(face);

        public WpfFace Font(GlyphTypeface font) => _fonts.TryGetValue(font, out var known) ? known : _fonts[font] = new WpfFace(font);

        private void Missed(string what) => Unfaithful[what] = Unfaithful.GetValueOrDefault(what) + 1;

        public MarkRecipe Mark(LayoutMark mark) => mark switch
        {
            TextMark text => Text(text.Glyphs, text.At, text.Foreground),
            GlyphMark glyphs => Glyphs(glyphs),
            GeometryMark shape => new ShapeRecipe(Outline(shape.Shape), Ink(shape.Fill), Ink(shape.Stroke), shape.Thickness,
                                                  shape.Dashes?.ToArray(), shape.Outline),
            RuleMark rule => new RuleRecipe(rule.Bounds, Ink(rule.Foreground)),
            LineMark line => new LineRecipe(line.From, line.To, Ink(line.Foreground)),
            WashMark wash => new WashRecipe(wash.Bounds, Ink(wash.Fill)!.Value),
            PictureMark picture => new PictureRecipe(picture.Picture, medium.Picture(picture.Picture), picture.Bounds),
            _ => throw new NotSupportedException(mark.GetType().Name),
        };

        public TextRecipe Text(FormattedText set, Point at, Brush? foreground)
        {
            if (_texts.TryGetValue(set, out var known)) return known with { At = at, Ink = Ink(foreground) };

            var (run, spans) = Run(set);
            if (spans > 1) Missed("words set in more than one run");

            var lines = TextLines.None;
            foreach (var line in run.TextDecorations ?? [])
                lines |= line.Location switch
                {
                    TextDecorationLocation.Underline => TextLines.Under,
                    TextDecorationLocation.Strikethrough => TextLines.Through,
                    _ => TextLines.None,
                };

            if (run.TextDecorations is { Count: > 0 } drawn && !drawn.All(line => line.Pen is null))
                Missed("words ruled with a pen of their own");

            if (!Equals(run.CultureInfo, CultureInfo.CurrentCulture)) Missed("words set in another culture");

            var recipe = new TextRecipe(set.Text, run.Typeface, Face(run.Typeface), run.FontRenderingEmSize, Ink(run.ForegroundBrush)!.Value,
                                        run.TextDecorations, lines, run.CultureInfo, set.MaxTextWidth, set.TextAlignment, at, Ink(foreground))
            {
                Key = new object(),
            };

            return _texts[set] = recipe;
        }

        private MarkRecipe Glyphs(GlyphMark mark)
        {
            var run = mark.Run;
            if (run.BidiLevel != 0 || run.IsSideways) Missed("glyphs set sideways or right to left");

            return new GlyphsRecipe(run.GlyphTypeface, Font(run.GlyphTypeface), run.FontRenderingEmSize, run.BaselineOrigin,
                                    [.. run.GlyphIndices], [.. run.AdvanceWidths], run.GlyphOffsets?.ToArray(),
                                    run.GlyphOffsets?.Select(offset => new LayoutVector(offset.X, offset.Y)).ToArray(), Ink(mark.Foreground));
        }

        public PaintRecipe Paint(LayoutPaint paint)
        {
            if (paint.Kept is { } && paint.Kept.Holds) Missed("a picture already kept");

            return new PaintRecipe([.. (paint.Turn ?? []).Select(turn => turn.Value)],
                                   paint.Snap is { } snap ? ([.. snap.GuidelinesX], [.. snap.GuidelinesY]) : null,
                                   paint.Kept is not null, paint.Scale, paint.Clip is { } clip ? Outline(clip) : null,
                                   paint.Opacity, Ink(paint.Mask));
        }

        /// <summary>An outline as the figures it is made of, its own transform worked in — what a builder draws it as.</summary>
        public OutlineRecipe Outline(Geometry shape)
        {
            var path = PathGeometry.CreateFromGeometry(shape);
            var placing = path.Transform?.Value ?? Matrix.Identity;
            var steps = new List<Draw>();

            Point Placed(Point point) => placing.IsIdentity ? point : placing.Transform(point);

            foreach (var figure in path.Figures)
            {
                steps.Add(new Draw(DrawKind.Begin, Placed(figure.StartPoint), default, default, figure.IsFilled, figure.IsClosed));

                foreach (var segment in figure.Segments)
                    switch (segment)
                    {
                        case LineSegment line:
                            steps.Add(new Draw(DrawKind.Line, Placed(line.Point)));
                            break;

                        case PolyLineSegment lines:
                            foreach (var point in lines.Points) steps.Add(new Draw(DrawKind.Line, Placed(point)));
                            break;

                        case BezierSegment curve:
                            steps.Add(new Draw(DrawKind.Bezier, Placed(curve.Point1), Placed(curve.Point2), Placed(curve.Point3)));
                            break;

                        case PolyBezierSegment curves:
                            for (var at = 0; at + 2 < curves.Points.Count; at += 3)
                                steps.Add(new Draw(DrawKind.Bezier, Placed(curves.Points[at]), Placed(curves.Points[at + 1]),
                                                   Placed(curves.Points[at + 2])));
                            break;

                        case QuadraticBezierSegment curve:
                            steps.Add(new Draw(DrawKind.Quadratic, Placed(curve.Point1), Placed(curve.Point2)));
                            break;

                        case PolyQuadraticBezierSegment curves:
                            for (var at = 0; at + 1 < curves.Points.Count; at += 2)
                                steps.Add(new Draw(DrawKind.Quadratic, Placed(curves.Points[at]), Placed(curves.Points[at + 1])));
                            break;

                        case ArcSegment arc:
                            if (!placing.IsIdentity) Missed("an arc in a transformed outline");
                            steps.Add(new Draw(DrawKind.Arc, Placed(arc.Point), new Point(arc.Size.Width, arc.Size.Height),
                                               new Point(arc.RotationAngle, 0), arc.IsLargeArc, arc.SweepDirection == SweepDirection.Clockwise));
                            break;
                    }
            }

            return new OutlineRecipe([.. steps], path.FillRule == FillRule.EvenOdd);
        }

        private static readonly FieldInfo FormatRuns =
            typeof(FormattedText).GetField("_formatRuns", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly FieldInfo DefaultParagraph =
            typeof(FormattedText).GetField("_defaultParaProps", BindingFlags.NonPublic | BindingFlags.Instance)!;

        /// <summary>
        /// What a run of words was set with — read back from WPF's own record of it, since formatted text says nothing of its
        /// face. A run formatted after it was made carries that in its spans; one never touched, in its paragraph's defaults.
        /// </summary>
        private static (TextRunProperties Run, int Spans) Run(FormattedText set)
        {
            var spans = FormatRuns.GetValue(set)!;
            var count = (int)spans.GetType().GetProperty("Count")!.GetValue(spans)!;

            if (count > 0)
            {
                var first = spans.GetType().GetProperty("Item")!.GetValue(spans, [0])!;
                if (first.GetType().GetField("element", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(first)
                    is TextRunProperties formatted)
                    return (formatted, count);
            }

            return (((TextParagraphProperties)DefaultParagraph.GetValue(set)!).DefaultTextRunProperties, count);
        }
    }

    private enum DrawKind : byte { Begin, Line, Bezier, Quadratic, Arc }

    /// <summary>One step of an outline. An arc keeps its radii in <see cref="B"/> and its turn in <see cref="C"/>'s X.</summary>
    private readonly record struct Draw(DrawKind Kind, Point A, Point B = default, Point C = default, bool Flag = false, bool Other = false);

    private sealed record OutlineRecipe(Draw[] Steps, bool EvenOdd)
    {
        public Geometry Wpf()
        {
            var shape = new StreamGeometry { FillRule = EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero };

            using (var into = shape.Open())
                foreach (var step in Steps)
                    switch (step.Kind)
                    {
                        case DrawKind.Begin: into.BeginFigure(step.A, step.Flag, step.Other); break;
                        case DrawKind.Line: into.LineTo(step.A, true, false); break;
                        case DrawKind.Bezier: into.BezierTo(step.A, step.B, step.C, true, false); break;
                        case DrawKind.Quadratic: into.QuadraticBezierTo(step.A, step.B, true, false); break;
                        case DrawKind.Arc:
                            into.ArcTo(step.A, new Size(step.B.X, step.B.Y), step.C.X, step.Flag,
                                       step.Other ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
                            break;
                    }

            shape.Freeze();
            return shape;
        }

        public LayoutShape Portable(ILayoutMedium medium)
        {
            var into = medium.Outline(EvenOdd);

            foreach (var step in Steps)
                switch (step.Kind)
                {
                    case DrawKind.Begin: into.Begin(At(step.A), step.Flag, step.Other); break;
                    case DrawKind.Line: into.LineTo(At(step.A)); break;
                    case DrawKind.Bezier: into.BezierTo(At(step.A), At(step.B), At(step.C)); break;
                    case DrawKind.Quadratic: into.QuadraticTo(At(step.A), At(step.B)); break;
                    case DrawKind.Arc: into.ArcTo(At(step.A), step.B.X, step.B.Y, step.C.X, step.Flag, step.Other); break;
                }

            return into.Close();
        }
    }

    private static LayoutPoint At(Point point) => new(point.X, point.Y);

    private sealed record PaintRecipe(Matrix[] Turns, (double[] X, double[] Y)? Snap, bool Keeps, double Scale, OutlineRecipe? Clip,
                                      double Opacity, (Brush Wpf, WpfInk Ink)? Mask)
    {
        /// <summary>As a WPF builder makes one: every turn and grid frozen once it is made, as the typesetter freezes them.</summary>
        public LayoutPaint Wpf() =>
            new(Turns.Length == 0 ? null : [.. Turns.Select(Turned)], Snap is { } snap ? Snapped(snap.X, snap.Y) : null,
                Keeps ? new LayoutKept() : null, Scale, Clip?.Wpf(), Opacity, Mask?.Wpf);

        private static Transform Turned(Matrix turn)
        {
            var turned = new MatrixTransform(turn);
            turned.Freeze();
            return turned;
        }

        private static GuidelineSet Snapped(double[] across, double[] down)
        {
            var snapped = new GuidelineSet(across, down);
            snapped.Freeze();
            return snapped;
        }

        public PortablePaint Portable(ILayoutMedium medium) =>
            new(Turns.Length == 0 ? null : [.. Turns.Select(turn => medium.Turn(turn.ToLayout()))],
                Snap is { } snap ? medium.Guides(snap.X, snap.Y) : null, Keeps ? new PortableKept() : null, Scale, Clip?.Portable(medium),
                Opacity, Mask?.Ink);
    }

    private abstract record MarkRecipe
    {
        public abstract string Name { get; }

        public abstract LayoutMark Wpf(Dictionary<object, FormattedText> texts);

        public abstract PortableMark Portable(ILayoutMedium medium, Dictionary<object, IShapedText> texts);
    }

    private sealed record TextRecipe(string Text, Typeface Typeface, WpfFace Face, double Size, (Brush Wpf, WpfInk Ink) Run,
                                     TextDecorationCollection? Decorations, TextLines Lines, CultureInfo Culture, double Room,
                                     TextAlignment Align, Point At, (Brush Wpf, WpfInk Ink)? Ink) : MarkRecipe
    {
        public override string Name => "words";

        /// <summary>The words set as a WPF builder sets them, once however many marks and runs share them.</summary>
        public FormattedText Set(Dictionary<object, FormattedText> texts)
        {
            if (texts.TryGetValue(Key, out var known)) return known;

            var set = new FormattedText(Text, Culture, FlowDirection.LeftToRight, Typeface, Size, Run.Wpf, LayoutText.Density);
            if (Decorations is not null) set.SetTextDecorations(Decorations);
            if (Room > 0 && set.MaxTextWidth != Room) set.MaxTextWidth = Room;
            if (set.TextAlignment != Align) set.TextAlignment = Align;

            return texts[Key] = set;
        }

        public IShapedText Shaped(ILayoutMedium medium, Dictionary<object, IShapedText> texts)
        {
            if (texts.TryGetValue(Key, out var known)) return known;

            var set = medium.Shape(Text, Face, Size, Run.Ink, Lines);
            set.Bound(Room > 0 ? Room : double.PositiveInfinity, Align switch
            {
                TextAlignment.Right => LayoutAlign.Right,
                TextAlignment.Center => LayoutAlign.Centre,
                TextAlignment.Justify => LayoutAlign.Justify,
                _ => LayoutAlign.Left,
            });

            return texts[Key] = set;
        }

        /// <summary>What every recipe taken from one run of words shares, so the run is set once however many marks and runs hold it.</summary>
        public required object Key { get; init; }

        public override LayoutMark Wpf(Dictionary<object, FormattedText> texts) => new TextMark(Set(texts), At, Ink?.Wpf);

        public override PortableMark Portable(ILayoutMedium medium, Dictionary<object, IShapedText> texts) =>
            new PortableTextMark(Shaped(medium, texts), new LayoutPoint(At.X, At.Y), Ink?.Ink);
    }

    private sealed record GlyphsRecipe(GlyphTypeface Font, WpfFace Face, double Size, Point Origin, ushort[] Indices, double[] Advances,
                                       Point[]? Offsets, LayoutVector[]? Shifts, (Brush Wpf, WpfInk Ink)? Ink) : MarkRecipe
    {
        public override string Name => "glyphs";

        public override LayoutMark Wpf(Dictionary<object, FormattedText> texts) =>
            new GlyphMark(new GlyphRun(Font, 0, false, Size, (float)LayoutText.Density, Indices, Origin, Advances, Offsets,
                                       null, null, null, null, null), Ink?.Wpf);

        public override PortableMark Portable(ILayoutMedium medium, Dictionary<object, IShapedText> texts)
            => new PortableGlyphMark(medium.Glyphs(Face, Size, At(Origin), Indices, Advances, Shifts), Ink?.Ink);
    }

    private sealed record ShapeRecipe(OutlineRecipe Outline, (Brush Wpf, WpfInk Ink)? Fill, (Brush Wpf, WpfInk Ink)? Stroke,
                                      double Thickness, double[]? Dashes, LayoutPen? Ends) : MarkRecipe
    {
        public override string Name => "outline";

        public override LayoutMark Wpf(Dictionary<object, FormattedText> texts) =>
            new GeometryMark(Outline.Wpf(), Fill?.Wpf, Stroke?.Wpf, Thickness)
            {
                Dashes = Dashes is null ? null : new DoubleCollection(Dashes),
                Outline = Ends,
            };

        public override PortableMark Portable(ILayoutMedium medium, Dictionary<object, IShapedText> texts)
        {
            var stroke = Stroke is not null || Thickness > 0
                ? medium.Stroke(Stroke?.Ink, Thickness, Dashes, Ends is { } ends
                    ? new StrokeEnds((StrokeCap)ends.StartCap, (StrokeCap)ends.EndCap, (StrokeCap)ends.DashCap,
                                     (StrokeJoin)ends.Join, ends.MiterLimit, ends.DashOffset)
                    : null)
                : null;

            return new PortableGeometryMark(Outline.Portable(medium), Fill?.Ink, stroke);
        }
    }

    private sealed record RuleRecipe(Rect Bounds, (Brush Wpf, WpfInk Ink)? Ink) : MarkRecipe
    {
        public override string Name => "rule";

        public override LayoutMark Wpf(Dictionary<object, FormattedText> texts) => new RuleMark(Bounds, Ink?.Wpf);

        public override PortableMark Portable(ILayoutMedium medium, Dictionary<object, IShapedText> texts) =>
            new PortableRuleMark(Bounds.ToLayout(), Ink?.Ink);
    }

    private sealed record LineRecipe(Point From, Point To, (Brush Wpf, WpfInk Ink)? Ink) : MarkRecipe
    {
        public override string Name => "line";

        public override LayoutMark Wpf(Dictionary<object, FormattedText> texts) => new LineMark(From, To, Ink?.Wpf);

        public override PortableMark Portable(ILayoutMedium medium, Dictionary<object, IShapedText> texts) =>
            new PortableLineMark(At(From), At(To), Ink?.Ink);
    }

    private sealed record WashRecipe(Rect Bounds, (Brush Wpf, WpfInk Ink) Fill) : MarkRecipe
    {
        public override string Name => "wash";

        public override LayoutMark Wpf(Dictionary<object, FormattedText> texts) => new WashMark(Bounds, Fill.Wpf);

        public override PortableMark Portable(ILayoutMedium medium, Dictionary<object, IShapedText> texts) =>
            new PortableWashMark(Bounds.ToLayout(), Fill.Ink);
    }

    private sealed record PictureRecipe(ImageSource Source, LayoutPicture Picture, Rect Bounds) : MarkRecipe
    {
        public override string Name => "picture";

        public override LayoutMark Wpf(Dictionary<object, FormattedText> texts) => new PictureMark(Source, Bounds);

        public override PortableMark Portable(ILayoutMedium medium, Dictionary<object, IShapedText> texts) =>
            new PortablePictureMark(Picture, Bounds.ToLayout());
    }
}
