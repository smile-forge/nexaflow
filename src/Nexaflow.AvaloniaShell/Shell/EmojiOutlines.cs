using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using HarfBuzzSharp;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>
/// An emoji as one shape: the plain outlines Segoe UI Emoji carries for each of its glyphs beneath the colour layers — what WPF, which
/// cannot draw colour fonts, shows for an emoji, and so what the shell's icons look like. Skia renders the colour layers and gives no
/// outline for them, so the outlines are read here from the font file through HarfBuzz (the shaper Avalonia already ships), the text
/// shaped first so a sequence draws as the font joins it.
/// <para>Called on the UI thread; each shape is kept once made.</para>
/// </summary>
internal static class EmojiOutlines
{
    private static readonly Lazy<(HbFont Font, int UnitsPerEm)?> Font = new(Open);
    private static readonly Dictionary<(string Text, double Size), (Geometry Shape, Size Extent)?> Made = [];

    // The outline callbacks HarfBuzz calls, kept alive for the process: it holds them by pointer.
    private static readonly MoveTo MoveToCall = OnMoveTo;
    private static readonly MoveTo LineToCall = OnLineTo;
    private static readonly QuadraticTo QuadraticToCall = OnQuadraticTo;
    private static readonly CubicTo CubicToCall = OnCubicTo;
    private static readonly ClosePath ClosePathCall = OnClosePath;
    private static readonly Lazy<IntPtr> Calls = new(MakeCalls);

    /// <summary>The shape of <paramref name="text"/> at <paramref name="size"/>, with the box it sits in — its advance wide, the font's
    /// ascent and descent high — or null when the font is missing or has none of it.</summary>
    public static (Geometry Shape, Size Extent)? Of(string text, double size)
    {
        if (Made.TryGetValue((text, size), out var made)) return made;
        made = Make(text, size);
        Made[(text, size)] = made;
        return made;
    }

    private static (HbFont, int)? Open()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf");
        if (!File.Exists(file)) return null;
        var face = new Face(Blob.FromFile(file), 0);
        var font = new HbFont(face);
        font.SetFunctionsOpenType();
        return (font, face.UnitsPerEm);
    }

    private static (Geometry, Size)? Make(string text, double size)
    {
        if (Font.Value is not var (font, unitsPerEm)) return null;

        using var buffer = new HbBuffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        font.Shape(buffer);

        var infos = buffer.GlyphInfos;
        var places = buffer.GlyphPositions;
        if (infos.Length == 0 || infos.All(info => info.Codepoint == 0)) return null;

        font.TryGetHorizontalFontExtents(out var extents);
        var scale = size / unitsPerEm;
        var geometry = new StreamGeometry();
        double pen = 0;
        using (var context = geometry.Open())
        {
            context.SetFillRule(FillRule.NonZero);
            var drawing = new Drawing(context, scale, extents.Ascender);
            var handle = GCHandle.Alloc(drawing);
            try
            {
                for (var i = 0; i < infos.Length; i++)
                {
                    drawing.Origin = new Vector(pen + places[i].XOffset * scale, -places[i].YOffset * scale);
                    hb_font_draw_glyph(font.Handle, infos[i].Codepoint, Calls.Value, GCHandle.ToIntPtr(handle));
                    drawing.Finish();
                    pen += places[i].XAdvance * scale;
                }
            }
            finally { handle.Free(); }
        }
        return (geometry, new Size(pen, (extents.Ascender - extents.Descender) * scale));
    }

    /// <summary>One shaping's outlines going into a geometry: font units, y up, made device units, y down, from the glyph's origin.</summary>
    private sealed class Drawing(StreamGeometryContext context, double scale, int ascender)
    {
        private bool _open;

        public Vector Origin { get; set; }

        public void Move(float x, float y)
        {
            Finish();
            context.BeginFigure(At(x, y), isFilled: true);
            _open = true;
        }

        public void Line(float x, float y) => context.LineTo(At(x, y));

        public void Quadratic(float cx, float cy, float x, float y) => context.QuadraticBezierTo(At(cx, cy), At(x, y));

        public void Cubic(float c1x, float c1y, float c2x, float c2y, float x, float y) => context.CubicBezierTo(At(c1x, c1y), At(c2x, c2y), At(x, y));

        public void Finish()
        {
            if (!_open) return;
            context.EndFigure(isClosed: true);
            _open = false;
        }

        private Point At(float x, float y) => new(Origin.X + x * scale, Origin.Y + (ascender - y) * scale);
    }

    private static Drawing DrawingOf(IntPtr data) => (Drawing)GCHandle.FromIntPtr(data).Target!;

    private static void OnMoveTo(IntPtr funcs, IntPtr data, IntPtr state, float x, float y, IntPtr user) => DrawingOf(data).Move(x, y);

    private static void OnLineTo(IntPtr funcs, IntPtr data, IntPtr state, float x, float y, IntPtr user) => DrawingOf(data).Line(x, y);

    private static void OnQuadraticTo(IntPtr funcs, IntPtr data, IntPtr state, float cx, float cy, float x, float y, IntPtr user)
        => DrawingOf(data).Quadratic(cx, cy, x, y);

    private static void OnCubicTo(IntPtr funcs, IntPtr data, IntPtr state, float c1x, float c1y, float c2x, float c2y, float x, float y, IntPtr user)
        => DrawingOf(data).Cubic(c1x, c1y, c2x, c2y, x, y);

    private static void OnClosePath(IntPtr funcs, IntPtr data, IntPtr state, IntPtr user) => DrawingOf(data).Finish();

    private static IntPtr MakeCalls()
    {
        var calls = hb_draw_funcs_create();
        hb_draw_funcs_set_move_to_func(calls, Marshal.GetFunctionPointerForDelegate(MoveToCall), IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_set_line_to_func(calls, Marshal.GetFunctionPointerForDelegate(LineToCall), IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_set_quadratic_to_func(calls, Marshal.GetFunctionPointerForDelegate(QuadraticToCall), IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_set_cubic_to_func(calls, Marshal.GetFunctionPointerForDelegate(CubicToCall), IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_set_close_path_func(calls, Marshal.GetFunctionPointerForDelegate(ClosePathCall), IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_make_immutable(calls);
        return calls;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MoveTo(IntPtr funcs, IntPtr data, IntPtr state, float x, float y, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void QuadraticTo(IntPtr funcs, IntPtr data, IntPtr state, float cx, float cy, float x, float y, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CubicTo(IntPtr funcs, IntPtr data, IntPtr state, float c1x, float c1y, float c2x, float c2y, float x, float y, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClosePath(IntPtr funcs, IntPtr data, IntPtr state, IntPtr user);

    private const string HarfBuzz = "libHarfBuzzSharp";

    [DllImport(HarfBuzz, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_draw_funcs_create();

    [DllImport(HarfBuzz, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_move_to_func(IntPtr funcs, IntPtr call, IntPtr user, IntPtr destroy);

    [DllImport(HarfBuzz, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_line_to_func(IntPtr funcs, IntPtr call, IntPtr user, IntPtr destroy);

    [DllImport(HarfBuzz, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_quadratic_to_func(IntPtr funcs, IntPtr call, IntPtr user, IntPtr destroy);

    [DllImport(HarfBuzz, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_cubic_to_func(IntPtr funcs, IntPtr call, IntPtr user, IntPtr destroy);

    [DllImport(HarfBuzz, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_close_path_func(IntPtr funcs, IntPtr call, IntPtr user, IntPtr destroy);

    [DllImport(HarfBuzz, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_make_immutable(IntPtr funcs);

    [DllImport(HarfBuzz, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_font_draw_glyph(IntPtr font, uint glyph, IntPtr funcs, IntPtr data);
}
