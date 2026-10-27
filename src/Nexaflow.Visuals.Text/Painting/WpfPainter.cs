using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Markdown.Layout;

namespace Nexaflow.Visuals.Text.Painting;

/// <summary>
/// Paints a layout tree with WPF — every mark of every piece, in the frame that piece was measured in, each handed back to the
/// medium that made what it holds.
///
/// <para>
/// It never asks what it is walking, which is the point of it. A canvas holding a bar of music beside a fraction beside a paragraph
/// is one tree of pieces that drew marks, so it paints in one descent and no content type has a painter of its own.
/// </para>
/// <para>
/// Descended rather than flattened, because a mark is recorded relative to its own piece's anchor and an anchor is relative to its
/// parent's. Where each piece stands is carried down the descent and handed to its marks rather than pushed as a transform, so a
/// picture being kept is marks rather than a group for every piece; only a piece drawn in a frame of its own — turned, snapped to a
/// pixel grid, clipped or masked — pushes one. The same relationship answers a hit test, so the picture and the answers cannot
/// disagree about where anything is.
/// </para>
/// </summary>
public static class WpfPainter
{
    /// <summary>
    /// Paints <paramref name="piece"/> and everything inside it, in its parent's frame — in two descents, always: every wash in the
    /// tree goes down, and then everything else over it. A wash is a background a piece asked for behind it, so it has to be under the
    /// ink of everything, not merely its own, and boxes overlap. A selection is not one of the layers: it goes on last, over everything.
    /// </summary>
    /// <param name="showing">
    /// The part of the page on screen, where only that is being looked at: a piece keeping a picture is painted only near it
    /// (<see cref="Around"/>), and one far from it lets its picture go — so a long document costs a screen of pictures however long it
    /// is. Null paints everything.
    /// </param>
    public static void Paint(DrawingContext dc, LayoutPiece piece, Brush ink, Rect? showing = null)
    {
        if (!piece.Exists) return;

        var foreground = WpfMedium.OnThisThread.Ink(ink);

        var near = showing is { } shown ? Around(shown) : (Rect?)null;
        var far = showing is { } seen ? Beyond(seen) : (Rect?)null;

        Descend(dc, piece, foreground, washes: true, default, near, far);
        Descend(dc, piece, foreground, washes: false, default, near, far);
    }

    /// <summary>
    /// What is painted round the part of the page on screen: a screen's worth above and below it, and the whole width, so a little
    /// scrolling shows what is already painted rather than asking for another paint.
    /// </summary>
    public static Rect Around(Rect showing) => new(-Wide, showing.Y - showing.Height, Wide * 2, showing.Height * 3);

    /// <summary>How far from what is shown a piece has to be before the picture kept of it is let go.</summary>
    private static Rect Beyond(Rect showing) => new(-Wide, showing.Y - (showing.Height * 4), Wide * 2, showing.Height * 9);

    /// <summary>Wider than any page, and finite: a rectangle running to infinity has no right-hand edge to be inside.</summary>
    private const double Wide = 1e9;

    /// <summary>
    /// Paints one piece where it sits on the page, rather than where it sits in its parent — for a caller redrawing part of a tree on
    /// its own. Everything holding it is entered first and draws nothing, because a piece's place is relative and its frame is
    /// therefore the thing above it.
    /// </summary>
    public static void PaintOne(DrawingContext dc, LayoutPiece piece, Brush ink)
    {
        if (!piece.Exists) return;

        var foreground = WpfMedium.OnThisThread.Ink(ink);
        var above = new List<LayoutPiece>();
        foreach (var up in piece.Ancestors()) above.Add(up);

        var offset = default(Vector);
        var pushed = 0;

        for (var at = above.Count - 1; at >= 0; at--)
        {
            var by = above[at].Offset;
            offset += new Vector(by.X, by.Y);
            pushed += Enter(dc, above[at].Painting, ref offset);
        }

        Descend(dc, piece, foreground, washes: true, offset);
        Descend(dc, piece, foreground, washes: false, offset);

        for (var at = 0; at < pushed; at++) dc.Pop();
    }

    /// <summary>
    /// <paramref name="root"/> painted into a picture of <paramref name="box"/>, <paramref name="scale"/> times as large, on
    /// <paramref name="ground"/> where one is said — what a figure is exported as, and what a block is copied as.
    /// </summary>
    public static BitmapSource? Painted(LayoutPiece root, Rect box, double scale, Brush ink, Brush? ground, double dpiX = 96, double dpiY = 96)
    {
        if (box.IsEmpty || box.Width <= 0 || box.Height <= 0 || scale <= 0) return null;

        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            if (ground is not null) dc.DrawRectangle(ground, null, new Rect(0, 0, box.Width * scale, box.Height * scale));

            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-box.X, -box.Y));
            dc.PushClip(new RectangleGeometry(box));
            Paint(dc, root, ink);
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(box.Width * scale * dpiX / 96)),
            Math.Max(1, (int)Math.Ceiling(box.Height * scale * dpiY / 96)),
            dpiX, dpiY, PixelFormats.Pbgra32);

        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>A picture, as the bytes of a PNG file.</summary>
    public static byte[] Png(BitmapSource picture)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));

        using var bytes = new MemoryStream();
        encoder.Save(bytes);

        return bytes.ToArray();
    }

    /// <summary>One layer of one subtree — see <see cref="Paint"/>, which is both of them.</summary>
    /// <param name="offset">Where the frame <paramref name="piece"/> is measured in stands, against what was last pushed.</param>
    /// <param name="near">What is painted of the pieces keeping pictures, on the page — null for all of them.</param>
    /// <param name="far">Past this, a piece's kept picture is let go.</param>
    private static void Descend(DrawingContext dc, LayoutPiece piece, WpfInk foreground, bool washes, Vector offset,
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
                if (mark is WashMark == washes) PaintMark(dc, mark, foreground.Brush, offset);

            foreach (var child in piece.Children) Descend(dc, child, foreground, washes, offset, near, far);
        }

        for (var at = 0; at < pushed; at++) dc.Pop();
    }

    /// <summary>A kept picture, recorded from everything the piece holds.</summary>
    private static Drawing Record(LayoutPiece piece, WpfInk foreground)
    {
        var recorded = new DrawingGroup();
        using (var dc = recorded.Open()) Inside(dc, piece, foreground);
        if (recorded.CanFreeze) recorded.Freeze();

        return recorded;
    }

    /// <summary>Everything a piece holds, both layers, in the piece's own frame — what a kept picture is recorded from.</summary>
    private static void Inside(DrawingContext dc, LayoutPiece piece, WpfInk foreground)
    {
        foreach (var mark in piece.Marks)
            if (mark is WashMark) PaintMark(dc, mark, foreground.Brush, default);

        foreach (var child in piece.Children) Descend(dc, child, foreground, washes: true, default);

        foreach (var mark in piece.Marks)
            if (mark is not WashMark) PaintMark(dc, mark, foreground.Brush, default);

        foreach (var child in piece.Children) Descend(dc, child, foreground, washes: false, default);
    }

    /// <summary>A kept picture, set down where its piece stands.</summary>
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
    private static void PaintMark(DrawingContext dc, LayoutMark mark, Brush fallback, Vector offset)
    {
        switch (mark)
        {
            case TextMark text:
            {
                var glyphs = (WpfText)text.Glyphs;
                glyphs.SetForegroundBrush(Brush(text.Ink) ?? fallback);
                dc.DrawText(glyphs, new Point(text.At.X + offset.X, text.At.Y + offset.Y));
                break;
            }

            case GeometryMark shape:
            {
                var pen = shape.Stroke is WpfStroke stroke ? stroke.PenFor(fallback) : null;
                var fill = shape.Fill is null && pen is not null ? null : Brush(shape.Fill) ?? fallback;

                var moved = Shifted(dc, offset);
                dc.DrawGeometry(fill, pen, ((WpfShape)shape.Shape).Geometry);
                if (moved) dc.Pop();
                break;
            }

            case RuleMark rule:
                dc.DrawRectangle(Brush(rule.Ink) ?? fallback, null, Rect.Offset(rule.Bounds.ToWpf(), offset));
                break;

            case LineMark line:
            {
                var pen = new Pen(Brush(line.Ink) ?? fallback, 1.0);
                pen.Freeze();
                dc.DrawLine(pen, line.From.ToWpf() + offset, line.To.ToWpf() + offset);
                break;
            }

            case GlyphMark glyphs:
            {
                var moved = Shifted(dc, offset);
                dc.DrawGlyphRun(Brush(glyphs.Ink) ?? fallback, (WpfGlyphs)glyphs.Run);
                if (moved) dc.Pop();
                break;
            }

            case WashMark wash:
                dc.DrawRectangle(((WpfInk)wash.Fill).Brush, null, Rect.Offset(wash.Bounds.ToWpf(), offset));
                break;

            case PictureMark picture:
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

    /// <summary>The brush an ink paints with, or null where a mark has none of its own.</summary>
    private static Brush? Brush(LayoutInk? ink) => ((WpfInk?)ink)?.Brush;

    /// <summary>Moves what is drawn next along by <paramref name="offset"/> where it is not drawn where it was made, and says whether it pushed.</summary>
    private static bool Shifted(DrawingContext dc, Vector offset)
    {
        if (offset is { X: 0, Y: 0 }) return false;

        dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
        return true;
    }

    /// <summary>
    /// Puts the drawing context into a piece's own frame where it is drawn in one — turned, scaled, snapped to a pixel grid, clipped
    /// or masked — scopes how much of it shows, and says how many pushes to undo. Where it sits first, then how it is drawn: a piece
    /// turned inside whatever holds it is turned about its own anchor, and a drawing set smaller is scaled inside the turn. A clip and
    /// a mask are said in the piece's own frame, inside however it is turned and scaled; a fade is the same in any frame.
    /// </summary>
    private static int Enter(DrawingContext dc, LayoutPaint? painting, ref Vector offset)
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

    /// <summary>Whether what a piece holds is painted measured from its own anchor rather than carried down to it.</summary>
    private static bool Framed(LayoutPaint painting) =>
        painting.Applies || painting.Snap is not null || painting.Clip is not null || painting.Mask is not null;

    /// <summary>A stretch of the page, said in a frame standing <paramref name="by"/> from the one it was said in.</summary>
    private static Rect? Moved(Rect? area, Vector by) => area is { } rect ? Rect.Offset(rect, by) : null;
}
