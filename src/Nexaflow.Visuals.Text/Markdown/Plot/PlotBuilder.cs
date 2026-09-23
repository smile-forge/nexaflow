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
/// Draws a plot block on the shared layout tree, reusing the Mermaid diagram kit (<see cref="DiagramShapes"/>,
/// <see cref="DiagramAxis"/>, <see cref="DiagramInk"/>, <see cref="DiagramLegend"/>) for marks, axes, colour
/// and the key. Every mark carries the source part it was drawn from, so it stays editable.
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

    /// <summary>Mark radius when drawn over a density/binning layer — small so the cloud stays visible.</summary>
    private const double Over = 1.6;

    /// <summary>And how much of what is under one shows through it.</summary>
    private const double Through = 0.25;

    private readonly StyleFormat _palette;
    private readonly DiagramInk _ink;
    private readonly PlotSettings _settings;
    private readonly string? _unreadable;
    
    private PlotBuilder(ContentReading reading, PlotSettings settings, string? unreadable, StyleFormat palette)
        : base(reading, EditState.For(reading.Source), palette, isReadOnly: true)
    {
        _settings = settings;
        _unreadable = unreadable;
        _palette = palette;
        _ink = new DiagramInk(palette);
    
    }

    /// <summary>Reads a block and lays it out. Never null, and never throws.</summary>
    public static Laid Build(string source, PlotFence fence, StyleFormat palette, double room, int at = 0)
    {
        var tree = PlotPipeline.Read(source, fence, out var settings, out var unreadable);

        return new PlotBuilder(ContentReading.Of(tree, at), settings, unreadable, palette).Lay(room);
    }

    protected override Laid? Build()
    {
        if (_unreadable is not null) return Stopped(_unreadable);

        var chart = PlotChart.Of(Reading.Root, _settings);

        if (chart.Marks.Count == 0)
            return Stopped("An empty plot. It takes a row of values for each point — two columns for where "
                         + "it goes — and settings written as `key: value` above them.");

        return Lay(chart);
    }

    // ── Turning a value into a place ────────────────────────────────────────

    /// <summary>Where a channel's values land along one axis, from 0 at the start to 1 at the end.</summary>
    /// <param name="Of">Where a bare name lands, for marks worked out rather than written.</param>
    private sealed record Placing(PlotAesthetic Channel, IReadOnlyList<DiagramTick> Ticks,
                                  Func<PlotValue?, double?> At, double Slot, Func<string, double?>? Of = null);

    /// <summary>Lays a channel along its axis: numbered for continuous values, one slot per name for discrete
    /// ones (ggplot2's continuous/discrete split).</summary>
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

    return this.Slotted(channel, slots ? chart.Slots(channel) : chart.Named(channel));
}

/// <summary>Axis of named slots: a value sits mid-slot so first/last stay inside the panel, and slot width
/// equals tile width.</summary>
private Placing Slotted(PlotAesthetic channel, IReadOnlyList<string> names)
{
    if (names.Count == 0) return new Placing(channel, [], _ => null, 1);

    var places = new Dictionary<string, double>(StringComparer.Ordinal);
    for (var at = 0; at < names.Count; at++) places[names[at]] = (at + 0.5) / names.Count;

    var marked = names.Select(name => new DiagramTick(
                          places[name], this.Worked(name, null, LabelSize, _palette.TextMuted)))
                      .ToList();

    double? Place(string name) => places.TryGetValue(name, out var place) ? place : null;

    return new Placing(channel, marked, value => value is null ? null : Place(value.Text),
                       1.0 / names.Count, Place);
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

    /// <summary>Glyph for a point: shape-column mapping, else the block-named shape, else a circle.</summary>
    private DiagramGlyph Glyph(PlotChart chart, PlotMark mark, IReadOnlyDictionary<string, int> shapes)
    {
        if (shapes.Count == 0) return DiagramGlyphs.Named(chart.Settings.Shape) ?? DiagramGlyph.Circle;

        return mark[PlotAesthetic.Shape] is { } value && shapes.TryGetValue(value.Text, out var at)
            ? DiagramGlyphs.At(at)
            : DiagramGlyph.Circle;
    }

    /// <summary>Mark radius, scaled by area not radius — matches ggplot2's size scale, since ink read is
    /// what matters, not radius.</summary>
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

    /// <summary>Mark alpha: mapped from the alpha channel if fed by a column, else the plain default.</summary>
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

    /// <summary>Jitter offset for overlapping marks, seeded from the row's source position (not the clock)
    /// so the same block always renders identically.</summary>
    private static (double Across, double Up) Shake(PlotChart chart, PlotMark mark, Rect plot,
                                                    Placing across, Placing up)
    {
        if (chart.Settings.Jitter <= 0) return (0, 0);

        var throws = new Random(mark.Part.Start * 397);

        var wide = chart.Settings.Jitter * across.Slot * plot.Width;
        var tall = chart.Settings.Jitter * up.Slot * plot.Height;

        return (((throws.NextDouble() * 2) - 1) * wide / 2, ((throws.NextDouble() * 2) - 1) * tall / 2);
    }

    /// <summary>How marks are coloured: which channel, and whether by group name or along a colour ramp.</summary>
    private sealed record Painting(PlotAesthetic Channel,
                                   IReadOnlyList<string> Groups,
                                   IReadOnlyDictionary<string, int> Order,
                                   DiagramSpan? Span,
                                   IReadOnlyList<Color> Stops)
    {
        /// <summary>Whether the colour is read as a quantity rather than as one of a few.</summary>
        public bool Counts => this.Span is not null;
    }

    /// <summary>Picks the colour channel (fill if mapped, else colour) and whether it runs along a ramp or
    /// by group.</summary>
    private Painting Paint(PlotChart chart, List<Diagnostic> trouble)
    {
        var it = chart.Settings;

        var channel = chart.Marks.Any(mark => mark[PlotAesthetic.Fill] is not null)
            ? PlotAesthetic.Fill
            : PlotAesthetic.Colour;

        // Binned plots colour by count regardless of columns — the count range is settled once bins are cut.
        if (it.Geom is PlotGeom.Bin2d or PlotGeom.Hex or PlotGeom.Density2d)
            return new Painting(channel, [], Ordered([]), DiagramSpan.Of(1, 2, widen: false), this.Ramp(it, trouble));

        // A correlation coefficient runs -1..1 about a meaningful zero, so the ramp is always diverging and centred.
        if (it.Geom == PlotGeom.Corr)
        {
            var (least, most) = it.FillLimits ?? (-1.0, 1.0);

            return new Painting(channel, [], Ordered([]), DiagramSpan.Of(least, most, widen: false),
                                this.Ramp(it with { Midpoint = it.Midpoint ?? 0 }, trouble));
        }

        if (!chart.Counts(channel))
                {
                    var groups = chart.Named(channel);
                    return new Painting(channel, groups, Ordered(groups), null, []);
                }

        var reach = it.FillLimits ?? chart.Reach(channel) ?? (Min: 0.0, Max: 1.0);

        // An explicit midpoint means the number has a meaningful zero, so the ramp centres on it rather than
        // the data average.
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

    /// <summary>Draws rows as counts in bins. A bin has no source part — like a gridline, nobody typed it —
    /// its colour is the count.</summary>
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

            // Binned in panel space (not value space) so hexagons stay regular regardless of axis scale.
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

    /// <summary>Draws a kernel density estimate as filled or line contours. Like a bin, a contour has no
    /// source part.</summary>
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

    /// <summary>Draws contours filled thinnest-first (so the cloud appears to thicken) or as lines.</summary>
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

    /// <summary>Draws a mark per row. Returns labels rather than drawing them, so all labels land in one
    /// layer above every mark — avoids a label being covered by the next tile.</summary>
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
                // Skip rows with no placeable value (e.g. mid-edit) — the rest still renders as a plot.
                trouble.Add(new Diagnostic(mark.Part.Start, Math.Max(1, mark.Part.Length),
                                           DiagnosticSeverity.Warning,
                                           "This row has no place: it takes a value across and a value up."));
                continue;
            }

            var (shakeAcross, shakeUp) = Shake(chart, mark, plot, across, up);

                    // Clamp jitter to the panel so a shaken mark never lands past what the axis shows.
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

            // `labels: true` writes the colour's value; a label column overrides it per mark.
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

    /// <summary>A tile per column pair, coloured by their correlation coefficient. No source part — the
    /// coefficient isn't a written cell — but the axis names are.</summary>
    private List<(DiagramWords Words, Point At)> Correlated(PlotChart chart, LayoutBuilder build, Rect plot,
                                                            Placing across, Placing up, Painting paint)
    {
        var labels = new List<(DiagramWords Words, Point At)>();

        build.Open(PlotPiece.Marks, part: null, stops: Stops.None);

        foreach (var (name, down, r) in chart.Correlations)
        {
            if (across.Of?.Invoke(name) is not { } x) continue;
            if (up.Of?.Invoke(down) is not { } y) continue;

            var at = new Point(plot.Left + (x * plot.Width), plot.Bottom - (y * plot.Height));

            var box = new Rect(at.X - (across.Slot * plot.Width / 2), at.Y - (up.Slot * plot.Height / 2),
                               across.Slot * plot.Width, up.Slot * plot.Height);

            var outline = DiagramShapes.Outline(DiagramShape.Rectangle, box);
            var fill = _ink.Scale(paint.Stops, paint.Span?.At(r) ?? 0.5);

            build.Open(PlotPiece.Mark, part: null, stops: Stops.None);
            build.Draw(new GeometryMark(outline, fill, null, 0));
            build.Occupies(outline);
            build.Close();

            if (!chart.Settings.Labels) continue;

            var words = this.Worked(PlotNumber.Written(Math.Round(r, 2)), null, LabelSize, _ink.Over(fill));
            labels.Add((words, new Point(at.X - (words.Width / 2), at.Y - (words.Height / 2))));
        }

        build.Close();

        return labels;
    }

    /// <summary>A fitted line and what it is drawn in — its group's colour, so overlapping bands are told apart.</summary>
    private sealed record Fitting(PlotLine Line, Brush Ink);

    /// <summary>Fits lines through the points. Fitted in panel space so it follows a log axis directly;
    /// statistics are computed from the raw values since screen-down and number-down disagree.</summary>
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

            // A single fit uses the accent colour; per-group fits use the group's own colour so overlapping
            // bands stay distinguishable.
            lines.Add(new Fitting(line, grouped ? inks[group[0]] : _palette.Accent));
        }

        return lines;
    }

    /// <summary>Vertical reach of the fitted confidence bands, in axis units, so the axis can widen to show
    /// them fully — a band is part of the answer, not decoration cropped to the panel (ggplot2 does the
    /// same). Skipped when the block set explicit limits. Computed in the axis's own transform since the
    /// panel isn't laid out yet.</summary>
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

    /// <summary>Computes and writes the correlation statistics from the raw values (not panel coordinates).</summary>
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

    /// <summary>Draws the fit's confidence band under the marks (so it doesn't grey out the points it's
    /// about), clipped to the panel.</summary>
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

    /// <summary>Groups row indices by fit group — indices not points, so callers can look up other per-row
    /// data like colour — preserving first-seen order.</summary>
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

    /// <summary>Writes the stats figures top-left, where a correlation plot's points rarely land.</summary>
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

    /// <summary>Maps each name to its first-seen order. A dictionary, not a linear search, since this is
    /// looked up once per mark.</summary>
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

    /// <summary>What every panel of a plot is drawn the same way from, worked out once over all the rows.</summary>
    private sealed record Panelling(Placing Across, Placing Up, Painting Paint,
                                    IReadOnlyDictionary<string, int> Shapes,
                                    DiagramStroke Faint,
                                    bool Tiles, bool Bins, bool Cloud, bool Corr);

    /// <summary>Draws one panel: gridlines, marks, fit, and stats for the rows that fell in it.
    /// <paramref name="how"/> stays plot-wide across facets so panels read consistently against each
    /// other.</summary>
    private (List<(DiagramWords Words, Point At)> Labels, DiagramSpan? Counts) Inside(
        PlotChart chart, LayoutBuilder build, Rect panel, Panelling how, List<Diagnostic> trouble)
    {
        var it = chart.Settings;
        var labels = new List<(DiagramWords Words, Point At)>();
        DiagramSpan? counts = null;

        // Behind everything, because a gridline is there to be read past.
        if (!how.Tiles && !how.Corr && it.Grid is PlotGrid.Both or PlotGrid.Y)
            DiagramGrid.Draw(build, PlotPiece.Grid, panel, how.Up.Ticks, upright: true, how.Faint);

        if (!how.Tiles && !how.Corr && it.Grid is PlotGrid.Both or PlotGrid.X)
            DiagramGrid.Draw(build, PlotPiece.Grid, panel, how.Across.Ticks, upright: false, how.Faint);

        // Bands go under the marks so they don't grey out the points they explain.
        IReadOnlyList<Fitting> fits = how.Bins || how.Cloud || how.Corr
            ? []
            : this.Fits(chart, panel, how.Across, how.Up, how.Paint);

        this.Banded(build, fits, panel);

        if (how.Corr)
        {
            labels = this.Correlated(chart, build, panel, how.Across, how.Up, how.Paint);
        }
        else if (how.Bins)
        {
            counts = this.Binned(chart, build, panel, how.Across, how.Up, how.Paint.Stops, trouble);
        }
        else if (how.Cloud)
        {
            this.Clouded(chart, build, panel, how.Across, how.Up, how.Paint.Stops, trouble);

            // The rows themselves over the cloud, where the block asks for both.
            if (it.Points)
                this.Drawn(chart, build, panel, how.Across, how.Up, how.Paint, how.Shapes, false, [], over: true);
        }
        else
        {
            labels = this.Drawn(chart, build, panel, how.Across, how.Up, how.Paint, how.Shapes, how.Tiles, trouble);
        }

        // Over the marks and under the axes: worked out from them, and never over the numbers.
        this.Traced(build, fits, panel);

        if (!how.Bins && !how.Cloud && !how.Corr) this.Reported(chart, build, panel);

        return (labels, counts);
    }

    /// <summary>
    /// The panels a faceted plot is divided into, each with the strip over it that names its level.
    /// </summary>
    /// <param name="cols">How many stand side by side.</param>
    /// <param name="strip">How tall the name over each one is.</param>
    private static IReadOnlyList<(Rect Panel, Rect Strip)> Divided(Rect plot, int count, int cols,
                                                                   double strip, double gap)
    {
        var rows = (int)Math.Ceiling(count / (double)cols);

        var wide = Math.Max(1, (plot.Width - (gap * (cols - 1))) / cols);
        var tall = Math.Max(1, (plot.Height - (gap * (rows - 1))) / rows);

        var cells = new List<(Rect, Rect)>(count);

        for (var at = 0; at < count; at++)
        {
            var left = plot.Left + ((at % cols) * (wide + gap));
            var top = plot.Top + ((at / cols) * (tall + gap));

            cells.Add((new Rect(left, top + strip, wide, Math.Max(1, tall - strip)),
                       new Rect(left, top, wide, strip)));
        }

        return cells;
    }

    private Laid Lay(PlotChart chart)
    {
        var it = chart.Settings;

        var (wide, tall) = SettingRoom.Fit(it.Width, it.Height, Room, PlotSettings.HeightShare);

        var trouble = new List<Diagnostic>();

        // A tile stands for one value rather than a stretch of them, so its axes are laid out in slots.
        var tiles = it.Geom == PlotGeom.Tile;
        var bins = it.Geom is PlotGeom.Bin2d or PlotGeom.Hex;
        var cloud = it.Geom == PlotGeom.Density2d;
        var corr = it.Geom == PlotGeom.Corr;

        // Correlation matrix axes are the columns themselves; the vertical axis is reversed so the diagonal
        // runs from the top-left.
        var pairs = corr ? chart.Correlations.Select(pair => pair.Across).Distinct().ToList() : [];

        // A panel per value the facet column takes. One value is no division at all, so it is left alone.
        var levels = it.Facet is null || corr ? [] : chart.Named(PlotAesthetic.Facet);
        var faceted = levels.Count > 1;

        // it.Flip swaps which channel reads across vs up; downstream code just uses acrossChannel/upChannel.
        var acrossChannel = it.Flip ? PlotAesthetic.Y : PlotAesthetic.X;
        var upChannel = it.Flip ? PlotAesthetic.X : PlotAesthetic.Y;

        var across = corr
            ? this.Slotted(acrossChannel, pairs)
            : this.Along(chart, acrossChannel, it.XScale, it.XLimits, it.XBreaks, tiles);

        // Axis opens out to show the full confidence band, unless the block set explicit limits.
        var banding = tiles || bins || cloud || corr || it.YLimits is not null
            ? null
            : this.Banding(chart, acrossChannel, upChannel,
                           Spanned(chart, acrossChannel, it.XScale, it.XLimits),
                           Spanned(chart, upChannel, it.YScale, it.YLimits));

        var up = corr
            ? this.Slotted(upChannel, [.. Enumerable.Reverse(pairs)])
            : this.Along(chart, upChannel, it.YScale, it.YLimits, it.YBreaks, tiles, banding);

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

        // Titles follow their channel (xTitle: labels whatever x: maps), not their screen side, so flip
        // carries both together.
        var xTitle = this.AxisTitle(chart, acrossChannel, it.Flip ? it.YTitle : it.XTitle);
        var yTitle = this.AxisTitle(chart, upChannel, it.Flip ? it.XTitle : it.YTitle);

        // Bin counts aren't known until bins are cut, so reserve bar-key room now and fill in numbers later.
        var key = bins || cloud
            ? this.Counting(DiagramSpan.Of(1, 2, widen: false), paint.Stops)
            : corr && paint.Span is { } coefficients
                ? this.Counting(coefficients, paint.Stops, whole: false)
                : this.Key(chart, paint);

        // Round the panel: the axes' own room, and beyond it the title band over, the caption under, and the
        // key on whichever side was asked for.
        var edges = DiagramPanel.Room(up.Ticks, across.Ticks, yTitle, xTitle, Gap)
                    + new DiagramEdges(
                        Left: it.Legend == PlotLegend.Left ? key.Size.Width + (Gap * 2) : 0,
                        Top: (title is null ? Gap : title.Height + Gap)
                             + (subtitle is null ? (title is null ? 0 : Gap) : subtitle.Height + Gap)
                             + (it.Legend == PlotLegend.Top ? key.Size.Height + Gap : 0),
                        Right: it.Legend == PlotLegend.Right ? key.Size.Width + (Gap * 2) : 0,
                        Bottom: (caption is null ? 0 : Gap + caption.Height)
                                + (it.Legend == PlotLegend.Bottom ? key.Size.Height + (Gap * 2) : 0));

        // What is given back is what was actually drawn, rather than the room offered.
        var round = DiagramPanel.Round(wide, tall, edges, it.Aspect, shrink: true);

        var plot = round.Plot;
        (wide, tall) = (round.Wide, round.Tall);

        var build = new LayoutBuilder();
        build.Open(PlotPiece.Plot);
        build.Covers(new Rect(0, 0, wide, tall));

        var rule = new DiagramStroke(_palette.CodeBorder);
        var faint = new DiagramStroke(DiagramInk.Faded(_palette.CodeBorder, 0.45));

        var how = new Panelling(across, up, paint, shapes, faint, tiles, bins, cloud, corr);

        var labels = new List<(DiagramWords Words, Point At)>();
        var strips = new List<(DiagramWords Words, Point At)>();

        if (!faceted)
        {
            var (said, counts) = this.Inside(chart, build, plot, how, trouble);

            labels.AddRange(said);
            if (counts is not null) key = this.Counting(counts, paint.Stops);
            if (cloud) key = new Chart(null, null);

            this.Axes(build, plot, across, up, rule);
        }
        else
        {
            var cols = Math.Clamp(it.FacetCols ?? (int)Math.Ceiling(Math.Sqrt(levels.Count)), 1, levels.Count);
            var over = this.Worked(levels[0], null, LabelSize, _palette.Text).Height + Gap;

            var cells = Divided(plot, levels.Count, cols, over, Gap * 2);

            for (var at = 0; at < levels.Count; at++)
            {
                var level = levels[at];
                var (panel, strip) = cells[at];

                // Its own rows, on everybody's scales.
                var only = chart with
                {
                    Marks = [.. chart.Marks.Where(mark => mark[PlotAesthetic.Facet]?.Text == level)],
                };

                var (said, _) = this.Inside(only, build, panel, how, trouble);
                labels.AddRange(said);

                // Axis numbers only on the outside (first column, bottom row) — elsewhere they'd sit between panels.
                this.Axes(build, panel, across, up, rule,
                          side: at % cols == 0,
                          foot: at + cols >= levels.Count);

                var says = this.Worked(level, null, LabelSize, _palette.Text);
                strips.Add((says, new Point(strip.Left + Math.Max(0, (strip.Width - says.Width) / 2),
                                            strip.Top + Math.Max(0, (strip.Height - Gap - says.Height) / 2))));
            }
        }

        if (labels.Count > 0)
        {
            build.Open(PlotPiece.Labels, part: null, stops: Stops.None);
            foreach (var (words, where) in labels) words.Set(build, where, PlotPiece.Label);
            build.Close();
        }

        foreach (var (words, where) in strips) words.Set(build, where, PlotPiece.Strip);

        title?.Set(build, new Point(plot.Left + Math.Max(0, (plot.Width - title.Width) / 2), Gap),
                   PlotPiece.Title);

        subtitle?.Set(build, new Point(plot.Left + Math.Max(0, (plot.Width - subtitle.Width) / 2),
                                       (title?.Height ?? 0) + Gap),
                      PlotPiece.Title);

        caption?.Set(build, new Point(plot.Left, tall - caption.Height), PlotPiece.Title);

        round.Titles(build, PlotPiece.AxisTitle, yTitle, xTitle,
                     DiagramAxis.Room(across.Ticks, upright: false) + Gap);

        if (key.Size.Height > 0) key.Draw(build, this.Where(it.Legend, key.Size, plot, wide, tall));

        build.Close();

        return new Laid(build.Seal(), new Size(wide, tall), trouble);
    }

    /// <summary>The numbers up a panel's side and along its foot.</summary>
    private void Axes(LayoutBuilder build, Rect panel, Placing across, Placing up, DiagramStroke rule,
                      bool side = true, bool foot = true)
    {
        if (side)
            DiagramAxis.Draw(build, PlotPiece.YAxis, null, panel.BottomLeft, panel.TopLeft, up.Ticks, rule,
                             PlotPiece.Tick, after: false);

        if (foot)
            DiagramAxis.Draw(build, PlotPiece.XAxis, null, panel.BottomLeft, panel.BottomRight, across.Ticks,
                             rule, PlotPiece.Tick, after: true);
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

    /// <summary>The key: a row per group for categorical colour, a colour bar for a quantity.</summary>
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
            // Drawn as plain words, not a key row — a swatch-less row would read as "no colour chosen", not
            // a heading.
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
            // Bar labels: start, end, and midpoint (the value the colour ramp centres on, if any).
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
    private Chart Counting(DiagramSpan counts, IReadOnlyList<Color> stops, bool whole = true)
    {
        var marks = new[] { 0.0, 0.5, 1.0 }
            .Select(at => (At: at, Value: Round(counts.Min + (at * (counts.Max - counts.Min)), whole)))
            .Select(mark => (mark.At, Words: this.Worked(DiagramScale.Plain(mark.Value), null, LabelSize, _palette.Text)))
            .ToList();

        return new Chart(null, new DiagramBar(stops, marks, _palette.CodeBorder));
    }

    /// <summary>Rounds a colour-bar number: whole for counts, two decimals for a coefficient.</summary>
    private static double Round(double value, bool whole) =>
        whole ? Math.Round(value) : Math.Round(value, 2);

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

    /// <summary>Words backed by source characters — what makes the plot editable (caret, drag-select,
    /// type-to-edit). A computed value like a tick number uses <see cref="Worked"/> instead.</summary>
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
            Editing.LayoutText.Density);

    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(SourceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            SourceSize,
            Brushes.Black,
            Editing.LayoutText.Density);

    /// <summary>The block shown as written, with the reason it could not be drawn.</summary>
    private Laid Stopped(string reason) =>
        LayoutText.Shown(Source, this.Characters(Source.Length == 0 ? " " : Source),
                         [new Diagnostic(0, Math.Max(Source.Length, 1), DiagnosticSeverity.Error, reason)]);
}
