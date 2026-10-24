using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Layout;

namespace Nexaflow.Tests.Visuals.Editing.Portable;

/// <summary>
/// Paints a renderer-free layout tree with WPF — the same two descents, the same scopes and the same kept pictures as the WPF
/// tree's painter, with each mark handed back to the medium that made what it holds.
/// </summary>
internal static class PortablePainter
{
    public static void Paint(DrawingContext dc, PortablePiece piece, WpfInk foreground, Rect? showing = null)
    {
        if (!piece.Exists) return;

        var near = showing is { } shown ? Around(shown) : (Rect?)null;
        var far = showing is { } seen ? Beyond(seen) : (Rect?)null;

        Descend(dc, piece, foreground, washes: true, default, near, far);
        Descend(dc, piece, foreground, washes: false, default, near, far);
    }

    private static Rect Around(Rect showing) => new(-Wide, showing.Y - showing.Height, Wide * 2, showing.Height * 3);

    private static Rect Beyond(Rect showing) => new(-Wide, showing.Y - (showing.Height * 4), Wide * 2, showing.Height * 9);

    private const double Wide = 1e9;

    private static void Descend(DrawingContext dc, PortablePiece piece, WpfInk foreground, bool washes, Vector offset,
                                Rect? near = null, Rect? far = null)
    {
        var painting = piece.Painting;

        if (painting is { Opacity: <= 0 }) return;

        var by = piece.Offset;
        offset += new Vector(by.X, by.Y);
        var standing = offset;
        var pushed = Enter(dc, painting, ref offset);

        if (painting?.Turn is { Count: > 0 }) near = far = null;
        else if (offset != standing)
        {
            near = Moved(near, offset - standing);
            far = Moved(far, offset - standing);
        }

        if (painting?.Kept is { } kept)
        {
            if (washes)
            {
                var box = piece.Box.IsEmpty ? Rect.Empty : Rect.Offset(piece.Box.ToWpf(), offset);

                if (near is not { } painted || painted.IntersectsWith(box))
                    Recorded(dc, kept.For(foreground, () => Record(piece, foreground)), offset);
                else if (far is { } beyond && !beyond.IntersectsWith(box))
                    kept.Forget();
            }
        }
        else
        {
            foreach (var mark in piece.Marks)
                if (mark is PortableWashMark == washes) PaintMark(dc, mark, foreground.Brush, offset);

            foreach (var child in piece.Children) Descend(dc, child, foreground, washes, offset, near, far);
        }

        for (var at = 0; at < pushed; at++) dc.Pop();
    }

    private static Drawing Record(PortablePiece piece, WpfInk foreground)
    {
        var recorded = new DrawingGroup();
        using (var dc = recorded.Open()) Inside(dc, piece, foreground);
        if (recorded.CanFreeze) recorded.Freeze();

        return recorded;
    }

    private static void Inside(DrawingContext dc, PortablePiece piece, WpfInk foreground)
    {
        foreach (var mark in piece.Marks)
            if (mark is PortableWashMark) PaintMark(dc, mark, foreground.Brush, default);

        foreach (var child in piece.Children) Descend(dc, child, foreground, washes: true, default);

        foreach (var mark in piece.Marks)
            if (mark is not PortableWashMark) PaintMark(dc, mark, foreground.Brush, default);

        foreach (var child in piece.Children) Descend(dc, child, foreground, washes: false, default);
    }

    private static void Recorded(DrawingContext dc, Drawing picture, Vector offset)
    {
        if (offset is { X: 0, Y: 0 })
        {
            dc.DrawDrawing(picture);

            return;
        }

        dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
        dc.DrawDrawing(picture);
        dc.Pop();
    }

    /// <summary>
    /// One mark, drawn moved along by <paramref name="offset"/>, as the WPF tree's own marks draw themselves: what can be drawn
    /// further along is, and what cannot is moved by a transform of its own.
    /// </summary>
    private static void PaintMark(DrawingContext dc, PortableMark mark, Brush fallback, Vector offset)
    {
        switch (mark)
        {
            case PortableTextMark text:
            {
                var glyphs = (WpfText)text.Glyphs;
                glyphs.SetForegroundBrush(Brush(text.Ink) ?? fallback);
                dc.DrawText(glyphs, new Point(text.At.X + offset.X, text.At.Y + offset.Y));
                break;
            }

            case PortableGeometryMark shape:
            {
                var pen = shape.Stroke is WpfStroke stroke ? stroke.PenFor(fallback) : null;
                var fill = shape.Fill is null && pen is not null ? null : Brush(shape.Fill) ?? fallback;

                var moved = Shifted(dc, offset);
                dc.DrawGeometry(fill, pen, ((WpfShape)shape.Shape).Geometry);
                if (moved) dc.Pop();
                break;
            }

            case PortableRuleMark rule:
                dc.DrawRectangle(Brush(rule.Ink) ?? fallback, null, Rect.Offset(rule.Bounds.ToWpf(), offset));
                break;

            case PortableLineMark line:
            {
                var pen = new Pen(Brush(line.Ink) ?? fallback, 1.0);
                pen.Freeze();
                dc.DrawLine(pen, line.From.ToWpf() + offset, line.To.ToWpf() + offset);
                break;
            }

            case PortableGlyphMark glyphs:
            {
                var moved = Shifted(dc, offset);
                dc.DrawGlyphRun(Brush(glyphs.Ink) ?? fallback, (WpfGlyphs)glyphs.Run);
                if (moved) dc.Pop();
                break;
            }

            case PortableWashMark wash:
                dc.DrawRectangle(((WpfInk)wash.Fill).Brush, null, Rect.Offset(wash.Bounds.ToWpf(), offset));
                break;

            case PortablePictureMark picture:
            {
                var drawing = new DrawingGroup();
                RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.HighQuality);
                RenderOptions.SetEdgeMode(drawing, EdgeMode.Aliased);
                drawing.Children.Add(new ImageDrawing(((WpfPicture)picture.Picture).Source, Rect.Offset(picture.Bounds.ToWpf(), offset)));
                if (drawing.CanFreeze) drawing.Freeze();
                dc.DrawDrawing(drawing);
                break;
            }
        }
    }

    private static Brush? Brush(LayoutInk? ink) => ((WpfInk?)ink)?.Brush;

    /// <summary>Moves what is drawn next along by <paramref name="offset"/> where it is not drawn where it was made, and says whether it pushed.</summary>
    private static bool Shifted(DrawingContext dc, Vector offset)
    {
        if (offset is { X: 0, Y: 0 }) return false;

        dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
        return true;
    }

    private static int Enter(DrawingContext dc, PortablePaint? painting, ref Vector offset)
    {
        if (painting is null) return 0;

        var pushed = 0;

        if (Framed(painting) && offset is not { X: 0, Y: 0 })
        {
            dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
            pushed++;
            offset = default;
        }

        if (painting.Turn is { } turns)
            foreach (var turn in turns)
            {
                dc.PushTransform(((WpfTurn)turn).Transform);
                pushed++;
            }

        if (painting.Scale != 1)
        {
            dc.PushTransform(new ScaleTransform(painting.Scale, painting.Scale));
            pushed++;
        }

        if (painting.Snap is WpfGuides snap)
        {
            dc.PushGuidelineSet(snap.Set);
            pushed++;
        }

        if (painting.Clip is WpfShape clip)
        {
            dc.PushClip(clip.Geometry);
            pushed++;
        }

        if (painting.Opacity < 1)
        {
            dc.PushOpacity(painting.Opacity);
            pushed++;
        }

        if (painting.Mask is WpfInk mask)
        {
            dc.PushOpacityMask(mask.Brush);
            pushed++;
        }

        return pushed;
    }

    private static bool Framed(PortablePaint painting) =>
        painting.Applies || painting.Snap is not null || painting.Clip is not null || painting.Mask is not null;

    private static Rect? Moved(Rect? area, Vector by) => area is { } rect ? Rect.Offset(rect, by) : null;
}
