using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>
/// Draws any <see cref="IModuleMatrix"/> to WPF: the dark modules as one merged-run geometry on a
/// quiet-zone border, in the palette's scannable pair unless the block chose its own.
///
/// <para>
/// One renderer for every matrix symbology, because the drawing is the same for all of them and the
/// details that matter to a scanner — module edges on device pixels, no seams between neighbours, a
/// quiet zone that is really quiet — are easy to get subtly wrong and should be got right once.
/// </para>
/// </summary>
public static class WpfMatrixRenderer
{
    /// <summary>
    /// Renders <paramref name="matrix"/>.
    /// </summary>
    /// <param name="rowHeight">
    /// Module height as a multiple of module width. One for a true matrix; a stacked symbology such as
    /// PDF417 draws each row taller than its modules are wide, and says so here.
    /// </param>
    public static Border Render(IModuleMatrix matrix, MatrixSettings settings, MarkdownPalette palette,
                                string tooltip, double rowHeight = 1)
    {
        Brush dark  = Brush(settings.Dark,  palette.QrDark);
        Brush light = Brush(settings.Light, palette.QrLight);

        double cell   = settings.CellSize;
        double width  = matrix.Width * cell;
        double height = matrix.Height * cell * rowHeight;

        var path = new Path
        {
            Data   = ModuleGeometry(matrix, cell, cell * rowHeight),
            Fill   = dark,
            Width  = width,
            Height = height,
        };

        // Module edges must land on device pixels: a half-pixel seam between two dark modules reads as
        // a light line to a camera, which is the whole difference between a code that scans and one
        // that does not.
        RenderOptions.SetEdgeMode(path, EdgeMode.Aliased);

        return new Border
        {
            Background          = light,
            Padding             = new Thickness(settings.Margin * cell),
            Child               = path,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin              = new Thickness(0, 4, 0, 10),
            UseLayoutRounding   = true,
            SnapsToDevicePixels = true,
            ToolTip             = Tooltip(tooltip, palette),
        };
    }

    // ── Geometry ───────────────────────────────────────────────────────────

    /// <summary>The dark modules as one frozen geometry, with horizontal runs merged into single rectangles.</summary>
    private static Geometry ModuleGeometry(IModuleMatrix matrix, double cellWidth, double cellHeight)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int y = 0; y < matrix.Height; y++)
            {
                int x = 0;
                while (x < matrix.Width)
                {
                    if (!matrix[x, y]) { x++; continue; }

                    int run = 1;
                    while (x + run < matrix.Width && matrix[x + run, y]) run++;

                    AddRect(ctx, x * cellWidth, y * cellHeight, run * cellWidth, cellHeight);
                    x += run;
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static void AddRect(StreamGeometryContext ctx, double x, double y, double w, double h)
    {
        ctx.BeginFigure(new Point(x, y), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(x + w, y),     isStroked: false, isSmoothJoin: false);
        ctx.LineTo(new Point(x + w, y + h), isStroked: false, isSmoothJoin: false);
        ctx.LineTo(new Point(x,     y + h), isStroked: false, isSmoothJoin: false);
    }

    // ── Colours and tooltip ────────────────────────────────────────────────

    private static Brush Brush(HexColor? explicitColor, Brush fallback)
    {
        if (explicitColor is not { } c) return fallback;

        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// What the code actually says. A rendered symbol is opaque by nature — the author cannot proof-read
    /// a grid of squares — so the payload has to be readable somewhere.
    /// </summary>
    private static object Tooltip(string text, MarkdownPalette palette) => new TextBlock
    {
        Text          = text,
        TextAlignment = TextAlignment.Left,
        TextWrapping  = TextWrapping.Wrap,
        MaxWidth      = 420,
        Foreground    = palette.Text,
    };

    /// <summary>The payload cut to a length a tooltip can hold, with an ellipsis where it was cut.</summary>
    public static string Abridged(string payload, int max = 400) =>
        payload.Length > max ? payload[..max] + "…" : payload;
}
