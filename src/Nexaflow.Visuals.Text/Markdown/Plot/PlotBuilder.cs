using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Plot;
using Nexaflow.Markdown.Settings;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Plot;

/// <summary>
/// Draws a plot block on the shared layout tree.
///
/// <para>
/// Almost none of the drawing is here. Marks are <see cref="DiagramShapes"/>, axes and their numbers are
/// <see cref="DiagramAxis"/> and <see cref="DiagramScale"/>, colour is <see cref="DiagramInk"/> and the
/// key is <see cref="DiagramLegend"/> — the same kit every Mermaid diagram is built from, which is why a
/// plot looks like the rest of the app without being told to. What is left is the arithmetic of turning
/// a value into a place, and the order the layers go down in.
/// </para>
/// <para>
/// Every mark carries the part it was drawn from, so a press means the row it came from and a drag
/// through the numbers picks out the characters somebody typed.
/// </para>
/// </summary>
internal sealed class PlotBuilder : ContentBuilder
{
    /// <summary>The face the source is shown in when the block cannot be read as a plot at all.</summary>
    private static readonly FontFamily SourceFont = new("Cascadia Code, Consolas, monospace");

    private const double SourceSize = 12;

    private static readonly FontFamily WordFont = new("Segoe UI, Arial, sans-serif");

    private const double Gap = 6;
    private const double LabelSize = 11.5;
    private const double TitleSize = 15;

    /// <summary>The least room the panel itself is given, however little is left after the gutters.</summary>
    private const double Smallest = 40;

    /// <summary>
    /// How big a mark is drawn where the rows are shown <em>over</em> something else — a density or a
    /// binning. Small and faint on purpose: a row over a cloud is there to say the cloud is made of rows,
    /// and at the number of rows that make a cloud worth drawing, marks at their usual size are a solid
    /// field with the cloud nowhere to be seen under it.
    /// </summary>
    private const double Over = 1.6;

    /// <summary>And how much of what is under one shows through it.</summary>
    private const double Through = 0.25;

    private readonly MarkdownPalette _palette;
    private readonly DiagramInk _ink;
    private readonly PlotFence _fence;
    private readonly double _room;
    private readonly double _dpi;

    private PlotBuilder(string source, PlotFence fence, MarkdownPalette palette, double room, double pixelsPerDip)
        : base(source)
    {
        _fence = fence;
        _palette = palette;
        _ink = new DiagramInk(palette);
        _room = room;
        _dpi = pixelsPerDip;
    }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(string source, PlotFence fence, MarkdownPalette palette, double room,
                             double pixelsPerDip) =>
        new PlotBuilder(source, fence, palette, room, pixelsPerDip).Lay();

    /// <summary>
    /// The element a plot is shown in. Editable, because every number in it is a number somebody typed.
    /// </summary>
    public static Editing.ContentElement Element(string source, PlotFence fence, DiagramRenderOptions options) =>
        new Editing.ContentElement(source, options.Palette,
            (state, room, pixelsPerDip) => Build(state.Source, fence, options.Palette, room, pixelsPerDip))
        {
            // Where the block's lines sit inside the fence that produced them, so an edit to a value is
            // spliced back where it came from.
            SourceStart = options.SourceOffset,
            SourceLength = source.Length,

            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 10),
        };

    protected override Laid? Read()
    {
        var tree = PlotPipeline.Read(Source, _fence, out var settings, out var error);
        if (error is not null) return Stopped(error);

        var chart = PlotChart.Of(ContentReading.Of(tree).Root, settings);

        if (chart.Marks.Count == 0)
            return Stopped("An empty plot. It takes a row of values for each point — two columns for where "
                         + "it goes — and settings written as `key: value` above them.");

        return Lay(chart);
    }

    // ── Turning a value into a place ────────────────────────────────────────

    /// <summary>
    /// An axis: which channel it reads, what is written along it, where a value stands on it from nought to
    /// one, and how much of it one value takes up — which is how wide a tile is drawn.
    /// </summary>
    /// <param name="Channel">
    /// Carried because a flipped plot reads up the page what it would otherwise read across. Everything that
    /// places a mark asks the axis which channel to take, so nothing else has to know the axes were swapped.
    /// </param>
    private sealed record Placing(PlotAesthetic Channel, IReadOnlyList<DiagramTick> Ticks,
                                  Func<PlotValue?, double?> At, double Slot);

    /// <summary>
    /// How a channel is laid along its axis: numbered where its values are numbers, and a slot per name
    /// where they are names — the same division ggplot2 makes between a continuous and a discrete scale.
    /// </summary>
    private Placing Along(PlotChart chart, PlotAesthetic channel, PlotScale scale,
                          (double Min, double Max)? limits, IReadOnlyList<double>? breaks, bool slots,
                          (double Min, double Max)? widened = null)
    {
        if (!slots && chart.Counts(channel) && (limits ?? Both(chart.Reach(channel), widened)) is { } reach)
        {
            // Limits the block wrote are the ends it asked for, so they are not opened out to round numbers.
            var span = DiagramSpan.Of(reach.Min, reach.Max, Transform(scale), widen: limits is null);

            var marks = breaks is null
                ? span.Ticks()
                : [.. breaks.Select(value => (Value: value, At: span.At(value), Says: DiagramScale.Plain(value)))
                            .Where(tick => tick.At is not null and >= -1e-9 and <= 1 + 1e-9)
                            .Select(tick => (tick.Value, At: tick.At!.Value, tick.Says))];

            var ticks = marks.Select(tick => new DiagramTick(
                                 tick.At, this.Worked(tick.Says, null, LabelSize, _palette.TextMuted)))
                             .ToList();

    return new Placing(channel, ticks, value => value?.Number is { } number ? span.At(number) : null, 1);
        }

        var names = slots ? chart.Slots(channel) : chart.Named(channel);
    if (names.Count == 0) return new Placing(channel, [], _ => null, 1);

        // A value stands in the middle of its own slot, so the first and last are inside the panel rather
        // than on its edges — and the slot is exactly how wide a tile is drawn, so a row of them fills the
        // panel once and no more.
        var places = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var at = 0; at < names.Count; at++) places[names[at]] = (at + 0.5) / names.Count;

        var marked = names.Select(name => new DiagramTick(
                              places[name], this.Worked(name, null, LabelSize, _palette.TextMuted)))
                          .ToList();

        return new Placing(channel, marked,
                value => value is not null && places.TryGetValue(value.Text, out var place) ? place : null,
                1.0 / names.Count);
    }

    /// <summary>The plain span a channel's numbers make, before anything drawn over them widens it.</summary>
    private static DiagramSpan Spanned(PlotChart chart, PlotAesthetic channel, PlotScale scale,
                                       (double Min, double Max)? limits)
    {
        var reach = limits ?? chart.Reach(channel) ?? (Min: 0.0, Max: 1.0);

        return DiagramSpan.Of(reach.Min, reach.Max, Transform(scale), widen: limits is null);
    }

    /// <summary>Both reaches together, so what is drawn over an axis is shown by it.</summary>
    private static (double Min, double Max)? Both((double Min, double Max)? one, (double Min, double Max)? other)
    {
        if (one is null) return other;
        if (other is null) return one;

        return (Math.Min(one.Value.Min, other.Value.Min), Math.Max(one.Value.Max, other.Value.Max));
    }

    /// <summary>How the kit reads an axis the block asked for.</summary>
    private static DiagramTransform Transform(PlotScale scale) => scale switch
    {
        PlotScale.Log10 => DiagramTransform.Log10,
        PlotScale.Log2 => DiagramTransform.Log2,
        PlotScale.Ln => DiagramTransform.NaturalLog,
        PlotScale.Sqrt => DiagramTransform.Sqrt,
        PlotScale.Reverse => DiagramTransform.Reverse,
        _ => DiagramTransform.Linear,
    };

    /// <summary>
    /// Which mark a point is drawn as: the glyph its group takes where a column feeds shape, the one the
    /// block named where it named one, and a circle otherwise.
    /// </summary>
    private DiagramGlyph Glyph(PlotChart chart, PlotMark mark, IReadOnlyDictionary<string, int> shapes)
    {
        if (shapes.Count == 0) return DiagramGlyphs.Named(chart.Settings.Shape) ?? DiagramGlyph.Circle;

        return mark[PlotAesthetic.Shape] is { } value && shapes.TryGetValue(value.Text, out var at)
            ? DiagramGlyphs.At(at)
            : DiagramGlyph.Circle;
    }

    /// <summary>
    /// How big a mark is drawn. Spread over area rather than radius, because a circle of twice the radius
    /// carries four times the ink and it is the ink a reader reads — which is what ggplot2's own size
    /// scale does.
    /// </summary>
    private static double Radius(PlotChart chart, PlotMark mark)
    {
        if (!chart.Counts(PlotAesthetic.Size)
            || chart.Reach(PlotAesthetic.Size) is not { } reach
            || mark[PlotAesthetic.Size]?.Number is not { } number)
            return PlotSettings.PlainSize;

        var share = reach.Max - reach.Min <= 0 ? 1 : (number - reach.Min) / (reach.Max - reach.Min);

        var min = chart.Settings.MinSize;
        var max = chart.Settings.MaxSize;

        return Math.Sqrt((min * min) + (share * ((max * max) - (min * min))));
    }

    /// <summary>
    /// How see-through a mark is drawn: spread over the alpha channel where a column feeds it, and the
    /// plainest where none does.
    /// </summary>
    private static double Clearness(PlotChart chart, PlotMark mark, bool over)
    {
        var it = chart.Settings;

        if (over) return Through;

        if (!chart.Counts(PlotAesthetic.Alpha)
            || chart.Reach(PlotAesthetic.Alpha) is not { } reach
            || mark[PlotAesthetic.Alpha]?.Number is not { } number)
            return it.MaxAlpha;

        var share = reach.Max - reach.Min <= 0 ? 1 : (number - reach.Min) / (reach.Max - reach.Min);

        return it.MinAlpha + (share * (it.MaxAlpha - it.MinAlpha));
    }

    /// <summary>
    /// How far a mark is moved off its place, so rows landing on the same value stop hiding one another.
    ///
    /// <para>
    /// Thrown from where the row was written rather than from the clock, so the same block always draws
    /// the same picture — the rule a word cloud's packing keeps, for the same reason.
    /// </para>
    /// </summary>
    private static (double Across, double Up) Shake(PlotChart chart, PlotMark mark, Rect plot,
                                                    Placing across, Placing up)
    {
        if (chart.Settings.Jitter <= 0) return (0, 0);

        var throws = new Random(mark.Part.Start * 397);

        var wide = chart.Settings.Jitter * across.Slot * plot.Width;
        var tall = chart.Settings.Jitter * up.Slot * plot.Height;

        return (((throws.NextDouble() * 2) - 1) * wide / 2, ((throws.NextDouble() * 2) - 1) * tall / 2);
    }

    /// <summary>
    /// How the marks are coloured: which channel says so, and whether it says it by naming a group or by
    /// standing somewhere along a run of colours.
    /// </summary>
    private sealed record Painting(PlotAesthetic Channel,
                                   IReadOnlyList<string> Groups,
                                   IReadOnlyDictionary<string, int> Order,
                                   DiagramSpan? Span,
                                   IReadOnlyList<Color> Stops)
    {
        /// <summary>Whether the colour is read as a quantity rather than as one of a few.</summary>
        public bool Counts => this.Span is not null;
    }

    /// <summary>
    /// What colours this chart: fill where anything feeds it, colour otherwise — and along a run of colours
    /// where that channel carries numbers.
    /// </summary>
    private Painting Paint(PlotChart chart, List<Diagnostic> trouble)
    {
        var it = chart.Settings;

        var channel = chart.Marks.Any(mark => mark[PlotAesthetic.Fill] is not null)
            ? PlotAesthetic.Fill
            : PlotAesthetic.Colour;

    // A binned plot colours by how many rows fell in each bin. That is a quantity whatever the columns
            // say — there may be no third column at all — so it always gets a run of colours, and which counts
            // it runs over is settled once the bins are cut.
        if (it.Geom is PlotGeom.Bin2d or PlotGeom.Hex or PlotGeom.Density2d)
                return new Painting(channel, [], Ordered([]), DiagramSpan.Of(1, 2, widen: false), this.Ramp(it, trouble));

        if (!chart.Counts(channel))
                {
                    var groups = chart.Named(channel);
                    return new Painting(channel, groups, Ordered(groups), null, []);
                }

        var reach = it.FillLimits ?? chart.Reach(channel) ?? (Min: 0.0, Max: 1.0);

        // A middle written says the number has a meaningful nought — a correlation, a change, a difference —
        // so the run is made even about it. Otherwise the middle colour would land wherever the values
        // happened to average, and the sign would stop being what the colour means.
        if (it.Midpoint is { } middle)
        {
            var reaches = Math.Max(Math.Abs(reach.Min - middle), Math.Abs(reach.Max - middle));
            if (reaches <= 0) reaches = 1;

            reach = (middle - reaches, middle + reaches);
        }

        var even = it.FillLimits is not null || it.Midpoint is not null;

    return new Painting(channel, [], Ordered([]),
                                DiagramSpan.Of(reach.Min, reach.Max, widen: !even), this.Ramp(it, trouble));
    }

    /// <summary>The run of colours the block asked for, or the one its kind of number is read along.</summary>
    private IReadOnlyList<Color> Ramp(PlotSettings it, List<Diagnostic> trouble)
    {
        // Nothing written: a run out from a middle where there is a middle, and a plain one otherwise.
        if (it.Gradient is null)
            return DiagramColours.Stops(it.Midpoint is null ? DiagramRamp.Viridis : DiagramRamp.BlueRed);

        if (DiagramColours.Named(it.Gradient) is { } ramp) return DiagramColours.Stops(ramp);

        var written = new List<Color>();

        foreach (var colour in it.Gradient.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            if (_ink.Written(colour) is SolidColorBrush read)
            {
                written.Add(read.Color);
                continue;
            }

            trouble.Add(new Diagnostic(0, Math.Max(1, Source.Length), DiagnosticSeverity.Warning,
                                       $"`gradient: {it.Gradient}` names no run of colours — `{colour}` is not "
                                       + $"a colour. The runs are {DiagramColours.Names}."));

            return DiagramColours.Stops(DiagramRamp.Viridis);
        }

        if (written.Count >= 2) return written;

        trouble.Add(new Diagnostic(0, Math.Max(1, Source.Length), DiagnosticSeverity.Warning,
                                   $"`gradient: {it.Gradient}` is one colour, and a run takes at least two."));

        return DiagramColours.Stops(DiagramRamp.Viridis);
    }

    /// <summary>What a mark is drawn in.</summary>
    private Brush Fill(PlotChart chart, PlotMark mark, Painting paint)
    {
        if (paint.Span is { } span)
            return mark[paint.Channel]?.Number is { } number && span.At(number) is { } share
                ? _ink.Scale(paint.Stops, share)
                : _palette.TextMuted;

        var order = mark[paint.Channel] is { } value && paint.Order.TryGetValue(value.Text, out var at) ? at : 0;

        return _ink.Series(order, Swatch(chart.Settings.Palette, order));
    }

    /// <summary>
    /// Draws the rows as counts in bins rather than as marks of their own.
    ///
    /// <para>
    /// A bin stands for no one row, so it carries no part and nothing in it takes a caret — the same as a
    /// gridline, and for the same reason: nobody typed it. What it is <em>about</em> is how many rows fell
    /// there, which is what its colour says.
    /// </para>
    /// </summary>
    private DiagramSpan? Binned(PlotChart chart, LayoutBuilder build, Rect plot,
                                Placing across, Placing up, IReadOnlyList<Color> stops,
                                List<Diagnostic> trouble)
    {
        var it = chart.Settings;
        var points = new List<(double X, double Y)>();

        foreach (var mark in chart.Marks)
        {
            var x = across.At(mark[across.Channel]);
                    var y = up.At(mark[up.Channel]);

            if (x is null || y is null)
            {
                trouble.Add(new Diagnostic(mark.Part.Start, Math.Max(1, mark.Part.Length),
                                           DiagnosticSeverity.Warning,
                                           "This row has no place: it takes a value across and a value up."));
                continue;
            }

            // Binned on the panel rather than in the values' own space, so a hexagon comes out regular
            // whatever the axes happen to span.
            points.Add((x.Value * plot.Width, (1 - y.Value) * plot.Height));
        }

        if (points.Count == 0) return null;

        var binning = it.Geom == PlotGeom.Hex
            ? PlotBins.Hexagons(points, plot.Width, it.BinsX)
            : PlotBins.Rectangles(points, plot.Width, plot.Height, it.BinsX, it.BinsY);

        var most = binning.Bins.Max(bin => bin.Count);
        var span = DiagramSpan.Of(1, Math.Max(2, most), widen: false);

        build.Open(PlotPiece.Marks, part: null, stops: Stops.None);

        foreach (var bin in binning.Bins)
        {
            var box = new Rect(plot.Left + bin.X - (binning.Wide / 2),
                               plot.Top + bin.Y - (binning.Tall / 2),
                               binning.Wide, binning.Tall);

            var outline = it.Geom == PlotGeom.Hex
                ? DiagramGlyphs.Outline(DiagramGlyph.Hexagon, box)
                : DiagramShapes.Outline(DiagramShape.Rectangle, box);

            build.Open(PlotPiece.Bin, part: null, stops: Stops.None);
            build.Draw(new GeometryMark(outline, _ink.Scale(stops, span.At(bin.Count) ?? 0), null, 0));
            build.Occupies(outline);
            build.Close();
        }

        build.Close();

        return span;
    }

    /// <summary>
    /// Draws how thickly the rows lie rather than the rows themselves: the contours of a kernel density
    /// estimate, filled between levels, drawn as lines, or coloured crossing by crossing.
    ///
    /// <para>
    /// Like a bin, a contour stands for no one row — nobody typed it — so nothing in it takes a caret.
    /// Where <c>points:</c> asks for them, the rows are drawn over it as marks that do.
    /// </para>
    /// </summary>
    private DiagramSpan? Clouded(PlotChart chart, LayoutBuilder build, Rect plot,
                                 Placing across, Placing up, IReadOnlyList<Color> stops,
                                 List<Diagnostic> trouble)
    {
        var it = chart.Settings;
        var points = new List<(double X, double Y)>();

        foreach (var mark in chart.Marks)
        {
            var x = across.At(mark[across.Channel]);
                    var y = up.At(mark[up.Channel]);

            if (x is null || y is null)
            {
                trouble.Add(new Diagnostic(mark.Part.Start, Math.Max(1, mark.Part.Length),
                                           DiagnosticSeverity.Warning,
                                           "This row has no place: it takes a value across and a value up."));
                continue;
            }

            // Estimated on the panel, so the kernel is as wide one way as the other where the block says so.
            points.Add((x.Value * plot.Width, y.Value * plot.Height));
        }

        if (points.Count < 3)
        {
            if (points.Count > 0)
                trouble.Add(new Diagnostic(0, Math.Max(1, Source.Length), DiagnosticSeverity.Warning,
                                           "Too few rows to say how thickly they lie. A density takes at least three."));

            return null;
        }

        var field = PlotDensity.Estimate(points, adjust: it.Adjust, bandwidth: it.Bandwidth);
        var most = field.Most;

        if (!(most > 0)) return null;

        var span = DiagramSpan.Of(0, most, widen: false);

        build.Open(PlotPiece.Marks, part: null, stops: Stops.None);

        if (it.Contour == PlotContour.Raster) this.Rastered(build, plot, field, span, stops);
        else this.Ringed(build, plot, field, span, stops, it);

        build.Close();

        return span;
    }

    /// <summary>Every crossing of the grid coloured by how thickly the points lie there.</summary>
    private void Rastered(LayoutBuilder build, Rect plot, PlotField field, DiagramSpan span,
                          IReadOnlyList<Color> stops)
    {
        var wide = plot.Width / Math.Max(1, field.Across - 1);
        var tall = plot.Height / Math.Max(1, field.Up - 1);

        for (var i = 0; i < field.Across; i++)
            for (var j = 0; j < field.Up; j++)
            {
                var (x, y) = field.Where(i, j);

                // The grid reaches past the panel, so what falls outside it is not drawn.
                if (x < 0 || x > plot.Width || y < 0 || y > plot.Height) continue;

                var box = new Rect(plot.Left + x - (wide / 2), plot.Bottom - y - (tall / 2), wide, tall);

                build.Open(PlotPiece.Cloud, part: null, stops: Stops.None);
                build.Draw(new RuleMark(box, _ink.Scale(stops, span.At(field.At[i, j]) ?? 0)));
                build.Covers(box);
                build.Close();
            }
    }

    /// <summary>
    /// The contours themselves: filled from the thinnest level up, so each lies over the one below it and a
    /// reader sees the cloud thicken; or drawn as lines where the rows beneath are what matters.
    /// </summary>
    private void Ringed(LayoutBuilder build, Rect plot, PlotField field, DiagramSpan span,
                        IReadOnlyList<Color> stops, PlotSettings it)
    {
        foreach (var level in PlotDensity.Contours(field, it.Levels))
        {
            var ink = _ink.Scale(stops, span.At(level.At) ?? 0);

            foreach (var ring in level.Rings)
            {
                var points = ring.Points
                                 .Select(point => new Point(plot.Left + point.X, plot.Bottom - point.Y))
                                 .ToList();

                if (points.Count < 3) continue;

                var outline = DiagramCurve.Closed(points);

                build.Open(PlotPiece.Cloud, part: null, stops: Stops.None);

                if (it.Contour == PlotContour.Bands) build.Draw(new GeometryMark(outline, ink, null, 0));
                else build.Draw(new GeometryMark(outline, null, ink, 1.4));

                build.Covers(outline.Bounds);
                build.Close();
            }
        }
    }

    /// <summary>
    /// Draws a mark per row: a point, a bubble or a tile, wherever its values put it.
    ///
    /// <para>
    /// Hands back the values to be written on the marks, rather than writing them here, so every label is
    /// set in one layer over every mark — a value half under the next tile would be a value nobody can read.
    /// </para>
    /// </summary>
    private List<(DiagramWords Words, Point At)> Drawn(PlotChart chart, LayoutBuilder build, Rect plot,
                                                       Placing across, Placing up, Painting paint,
                                                       IReadOnlyDictionary<string, int> shapes, bool tiles,
                                                       List<Diagnostic> trouble, bool over = false)
    {
        var it = chart.Settings;
        var labels = new List<(DiagramWords Words, Point At)>();

        build.Open(PlotPiece.Marks, part: null, stops: Stops.None);

        foreach (var mark in chart.Marks)
        {
            var x = across.At(mark[across.Channel]);
                        var y = up.At(mark[up.Channel]);

            if (x is null || y is null)
            {
                // A row that says nothing this can place is waved where it stands, and the rest are still
                // a plot. It is the row somebody is editing, and it is wrong every time they are halfway
                // through changing it.
                trouble.Add(new Diagnostic(mark.Part.Start, Math.Max(1, mark.Part.Length),
                                           DiagnosticSeverity.Warning,
                                           "This row has no place: it takes a value across and a value up."));
                continue;
            }

            var (shakeAcross, shakeUp) = Shake(chart, mark, plot, across, up);

                    // Held inside the panel, because a mark shaken past the axis is a mark standing at a value the
                            // axis says is not there.
                            var at = new Point(Math.Clamp(plot.Left + (x.Value * plot.Width) + shakeAcross, plot.Left, plot.Right),
                                               Math.Clamp(plot.Bottom - (y.Value * plot.Height) + shakeUp, plot.Top, plot.Bottom));

                    var fill = this.Fill(chart, mark, paint);

            var box = tiles
                        ? new Rect(at.X - (across.Slot * plot.Width / 2), at.Y - (up.Slot * plot.Height / 2),
                                   across.Slot * plot.Width, up.Slot * plot.Height)
                        : Around(at, over ? Over : Radius(chart, mark));

            var outline = tiles
                ? DiagramShapes.Outline(DiagramShape.Rectangle, box)
                : DiagramGlyphs.Outline(this.Glyph(chart, mark, shapes), box);

            build.Open(PlotPiece.Mark, mark.Part, stops: Stops.None);
    build.Draw(new GeometryMark(outline,
                                        tiles ? fill : DiagramInk.Faded(fill, Clearness(chart, mark, over)),
                                        null, 0));
            build.Occupies(outline);
            build.Close();

            // A value written on its own tile, which a small heat map has room for and a big one has not.
    // A column mapped to label names each mark; `labels: true` writes the value the colour stands for.
            var naming = mark[PlotAesthetic.Label] ?? (it.Labels ? mark[paint.Channel] : null);
            if (naming is not { } said) continue;

            // The characters the reader typed, so a value drawn on a mark is one they can type into.
            var words = this.Written(said.Inner, LabelSize, tiles ? _ink.Over(fill) : _palette.Text);

            labels.Add((words, tiles
                ? new Point(at.X - (words.Width / 2), at.Y - (words.Height / 2))
                : new Point(at.X + Gap, at.Y - (words.Height / 2))));
        }

        build.Close();

        return labels;
    }

    /// <summary>A fitted line and what it is drawn in — its group's colour, so overlapping bands are told apart.</summary>
    private sealed record Fitting(PlotLine Line, Brush Ink);

    /// <summary>
    /// The lines fitted through the points, worked out but not yet drawn.
    ///
    /// <para>
    /// Fitted on the panel, so a line follows a log axis where there is one and can simply be drawn. What
    /// the points say about each other is worked out from the values instead, because down the page is the
    /// way a screen counts and not the way a number does.
    /// </para>
    /// </summary>
    private IReadOnlyList<Fitting> Fits(PlotChart chart, Rect plot, Placing across, Placing up, Painting paint)
    {
        var it = chart.Settings;
        if (it.Fit == PlotFit.None) return [];

        var placed = new List<(double X, double Y)>();
        var inks = new List<Brush>();

        foreach (var mark in chart.Marks)
            if (across.At(mark[across.Channel]) is { } x && up.At(mark[up.Channel]) is { } y)
            {
                placed.Add((plot.Left + (x * plot.Width), plot.Bottom - (y * plot.Height)));
                inks.Add(this.Fill(chart, mark, paint));
            }

        var grouped = chart.Named(PlotAesthetic.Group).Count > 0;
        var lines = new List<Fitting>();

        foreach (var group in Split(chart, placed.Count))
        {
            if (group.Count == 0) continue;

            var points = group.Select(at => placed[at]).ToList();

            var line = it.Fit == PlotFit.Loess
                ? PlotFits.Curved(points, it.Se, level: it.Level)
                : PlotFits.Straight(points, it.Se, it.Level);

            if (line is null) continue;

            // One fit through everything is the accent's; a fit per group takes that group's own colour, so
            // two bands lying over each other still say which is which.
            lines.Add(new Fitting(line, grouped ? inks[group[0]] : _palette.Accent));
        }

        return lines;
    }

    /// <summary>
    /// How far up and down the fitted bands reach, in the values' own terms — so the axis can be opened out
    /// to show all of them.
    ///
    /// <para>
    /// <strong>A band is part of the answer, not decoration over it.</strong> Cropping one to the panel
    /// leaves a plot that says the fit stops where the axis happens to, which is not what the arithmetic
    /// said; ggplot2 trains its scale on every layer for the same reason. Where the block wrote the ends
    /// itself, they are the ends it asked for and this is not consulted.
    /// </para>
    /// <para>
    /// Worked out in the axis's own reading rather than on the panel, because the panel is not laid out yet
    /// — and a fit down a logarithmic axis is a fit through the logarithms either way.
    /// </para>
    /// </summary>
    private (double Min, double Max)? Banding(PlotChart chart, PlotAesthetic acrossChannel,
                                              PlotAesthetic upChannel, DiagramSpan across, DiagramSpan up)
    {
        var it = chart.Settings;

        if (it.Fit == PlotFit.None || !it.Se) return null;

        var read = new List<(double X, double Y)>();

        foreach (var mark in chart.Marks)
            if (mark[acrossChannel]?.Number is { } x && mark[upChannel]?.Number is { } y
                && across.Reading(x) is { } alongX && up.Reading(y) is { } alongY)
                read.Add((alongX, alongY));

        double? least = null, most = null;

        foreach (var group in Split(chart, read.Count))
            {
                var points = group.Select(at => read[at]).ToList();

                var line = it.Fit == PlotFit.Loess
                    ? PlotFits.Curved(points, band: true, level: it.Level)
                    : PlotFits.Straight(points, band: true, it.Level);

            if (line?.Below is not { } below || line.Above is not { } above) continue;

            foreach (var (_, y) in below) least = least is null ? y : Math.Min(least.Value, y);
            foreach (var (_, y) in above) most = most is null ? y : Math.Max(most.Value, y);
        }

        if (least is null || most is null) return null;

        return (up.Value(least.Value), up.Value(most.Value));
    }

    /// <summary>
    /// What the points say about each other, written on the panel — from the values themselves, because
    /// down the page is the way a screen counts and not the way a number does.
    /// </summary>
    private void Reported(PlotChart chart, LayoutBuilder build, Rect plot)
    {
        var it = chart.Settings;
        if (it.Stats is not { Count: > 0 }) return;

        var values = new List<(double X, double Y)>();

        foreach (var mark in chart.Marks)
            if (mark[PlotAesthetic.X]?.Number is { } x && mark[PlotAesthetic.Y]?.Number is { } y)
                values.Add((x, y));

        if (PlotFits.Of(it.Method, values) is { } said) this.Reported(build, plot, it, said);
    }

    /// <summary>
    /// The band a fit's own uncertainty makes, under the marks.
    ///
    /// <para>
    /// Under, because a band is context and the rows are the subject: drawn over them it greys out the very
    /// points it is about. And clipped to the panel, because a fit through four points has a band wider than
    /// anything the axes cover, and a band reaching past them says the plot extends where it does not.
    /// </para>
    /// </summary>
    private void Banded(LayoutBuilder build, IReadOnlyList<Fitting> lines, Rect plot)
    {
        foreach (var fitting in lines)
        {
            var line = fitting.Line;
            if (line.Below is not { Count: > 1 } below || line.Above is not { Count: > 1 } above) continue;

            // Up one edge and back down the other, which closes the band without a seam through it.
            var round = new List<Point>();

            foreach (var (x, y) in below) round.Add(new Point(x, y));
            for (var at = above.Count - 1; at >= 0; at--) round.Add(new Point(above[at].X, above[at].Y));

            var shape = Within(DiagramCurve.Closed(round), plot);

                    build.Open(PlotPiece.Band, part: null, stops: Stops.None);
            build.Draw(new GeometryMark(shape, DiagramInk.Faded(fitting.Ink, 0.16), null, 0));
            build.Covers(plot);
            build.Close();
        }
    }

    /// <summary>Whatever of a shape falls inside the panel, which is all of it that means anything.</summary>
    private static Geometry Within(Geometry shape, Rect plot)
    {
        var panel = new RectangleGeometry(plot);
        var kept = Geometry.Combine(shape, panel, GeometryCombineMode.Intersect, null);

        kept.Freeze();
        return kept;
    }

    /// <summary>
    /// Which rows belong together for the purpose of a fit, as their places in the order they were read.
    ///
    /// <para>
    /// Indices rather than the points themselves, so a caller can find whatever else it holds about the
    /// same rows — their colours, say. Handing back the points alone left the colours to be matched up by
    /// counting, and a dictionary does not hand its groups back in the order they were written.
    /// </para>
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<int>> Split(PlotChart chart, int count)
    {
        var named = chart.Marks.Where(mark => mark[PlotAesthetic.X] is not null).ToList();

        if (chart.Named(PlotAesthetic.Group).Count == 0)
            return [[.. Enumerable.Range(0, count)]];

        var parts = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var at = 0; at < count && at < named.Count; at++)
        {
            var name = named[at][PlotAesthetic.Group]?.Text ?? string.Empty;

            if (!parts.TryGetValue(name, out var part)) parts[name] = part = [];

            part.Add(at);
        }

        return [.. parts.Values];
    }

    /// <summary>The fitted lines themselves, over the marks and clipped to the panel.</summary>
    private void Traced(LayoutBuilder build, IReadOnlyList<Fitting> lines, Rect plot)
    {
        foreach (var fitting in lines)
        {
            var line = fitting.Line;
            var trace = new PathFigure { StartPoint = new Point(line.Along[0].X, line.Along[0].Y) };

            for (var at = 1; at < line.Along.Count; at++)
                trace.Segments.Add(new LineSegment(new Point(line.Along[at].X, line.Along[at].Y), true));

            var path = new PathGeometry();
            path.Figures.Add(trace);

            var drawn = Within(path.GetWidenedPathGeometry(new Pen(Brushes.Black, 2)), plot);

            build.Open(PlotPiece.Fit, part: null, stops: Stops.None);
    build.Draw(new GeometryMark(drawn, fitting.Ink, null, 0));
            build.Occupies(drawn);
            build.Close();
        }
    }

    /// <summary>
    /// The figures written at the top left of the panel, where a correlation plot puts them and where the
    /// points of one rarely are.
    /// </summary>
    private void Reported(LayoutBuilder build, Rect plot, PlotSettings it, PlotCorrelation said)
    {
        var parts = new List<string>();

        foreach (var what in it.Stats!)
            parts.Add(what switch
            {
                "r" => $"{Letter(it.Method)} = {Figure(said.R)}",
                "r2" => $"R² = {Figure(said.RSquared)}",
                "n" => $"n = {said.N}",
                _ => said.P < 0.001 ? "p < 0.001" : $"p = {Figure(said.P)}",
            });

        var words = this.Worked(string.Join("   ", parts), null, LabelSize, _palette.Text);

        build.Open(PlotPiece.Stats, part: null, stops: Stops.None);
        words.Set(build, new Point(plot.Left + Gap, plot.Top + Gap), PlotPiece.Stats);
        build.Close();
    }

    /// <summary>What a correlation is written as, which is the letter its own method uses.</summary>
    private static string Letter(PlotMethod method) => method switch
    {
        PlotMethod.Spearman => "ρ",
        PlotMethod.Kendall => "τ",
        _ => "r",
    };

    /// <summary>A coefficient as a reader wants it: two decimals, which is all one is worth.</summary>
    private static string Figure(double value) => value.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>
    /// Where each name stands in the order the groups were first written.
    ///
    /// <para>
    /// A map rather than a search, because this is asked once per mark: a scatter of four thousand rows
    /// against a dozen groups walked the list four thousand times to answer the same twelve questions.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, int> Ordered(IReadOnlyList<string> names)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var at = 0; at < names.Count; at++) order[names[at]] = at;

        return order;
    }

    /// <summary>The colour the block wrote for a place in the order, or null to leave it to the theme.</summary>
    private static string? Swatch(IReadOnlyList<string>? palette, int order) =>
        palette is null || palette.Count == 0 ? null : palette[order % palette.Count];

    // ── The drawing ─────────────────────────────────────────────────────────

    private Laid Lay(PlotChart chart)
    {
        var it = chart.Settings;

        var (wide, tall) = SettingRoom.Fit(it.Width, it.Height, _room, PlotSettings.HeightShare);

        var trouble = new List<Diagnostic>();

        // A tile stands for one value rather than a stretch of them, so its axes are laid out in slots.
        var tiles = it.Geom == PlotGeom.Tile;
    var bins = it.Geom is PlotGeom.Bin2d or PlotGeom.Hex;
            var cloud = it.Geom == PlotGeom.Density2d;

        // Flipped, the axes swap: what was read across is read up. Everything after this is told which is
            // which, so nothing else has to know.
            var acrossChannel = it.Flip ? PlotAesthetic.Y : PlotAesthetic.X;
            var upChannel = it.Flip ? PlotAesthetic.X : PlotAesthetic.Y;

            var across = this.Along(chart, acrossChannel, it.XScale, it.XLimits, it.XBreaks, tiles);

                // A band is part of the answer, so the axis opens out to show all of it — unless the block wrote the
                // ends itself, in which case they are the ends it asked for.
                var banding = tiles || bins || cloud || it.YLimits is not null
                    ? null
                    : this.Banding(chart, acrossChannel, upChannel,
                                   Spanned(chart, acrossChannel, it.XScale, it.XLimits),
                                   Spanned(chart, upChannel, it.YScale, it.YLimits));

                var up = this.Along(chart, upChannel, it.YScale, it.YLimits, it.YBreaks, tiles, banding);

        var paint = this.Paint(chart, trouble);
    var named = chart.Named(PlotAesthetic.Shape);
            var shapes = Ordered(named);

    if (it.Shape is not null && named.Count == 0 && DiagramGlyphs.Named(it.Shape) is null)
            trouble.Add(new Diagnostic(0, Math.Max(1, Source.Length), DiagnosticSeverity.Warning,
                                       $"`shape: {it.Shape}` names neither a column nor a mark. "
                                       + $"The marks are {DiagramGlyphs.Names}."));

        var title = it.Title is null ? null : this.Worked(it.Title, null, TitleSize, _palette.Heading);
            var subtitle = it.Subtitle is null ? null : this.Worked(it.Subtitle, null, LabelSize, _palette.TextMuted);
            var caption = it.Caption is null ? null : this.Worked(it.Caption, null, LabelSize, _palette.TextMuted);

            // A title follows its own channel rather than its side of the panel: `xTitle:` names what `x:` maps,
                // and flipping carries both of them up the page together. Naming the side instead put the weight
                // title under an axis of miles per gallon.
                var xTitle = this.AxisTitle(chart, acrossChannel, it.Flip ? it.YTitle : it.XTitle);
                var yTitle = this.AxisTitle(chart, upChannel, it.Flip ? it.XTitle : it.YTitle);

        // What a binned plot's colours run over is the counts, and there is no knowing them until the bins
        // are cut — so it takes the room a bar needs now and is given its numbers once they are counted.
    var key = bins || cloud
                ? this.Counting(DiagramSpan.Of(1, 2, widen: false), paint.Stops)
                : this.Key(chart, paint);

        // Round the panel: the upright axis's numbers on its left with its title turned up beyond them,
        // the flat axis's under it, the title over it, and the key on whichever side was asked for.
        var left = DiagramAxis.Room(up.Ticks, upright: true) + (yTitle is null ? 0 : yTitle.Height + Gap)
                   + (it.Legend == PlotLegend.Left ? key.Size.Width + (Gap * 2) : 0);

        var top = (title is null ? Gap : title.Height + Gap)
                      + (subtitle is null ? (title is null ? 0 : Gap) : subtitle.Height + Gap)
                      + (it.Legend == PlotLegend.Top ? key.Size.Height + Gap : 0);

            var bottom = DiagramAxis.Room(across.Ticks, upright: false) + (xTitle is null ? 0 : Gap + xTitle.Height)
                         + (caption is null ? 0 : Gap + caption.Height)
                         + (it.Legend == PlotLegend.Bottom ? key.Size.Height + (Gap * 2) : 0);

        var right = Math.Max(Gap * 2, (across.Ticks.LastOrDefault()?.Words?.Width / 2) ?? 0)
                    + (it.Legend == PlotLegend.Right ? key.Size.Width + (Gap * 2) : 0);

        var panel = new Size(Math.Max(Smallest, wide - left - right), Math.Max(Smallest, tall - top - bottom));

            // A panel the block gave a shape to keeps it, and the room left over is simply not used: a
            // correlation matrix asked for square cells is not a correlation matrix drawn oblong.
            if (it.Aspect is { } shape and > 0)
            {
                if (panel.Width / panel.Height > shape) panel = new Size(panel.Height * shape, panel.Height);
                else panel = new Size(panel.Width, panel.Width / shape);
            }

        var plot = new Rect(left, top, panel.Width, panel.Height);

                // What was actually drawn, which is what the block takes up. A panel held to a shape leaves room
                // over, and room over would stand the key away from the plot it explains.
                wide = left + panel.Width + right;
                tall = top + panel.Height + bottom;

        var build = new LayoutBuilder();
        build.Open(PlotPiece.Plot);
        build.Covers(new Rect(0, 0, wide, tall));

        // Behind everything, because a gridline is there to be read past.
        var rule = new DiagramStroke(_palette.CodeBorder);
        var faint = new DiagramStroke(DiagramInk.Faded(_palette.CodeBorder, 0.45));

    if (!tiles && it.Grid is PlotGrid.Both or PlotGrid.Y)
            DiagramGrid.Draw(build, PlotPiece.Grid, plot, up.Ticks, upright: true, faint);

        if (!tiles && it.Grid is PlotGrid.Both or PlotGrid.X)
            DiagramGrid.Draw(build, PlotPiece.Grid, plot, across.Ticks, upright: false, faint);

    var labels = new List<(DiagramWords Words, Point At)>();

            // Under the marks: a band is context and the rows are the subject, so drawn over them it greys out
            // the very points it is about.
        var fits = bins || cloud ? [] : this.Fits(chart, plot, across, up, paint);
            this.Banded(build, fits, plot);

        if (bins)
            {
                if (this.Binned(chart, build, plot, across, up, paint.Stops, trouble) is { } counts)
                    key = this.Counting(counts, paint.Stops);
            }
            else if (cloud)
            {
                // A density says nothing a reader can put a number to, so its key is the run of colours alone
                // rather than a scale of how thickly anything lies.
                this.Clouded(chart, build, plot, across, up, paint.Stops, trouble);
                key = new Chart(null, null);

                // The rows themselves over the cloud, where the block asks for both.
        if (it.Points) this.Drawn(chart, build, plot, across, up, paint, shapes, false, [], over: true);
            }
            else
            {
                labels = this.Drawn(chart, build, plot, across, up, paint, shapes, tiles, trouble);
            }

    // Over the marks and under the axes: worked out from them, and never over the numbers.
        this.Traced(build, fits, plot);

        if (!bins && !cloud) this.Reported(chart, build, plot);

            if (labels.Count > 0)
        {
            build.Open(PlotPiece.Labels, part: null, stops: Stops.None);
            foreach (var (words, where) in labels) words.Set(build, where, PlotPiece.Label);
            build.Close();
        }

        DiagramAxis.Draw(build, PlotPiece.YAxis, null, plot.BottomLeft, plot.TopLeft, up.Ticks, rule,
                         PlotPiece.Tick, after: false);

        DiagramAxis.Draw(build, PlotPiece.XAxis, null, plot.BottomLeft, plot.BottomRight, across.Ticks, rule,
                         PlotPiece.Tick, after: true);

        title?.Set(build, new Point(plot.Left + Math.Max(0, (plot.Width - title.Width) / 2), Gap),
                       PlotPiece.Title);

            subtitle?.Set(build, new Point(plot.Left + Math.Max(0, (plot.Width - subtitle.Width) / 2),
                                           (title?.Height ?? 0) + Gap),
                          PlotPiece.Title);

            caption?.Set(build, new Point(plot.Left, tall - caption.Height), PlotPiece.Title);

        // Turned a quarter turn so it reads up the axis: anchored at its foot, it reaches up by however
        // wide its words are.
        yTitle?.Set(build, new Point(0, plot.Top + ((plot.Height + yTitle.Width) / 2)),
                    PlotPiece.AxisTitle, degrees: -90);

        xTitle?.Set(build, new Point(plot.Left + Math.Max(0, (plot.Width - xTitle.Width) / 2),
                                     plot.Bottom + DiagramAxis.Room(across.Ticks, upright: false) + Gap),
                    PlotPiece.AxisTitle);

        if (key.Size.Height > 0) key.Draw(build, this.Where(it.Legend, key.Size, plot, wide, tall));

        build.Close();

        return new Laid(build.Seal(), new Size(wide, tall), trouble);
    }

    /// <summary>The box a mark of a given radius is drawn in.</summary>
    private static Rect Around(Point at, double radius) =>
        new(at.X - radius, at.Y - radius, radius * 2, radius * 2);

    /// <summary>Where the key goes, for the side it was asked for.</summary>
    private Point Where(PlotLegend side, Size key, Rect plot, double wide, double tall) => side switch
    {
        PlotLegend.Left => new Point(Gap, plot.Top),
        PlotLegend.Top => new Point(plot.Left, Math.Max(0, plot.Top - key.Height - Gap)),
        PlotLegend.Bottom => new Point(plot.Left + Math.Max(0, (plot.Width - key.Width) / 2), tall - key.Height),
        _ => new Point(Math.Max(plot.Right + (Gap * 2), wide - key.Width), plot.Top),
    };

    /// <summary>
    /// The key, whichever kind this chart needs: a row per group where the colour names one, and a bar of
    /// the colours themselves where it is a quantity.
    /// </summary>
    private sealed record Chart(DiagramLegend? Rows, DiagramBar? Bar, DiagramWords? Heading = null)
    {
        private const double Apart = 4;

        public Size Size
        {
            get
            {
                var key = this.Rows?.Size ?? this.Bar?.Size ?? new Size(0, 0);

                if (this.Heading is not { } heading || key.Height <= 0) return key;

                return new Size(Math.Max(key.Width, heading.Width), key.Height + heading.Height + Apart);
            }
        }

        public void Draw(LayoutBuilder build, Point at)
        {
            // Words rather than a row of the key: a row with no swatch is drawn as an empty outlined
            // square, which reads as a colour nobody chose rather than as a heading.
            if (this.Heading is { } heading)
            {
                heading.Set(build, at, PlotPiece.Name);
                at = new Point(at.X, at.Y + heading.Height + Apart);
            }

            this.Rows?.Draw(build, at);
            this.Bar?.Draw(build, at);
        }
    }

    /// <summary>What explains the colours, or nothing where they explain themselves.</summary>
    private Chart Key(PlotChart chart, Painting paint)
    {
        if (chart.Settings.Legend == PlotLegend.None) return new Chart(null, null);

        if (paint.Span is { } span)
        {
            // Three numbers is what a bar needs to be read: where it starts, where it ends, and the middle
            // — which is the value the colour turns about where one was written.
            var marks = new[] { 0.0, 0.5, 1.0 }
                .Select(at => (At: at, Value: span.Min + (at * (span.Max - span.Min))))
                .Select(mark => (mark.At, Words: this.Worked(DiagramScale.Plain(Rounded(mark.Value)), null, LabelSize, _palette.Text)))
                .ToList();

    return new Chart(null, new DiagramBar(paint.Stops, marks, _palette.CodeBorder), this.Heading(chart));
        }

        if (paint.Groups.Count == 0) return new Chart(null, null);

        var rows = paint.Groups.Select((name, at) => new DiagramKey(
                                    this.Named(chart, paint.Channel, name),
                                    _ink.Series(at, Swatch(chart.Settings.Palette, at)),
                                    [this.Worked(name, null, LabelSize, _palette.Text)]))
                                .ToList();

        var across = chart.Settings.Legend is PlotLegend.Bottom or PlotLegend.Top;

    return new Chart(new DiagramLegend(rows, [PlotPiece.Name], across, _palette.CodeBorder), null, this.Heading(chart));
    }

    /// <summary>What the key is called, where the block calls it anything.</summary>
    private DiagramWords? Heading(PlotChart chart) =>
        chart.Settings.LegendTitle is { } said
            ? this.Worked(said, null, LabelSize, _palette.Heading)
            : null;

    /// <summary>
    /// The key a binned heat map gets: the counts its colours run over, rather than any column's values.
    /// </summary>
    private Chart Counting(DiagramSpan counts, IReadOnlyList<Color> stops)
    {
        var marks = new[] { 0.0, 0.5, 1.0 }
            .Select(at => (At: at, Value: Math.Round(counts.Min + (at * (counts.Max - counts.Min)))))
            .Select(mark => (mark.At, Words: this.Worked(DiagramScale.Plain(mark.Value), null, LabelSize, _palette.Text)))
            .ToList();

        return new Chart(null, new DiagramBar(stops, marks, _palette.CodeBorder));
    }

    /// <summary>A number on a colour bar, kept to what a reader can take in at a glance.</summary>
    private static double Rounded(double value)
    {
        if (value == 0 || double.IsNaN(value) || double.IsInfinity(value)) return value;

        var power = Math.Pow(10, 2 - Math.Ceiling(Math.Log10(Math.Abs(value))));
        return Math.Round(value * power) / power;
    }

    /// <summary>Where a group was first named, which is what its row in the key stands for.</summary>
    private ContentPart? Named(PlotChart chart, PlotAesthetic channel, string group) =>
        chart.Marks.Select(mark => mark[channel])
                   .FirstOrDefault(value => value is not null && value.Text == group)
                  ?.Cell;

    /// <summary>What an axis is called: what the block says, else the name of the column feeding it.</summary>
    private DiagramWords? AxisTitle(PlotChart chart, PlotAesthetic channel, string? written) =>
        chart.Titled(channel, written) is { Length: > 0 } says
            ? this.Worked(says, null, LabelSize, _palette.TextMuted)
            : null;

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>Words worked out rather than typed — a tick's number, an axis title, a name in the key.</summary>
    private DiagramWords Worked(string says, ContentPart? part, double size, Brush ink) =>
        new(this.Text(says, size, ink), part, null, this.Text("x", size, ink), ink,
            maps: false, writes: false);

    /// <summary>
    /// Words that are the characters somebody typed, drawn where they were written.
    ///
    /// <para>
    /// What makes a plot editable: a caret stands inside one of these, a drag through it picks out its
    /// digits, and typing into the picture edits the block. A value the plot worked out rather than read —
    /// a tick's number, a coefficient — is <see cref="Worked"/> instead, and stepping never stops in one.
    /// </para>
    /// </summary>
    private DiagramWords Written(ContentPart part, double size, Brush ink) =>
        new(this.Text(part.Text, size, ink), part, null, this.Text("x", size, ink), ink,
            maps: true, writes: true);

    private FormattedText Text(string says, double size, Brush ink) =>
        new(says,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(WordFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size,
            ink,
            _dpi);

    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(SourceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            SourceSize,
            Brushes.Black,
            _dpi);

    /// <summary>The block shown as written, with the reason it could not be drawn.</summary>
    private Laid Stopped(string reason) =>
        LayoutText.Shown(Source, this.Characters(Source.Length == 0 ? " " : Source),
                         [new Diagnostic(0, Math.Max(Source.Length, 1), DiagnosticSeverity.Error, reason)]);
}
