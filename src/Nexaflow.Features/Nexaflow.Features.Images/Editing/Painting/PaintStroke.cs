using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Selection;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Painting;

/// <summary>What a stroke does where it lands: lays colour down, or takes it away.</summary>
public enum PaintKind
{
    Brush,
    Eraser,
}

/// <summary>The round brush: its tip, and how much each dab and the stroke as a whole may cover.</summary>
public sealed record BrushSettings
{
    public const double MinSize = 1;
    public const double MaxSize = 1000;

    /// <summary>The tip's diameter, in canvas pixels.</summary>
    public double Size { get; init; } = 24;

    /// <summary>How much of the radius is solid, 0–1; the rest fades to the edge.</summary>
    public double Hardness { get; init; } = 0.8;

    /// <summary>The most a stroke covers, 0–1, however often it passes over a place.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>How much each dab adds toward <see cref="Opacity"/>, 0–1.</summary>
    public double Flow { get; init; } = 1;

    /// <summary>The distance between dabs, as a share of the size.</summary>
    public double Spacing { get; init; } = 0.15;

    /// <summary>Pen pressure scales the tip.</summary>
    public bool PressureSizes { get; init; } = true;

    /// <summary>Pen pressure scales how much the stroke covers.</summary>
    public bool PressureOpacity { get; init; }
}

/// <summary>
/// One stroke of the brush or the eraser over a layer's pixels or its mask. Dabs are stamped along the pointer at the spacing
/// into a stroke mask, each adding its flow toward the stroke's opacity, so a stroke laid over itself never builds past it; the
/// layer as it was when the stroke began is then drawn through that mask, only where the selection lets it. The mask is kept per
/// 256-pixel tile, so a stroke costs what it touched.
/// </summary>
public sealed class PaintStroke
{
    private const int TileSize = 256;
    private const int TileShift = 8;
    private const int TileMask = TileSize - 1;

    private readonly Dictionary<(int Column, int Row), float[]> _mask = [];
    private readonly BrushSettings _brush;
    private readonly int _width;
    private readonly int _height;
    private readonly TiledCoverage? _within;
    private SKPoint? _last;
    private float _lastPressure;
    private double _sinceDab;

    /// <param name="within">Where the stroke may land, as coverage over the canvas; null for anywhere.</param>
    public PaintStroke(BrushSettings brush, PaintKind kind, int width, int height, TiledCoverage? within)
    {
        _brush  = brush;
        Kind    = kind;
        _width  = width;
        _height = height;
        _within = within;
    }

    public PaintKind Kind { get; }

    /// <summary>
    /// The pointer reached <paramref name="at"/> (canvas pixels) at <paramref name="pressure"/> (0–1; 1 for a mouse): the dabs
    /// between where it was and here are stamped. Gives the canvas area they touched; empty where none fell.
    /// </summary>
    public SKRectI To(SKPoint at, float pressure)
    {
        pressure = Math.Clamp(pressure, 0.01f, 1f);
        if (_last is not { } from)
        {
            _last = at;
            _lastPressure = pressure;
            _sinceDab = 0;
            return Dab(at, pressure);
        }

        double step = Math.Max(0.5, _brush.Spacing * _brush.Size);
        double dx = at.X - from.X, dy = at.Y - from.Y, length = Math.Sqrt(dx * dx + dy * dy);
        var touched = SKRectI.Empty;
        double along = step - _sinceDab;
        if (along > length) _sinceDab += length;
        else
        {
            for (; along <= length; along += step)
            {
                double t = length == 0 ? 1 : along / length;
                var dab = Dab(new SKPoint((float)(from.X + dx * t), (float)(from.Y + dy * t)), (float)(_lastPressure + (pressure - _lastPressure) * t));
                touched = touched.IsEmpty ? dab : dab.IsEmpty ? touched : SKRectI.Union(touched, dab);
            }
            _sinceDab = length - (along - step);
        }
        _last = at;
        _lastPressure = pressure;
        return touched;
    }

    // One dab of the tip centred on c: its coverage added toward the stroke's opacity.
    private SKRectI Dab(SKPoint c, float pressure)
    {
        double radius = Math.Max(0.5, _brush.Size * (_brush.PressureSizes ? pressure : 1) / 2);
        double solid  = radius * Math.Clamp(_brush.Hardness, 0, 1);
        float cap     = (float)(Math.Clamp(_brush.Opacity, 0, 1) * (_brush.PressureOpacity ? pressure : 1));
        double flow   = Math.Clamp(_brush.Flow, 0, 1);
        var area = SKRectI.Intersect(new SKRectI((int)Math.Floor(c.X - radius - 1), (int)Math.Floor(c.Y - radius - 1),
                                                 (int)Math.Ceiling(c.X + radius + 1), (int)Math.Ceiling(c.Y + radius + 1)),
                                     new SKRectI(0, 0, _width, _height));
        if (area.IsEmpty || cap <= 0 || flow <= 0) return SKRectI.Empty;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            double py = y + 0.5 - c.Y;
            for (int x = area.Left; x < area.Right; x++)
            {
                double px = x + 0.5 - c.X, d = Math.Sqrt(px * px + py * py);
                if (d >= radius + 0.5) continue;
                double a;
                if (d <= solid) a = 1;
                else if (d >= radius) a = 0;
                else
                {
                    double t = (d - solid) / (radius - solid);
                    a = 1 - t * t * (3 - 2 * t);
                }
                a *= Math.Clamp(radius + 0.5 - d, 0, 1) * flow;   // the edge anti-aliased, then the flow
                if (a <= 0) continue;
                var tile = Tile(x >> TileShift, y >> TileShift);
                int i = (y & TileMask) * TileSize + (x & TileMask);
                float m = tile[i];
                if (m < cap) tile[i] = m + (float)((cap - m) * a);
            }
        }
        return area;
    }

    private float[] Tile(int column, int row)
    {
        if (!_mask.TryGetValue((column, row), out var tile)) _mask[(column, row)] = tile = new float[TileSize * TileSize];
        return tile;
    }

    // How much the stroke covers each pixel of area, the selection taken in: 0–1, row by row.
    private float[] Strength(SKRectI area)
    {
        var strength = new float[area.Width * area.Height];
        byte[]? within = _within?.Read(area);
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                if (!_mask.TryGetValue((x >> TileShift, y >> TileShift), out var tile)) continue;
                int i = (y - area.Top) * area.Width + (x - area.Left);
                float m = tile[(y & TileMask) * TileSize + (x & TileMask)];
                strength[i] = within is null ? m : m * within[i] / 255f;
            }
        return strength;
    }

    /// <summary>
    /// A layer's pixels with the stroke drawn through <paramref name="area"/>: <paramref name="before"/>, the pixels when the
    /// stroke began, with <paramref name="colour"/> laid over — or taken away, for the eraser — as much as the stroke covers;
    /// written into <paramref name="current"/>. A layer whose transparency is locked keeps it: the brush recolours what is painted,
    /// the eraser paints <paramref name="background"/> into it.
    /// </summary>
    public TiledPixels Drawn(TiledPixels before, TiledPixels current, SKRectI area, SKColor colour, bool alphaLocked, SKColor background)
    {
        if (area.IsEmpty) return current;
        var strength = Strength(area);
        var pixels = before.Read(area);
        var ink = Kind == PaintKind.Brush ? colour : background;
        float inkA = ink.Alpha / 255f;
        for (int p = 0, i = 0; p < strength.Length; p++, i += 4)
        {
            float s = strength[p];
            if (s <= 0) continue;
            if (Kind == PaintKind.Eraser && !alphaLocked)
            {
                for (int c = 0; c < 4; c++) pixels[i + c] = (byte)Math.Round(pixels[i + c] * (1 - s));
                continue;
            }
            float srcA = inkA * s;
            float r = ink.Red / 255f * srcA, g = ink.Green / 255f * srcA, b = ink.Blue / 255f * srcA;
            if (alphaLocked)
            {
                // Source atop: the ink only where the layer already is, its transparency kept.
                float dstA = pixels[i + 3] / 255f;
                pixels[i]     = (byte)Math.Round(Math.Clamp(r * 255 * dstA + pixels[i] * (1 - srcA), 0, 255));
                pixels[i + 1] = (byte)Math.Round(Math.Clamp(g * 255 * dstA + pixels[i + 1] * (1 - srcA), 0, 255));
                pixels[i + 2] = (byte)Math.Round(Math.Clamp(b * 255 * dstA + pixels[i + 2] * (1 - srcA), 0, 255));
                continue;
            }
            pixels[i]     = (byte)Math.Round(Math.Clamp(r * 255 + pixels[i] * (1 - srcA), 0, 255));
            pixels[i + 1] = (byte)Math.Round(Math.Clamp(g * 255 + pixels[i + 1] * (1 - srcA), 0, 255));
            pixels[i + 2] = (byte)Math.Round(Math.Clamp(b * 255 + pixels[i + 2] * (1 - srcA), 0, 255));
            pixels[i + 3] = (byte)Math.Round(Math.Clamp(srcA * 255 + pixels[i + 3] * (1 - srcA), 0, 255));
        }
        return current.Written(area, pixels);
    }

    /// <summary>
    /// A mask with the stroke drawn through <paramref name="area"/>: <paramref name="before"/>, the mask when the stroke began,
    /// moved toward <paramref name="grey"/> — or, for the eraser, toward white, showing the layer again — as much as the stroke
    /// covers; written into <paramref name="current"/>.
    /// </summary>
    public TiledCoverage Drawn(TiledCoverage before, TiledCoverage current, SKRectI area, byte grey)
    {
        if (area.IsEmpty) return current;
        var strength = Strength(area);
        var values = before.Read(area);
        byte toward = Kind == PaintKind.Brush ? grey : (byte)255;
        for (int p = 0; p < strength.Length; p++)
        {
            float s = strength[p];
            if (s > 0) values[p] = (byte)Math.Round(values[p] + (toward - values[p]) * s);
        }
        return current.Written(area, values);
    }
}
