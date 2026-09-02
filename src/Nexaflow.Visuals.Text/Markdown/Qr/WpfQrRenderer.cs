using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// Draws a <see cref="QrMatrix"/> as a WPF element: the modules as one filled path, on a background
/// that extends past them by the quiet zone.
///
/// <para>
/// One path rather than a rectangle per module ΓÇö a version-40 symbol is 31,329 modules, and a visual
/// tree of that many shapes costs far more than the geometry does. Horizontal runs are merged as they
/// are emitted, which typically leaves a few hundred rectangles for a code of any ordinary size.
/// </para>
///
/// <para>
/// The colours are the one thing here that is not geometry. A QR code has to stay dark-on-light to be
/// readable, so the defaults come from the palette's own QR tokens rather than its text and surface
/// brushes: following those would invert the code on a dark theme and hand the reader something no
/// scanner will take. A block's <c>dark:</c> / <c>light:</c> settings override them outright.
/// </para>
/// </summary>
public static class WpfQrRenderer
{
    /// <summary>Renders <paramref name="block"/>'s payload, or a message saying why it could not be encoded.</summary>
    public static FrameworkElement Render(QrBlock block, MarkdownPalette palette)
    {
        if (QrEncoder.TryEncode(block.Payload, block.ErrorCorrection, out var matrix, out string? error))
            return Render(matrix!, block, palette);

        // The error box prints its source on one unwrapped line, so the payload is trimmed first: the one
        // failure that reaches here is a payload too long to encode, which would stretch across the page.
        const int maxEcho = 120;
        string echo = block.Payload.Length > maxEcho ? block.Payload[..maxEcho] + "ΓÇª" : block.Payload;

        return DiagramRenderer.ErrorElement(error!, echo);
    }

    /// <summary>Renders an already-encoded symbol with the drawing settings from <paramref name="block"/>.</summary>
    public static FrameworkElement Render(QrMatrix matrix, QrBlock block, MarkdownPalette palette)
    {
        Brush dark  = Brush(block.Dark,  palette.QrDark);
        Brush light = Brush(block.Light, palette.QrLight);

        double cell = block.CellSize;
        double side = matrix.Size * cell;

        var path = new Path
        {
            Data   = ModuleGeometry(matrix, cell),
            Fill   = dark,
            Width  = side,
            Height = side,
        };

        // Module edges must land on device pixels: a half-pixel seam between two dark modules reads as
        // a light line to a camera, which is the whole difference between a code that scans and one
        // that does not.
        RenderOptions.SetEdgeMode(path, EdgeMode.Aliased);

        var border = new Border
        {
            Background          = light,
            Padding             = new Thickness(block.Margin * cell),
            Child               = path,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin              = new Thickness(0, 4, 0, 10),
            UseLayoutRounding   = true,
            SnapsToDevicePixels = true,
            ToolTip             = Tooltip(matrix, block, palette),
        };

        return border;
    }

    // ΓöÇΓöÇ Geometry ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>The dark modules as one frozen geometry, with horizontal runs merged into single rectangles.</summary>
    private static Geometry ModuleGeometry(QrMatrix matrix, double cell)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int y = 0; y < matrix.Size; y++)
            {
                int x = 0;
                while (x < matrix.Size)
                {
                    if (!matrix[x, y]) { x++; continue; }

                    int run = 1;
                    while (x + run < matrix.Size && matrix[x + run, y]) run++;

                    AddRect(ctx, x * cell, y * cell, run * cell, cell);
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

    // ΓöÇΓöÇ Colours and tooltip ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    private static Brush Brush(QrColor? explicitColor, Brush fallback)
    {
        if (explicitColor is not { } c) return fallback;

        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// What the code actually says, plus the symbol it needed. A rendered QR is opaque by nature ΓÇö the
    /// author cannot proof-read a grid of squares ΓÇö so the payload has to be readable somewhere.
    /// </summary>
    private static object Tooltip(QrMatrix matrix, QrBlock block, MarkdownPalette palette)
    {
        const int maxPayload = 400;
        string payload = block.Payload.Length > maxPayload
            ? block.Payload[..maxPayload] + "ΓÇª"
            : block.Payload;

        return new TextBlock
        {
            Text = $"{block.Type} ┬╖ version {matrix.Version} ({matrix.Size}├ù{matrix.Size}), "
                 + $"error correction {block.ErrorCorrection}\n\n{payload}",
            TextAlignment = TextAlignment.Left,
            TextWrapping  = TextWrapping.Wrap,
            MaxWidth      = 420,
            Foreground    = palette.Text,
        };
    }
}
