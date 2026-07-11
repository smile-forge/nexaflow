using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders an <see cref="ArchitectureDiagram"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  Services are placed on a deterministic grid seeded from the edges'
/// side hints (an edge <c>A:R -- L:B</c> puts B to the right of A); groups draw as boxes around their
/// members; each service shows a built-in vector glyph for the default icons
/// (cloud/database/disk/internet/server) or the icon name as a caption; edges anchor to the declared
/// <c>T/B/L/R</c> side of each endpoint.  Output tree: <c>Border → ScrollViewer → Canvas</c>.
/// </summary>
public static class WpfArchitectureRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");

    private const double BoxW    = 104;
    private const double BoxH    = 72;
    private const double IconSz  = 28;
    private const double Margin  = 20;
    private const double TitleH  = 26;
    private const double GroupPad = 16;
    private const double GroupHeader = 20;
    private const double JunctionR = 7;

    public static FrameworkElement Render(ArchitectureDiagram diagram, MarkdownPalette palette)
    {
        double sep = Math.Clamp(diagram.Config.NodeSeparation, 12, 120);
        double cellW = BoxW + sep, cellH = BoxH + sep + GroupHeader;

        var cells = PlaceServices(diagram);

        bool hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);
        double originY = Margin + (hasTitle ? TitleH : 0);

        // Service pixel rectangles (junctions collapse to a small square centred in their cell).
        var rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        foreach (var s in diagram.Services)
        {
            if (!cells.TryGetValue(s.Id, out var c)) continue;
            double x = Margin + GroupPad + c.col * cellW;
            double y = originY + GroupPad + c.row * cellH;
            rects[s.Id] = s.IsJunction
                ? new Rect(x + BoxW / 2 - JunctionR, y + BoxH / 2 - JunctionR, JunctionR * 2, JunctionR * 2)
                : new Rect(x, y, BoxW, BoxH);
        }

        var groupBoxes = ComputeGroupBoxes(diagram, rects);

        double maxX = Margin + GroupPad, maxY = originY + GroupPad;
        foreach (var r in rects.Values)     { maxX = Math.Max(maxX, r.Right); maxY = Math.Max(maxY, r.Bottom); }
        foreach (var r in groupBoxes.Values){ maxX = Math.Max(maxX, r.Right); maxY = Math.Max(maxY, r.Bottom); }
        double canvasW = maxX + Margin + GroupPad;
        double canvasH = maxY + Margin + GroupPad;

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = palette.CodeBg };

        if (hasTitle)
        {
            var tb = new TextBlock
            {
                Text = diagram.Title, Foreground = palette.Heading, FontFamily = BodyFont,
                FontSize = 15, FontWeight = FontWeights.SemiBold,
            };
            Canvas.SetLeft(tb, Margin);
            Canvas.SetTop(tb, Margin - 2);
            canvas.Children.Add(tb);
        }

        // Group boxes first (behind), then edges, then service/junction nodes on top.
        int gi = 0;
        foreach (var g in diagram.Groups)
        {
            if (!groupBoxes.TryGetValue(g.Id, out var box)) continue;
            AddGroupBox(canvas, g, box, palette, gi++);
        }

        foreach (var e in diagram.Edges)
            AddEdge(canvas, diagram, e, rects, groupBoxes, palette);

        foreach (var s in diagram.Services)
        {
            if (!rects.TryGetValue(s.Id, out var r)) continue;
            if (s.IsJunction) AddJunction(canvas, r, palette);
            else              AddService(canvas, s, r, palette);
        }

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Content = canvas,
        };
        return new Border
        {
            Background = palette.CodeBg, BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12), Child = scroller,
        };
    }

    // ── Grid placement ─────────────────────────────────────────────────────

    private static Dictionary<string, (int col, int row)> PlaceServices(ArchitectureDiagram diagram)
    {
        var pos      = new Dictionary<string, (int col, int row)>(StringComparer.Ordinal);
        var occupied = new HashSet<(int, int)>();

        void Set(string id, (int col, int row) p) { pos[id] = p; occupied.Add(p); }

        var nodes = diagram.Services.Select(s => s.Id).ToList();
        if (nodes.Count == 0) return pos;

        Set(nodes[0], (0, 0));

        // Relax over service-to-service edges until nothing new can be placed.
        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 64)
        {
            changed = false;
            foreach (var e in diagram.Edges)
            {
                if (e.FromIsGroup || e.ToIsGroup) continue;
                bool fp = pos.ContainsKey(e.FromId), tp = pos.ContainsKey(e.ToId);
                if (fp == tp) continue;

                if (fp)
                {
                    var target = FindFree(Offset(pos[e.FromId], e.FromSide), e.FromSide, occupied);
                    Set(e.ToId, target); changed = true;
                }
                else
                {
                    var target = FindFree(Offset(pos[e.ToId], Opposite(e.FromSide)), Opposite(e.FromSide), occupied);
                    Set(e.FromId, target); changed = true;
                }
            }
        }

        // Any disconnected services flow into free cells on the first row(s).
        int scan = 0;
        foreach (var id in nodes)
        {
            if (pos.ContainsKey(id)) continue;
            while (occupied.Contains((scan, 0))) scan++;
            Set(id, (scan, 0)); scan++;
        }

        ApplyAlignments(diagram, pos, occupied);
        Normalize(pos);
        return pos;
    }

    private static void ApplyAlignments(ArchitectureDiagram diagram,
        Dictionary<string, (int col, int row)> pos, HashSet<(int, int)> occupied)
    {
        foreach (var a in diagram.Alignments)
        {
            var placed = a.Ids.Where(pos.ContainsKey).ToList();
            if (placed.Count < 2) continue;
            if (a.IsRow)
            {
                int row = placed.Min(id => pos[id].row);
                foreach (var id in placed)
                {
                    var p = (pos[id].col, row);
                    if (p == pos[id] || occupied.Contains(p)) continue;
                    occupied.Remove(pos[id]); pos[id] = p; occupied.Add(p);
                }
            }
            else
            {
                int col = placed.Min(id => pos[id].col);
                foreach (var id in placed)
                {
                    var p = (col, pos[id].row);
                    if (p == pos[id] || occupied.Contains(p)) continue;
                    occupied.Remove(pos[id]); pos[id] = p; occupied.Add(p);
                }
            }
        }
    }

    private static (int col, int row) Offset((int col, int row) p, ArchSide side) => side switch
    {
        ArchSide.Left   => (p.col - 1, p.row),
        ArchSide.Right  => (p.col + 1, p.row),
        ArchSide.Top    => (p.col, p.row - 1),
        _               => (p.col, p.row + 1),   // Bottom
    };

    private static ArchSide Opposite(ArchSide s) => s switch
    {
        ArchSide.Left  => ArchSide.Right,
        ArchSide.Right => ArchSide.Left,
        ArchSide.Top   => ArchSide.Bottom,
        _              => ArchSide.Top,
    };

    /// <summary>Returns <paramref name="start"/> if free, else steps along <paramref name="dir"/> until a
    /// free cell is found (bounded); a final fallback scans outward so placement always terminates.</summary>
    private static (int col, int row) FindFree((int col, int row) start, ArchSide dir, HashSet<(int, int)> occupied)
    {
        var p = start;
        for (int i = 0; i < 64 && occupied.Contains(p); i++) p = Offset(p, dir);
        if (!occupied.Contains(p)) return p;
        for (int r = 0; r < 256; r++)
            for (int c = 0; c < 256; c++)
                if (!occupied.Contains((c, r))) return (c, r);
        return p;
    }

    private static void Normalize(Dictionary<string, (int col, int row)> pos)
    {
        if (pos.Count == 0) return;
        int minC = pos.Values.Min(p => p.col), minR = pos.Values.Min(p => p.row);
        foreach (var k in pos.Keys.ToList()) pos[k] = (pos[k].col - minC, pos[k].row - minR);
    }

    // ── Group boxes ──────────────────────────────────────────────────────────

    private static Dictionary<string, Rect> ComputeGroupBoxes(ArchitectureDiagram diagram, Dictionary<string, Rect> rects)
    {
        var boxes = new Dictionary<string, Rect>(StringComparer.Ordinal);

        // Direct members first.
        foreach (var g in diagram.Groups)
        {
            var members = diagram.Services.Where(s => s.GroupId == g.Id && rects.ContainsKey(s.Id))
                                          .Select(s => rects[s.Id]).ToList();
            if (members.Count == 0) continue;
            boxes[g.Id] = Expand(Union(members), GroupPad, GroupHeader);
        }

        // Grow parents to enclose nested child-group boxes (one settle pass per nesting level).
        for (int pass = 0; pass < 6; pass++)
        {
            bool changed = false;
            foreach (var g in diagram.Groups)
            {
                if (g.ParentId is not string pid) continue;
                if (!boxes.TryGetValue(g.Id, out var childBox)) continue;
                Rect grown = boxes.TryGetValue(pid, out var parentBox)
                    ? Union([parentBox, Expand(childBox, GroupPad, 0)])
                    : Expand(childBox, GroupPad, GroupHeader);
                if (!boxes.TryGetValue(pid, out var existing) || existing != grown) { boxes[pid] = grown; changed = true; }
            }
            if (!changed) break;
        }
        return boxes;
    }

    private static Rect Union(IReadOnlyList<Rect> rects)
    {
        double l = rects.Min(r => r.Left), t = rects.Min(r => r.Top);
        double rr = rects.Max(r => r.Right), b = rects.Max(r => r.Bottom);
        return new Rect(l, t, rr - l, b - t);
    }

    private static Rect Expand(Rect r, double pad, double header) =>
        new(r.X - pad, r.Y - pad - header, r.Width + pad * 2, r.Height + pad * 2 + header);

    private static void AddGroupBox(Canvas canvas, ArchGroup g, Rect box, MarkdownPalette palette, int index)
    {
        Color c = (palette.Series[index % palette.Series.Count] as SolidColorBrush)?.Color ?? Colors.Gray;
        var fill = new SolidColorBrush(Color.FromArgb(0x14, c.R, c.G, c.B)); fill.Freeze();

        var border = new Border
        {
            Width = box.Width, Height = box.Height, Background = fill,
            BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        Canvas.SetLeft(border, box.X);
        Canvas.SetTop(border, box.Y);
        canvas.Children.Add(border);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (g.Icon is string icon)
            header.Children.Add(GlyphElement(icon, 14, palette.Accent));
        header.Children.Add(new TextBlock
        {
            Text = g.Display, Foreground = palette.TextMuted, FontFamily = BodyFont,
            FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Canvas.SetLeft(header, box.X + 8);
        Canvas.SetTop(header, box.Y + 4);
        canvas.Children.Add(header);
    }

    // ── Service & junction nodes ──────────────────────────────────────────────

    private static void AddService(Canvas canvas, ArchService s, Rect r, MarkdownPalette palette)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(GlyphElement(s.Icon, IconSz, palette.Accent));
        stack.Children.Add(new TextBlock
        {
            Text = s.Display, Foreground = palette.Text, FontFamily = BodyFont, FontSize = 11,
            TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = BoxW - 8, Margin = new Thickness(0, 4, 0, 0),
        });

        var border = new Border
        {
            Width = r.Width, Height = r.Height, Background = palette.TableHeaderBg,
            BorderBrush = palette.Accent, BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(6), Child = stack,
        };
        Canvas.SetLeft(border, r.X);
        Canvas.SetTop(border, r.Y);
        canvas.Children.Add(border);
    }

    private static void AddJunction(Canvas canvas, Rect r, MarkdownPalette palette)
    {
        var dot = new Ellipse
        {
            Width = r.Width, Height = r.Height, Fill = palette.CodeBg,
            Stroke = palette.Accent, StrokeThickness = 1.5,
        };
        Canvas.SetLeft(dot, r.X);
        Canvas.SetTop(dot, r.Y);
        canvas.Children.Add(dot);
    }

    // ── Edges ──────────────────────────────────────────────────────────────

    private static void AddEdge(Canvas canvas, ArchitectureDiagram diagram, ArchEdge e, Dictionary<string, Rect> rects,
        Dictionary<string, Rect> groupBoxes, MarkdownPalette palette)
    {
        if (!TryEndpoint(diagram, e.FromId, e.FromIsGroup, rects, groupBoxes, out var fromBox) ||
            !TryEndpoint(diagram, e.ToId,   e.ToIsGroup,   rects, groupBoxes, out var toBox))
            return;

        var from = Anchor(fromBox, e.FromSide);
        var to   = Anchor(toBox,   e.ToSide);

        canvas.Children.Add(new Line
        {
            X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
            Stroke = palette.TextMuted, StrokeThickness = 1.5,
        });
        if (e.EndArrow)   AddArrowHead(canvas, from, to, palette.TextMuted);
        if (e.StartArrow) AddArrowHead(canvas, to, from, palette.TextMuted);
    }

    /// <summary>Resolves an endpoint box.  A <c>{group}</c> flag on a service id resolves to that service's
    /// containing group box; on a bare group id it resolves to that group directly.</summary>
    private static bool TryEndpoint(ArchitectureDiagram diagram, string id, bool isGroup,
        Dictionary<string, Rect> rects, Dictionary<string, Rect> groupBoxes, out Rect box)
    {
        if (isGroup)
        {
            string? gid = diagram.FindService(id)?.GroupId ?? (diagram.FindGroup(id) is not null ? id : null);
            if (gid is not null) return groupBoxes.TryGetValue(gid, out box);
            box = default;
            return false;
        }
        return rects.TryGetValue(id, out box);
    }

    private static Point Anchor(Rect r, ArchSide side) => side switch
    {
        ArchSide.Left   => new Point(r.Left,  r.Top + r.Height / 2),
        ArchSide.Right  => new Point(r.Right, r.Top + r.Height / 2),
        ArchSide.Top    => new Point(r.Left + r.Width / 2, r.Top),
        _               => new Point(r.Left + r.Width / 2, r.Bottom),
    };

    private static void AddArrowHead(Canvas canvas, Point from, Point to, Brush brush)
    {
        double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double len = 9, spread = 0.5;
        var p1 = new Point(to.X - len * Math.Cos(angle - spread), to.Y - len * Math.Sin(angle - spread));
        var p2 = new Point(to.X - len * Math.Cos(angle + spread), to.Y - len * Math.Sin(angle + spread));
        canvas.Children.Add(new Polygon { Fill = brush, Points = [to, p1, p2] });
    }

    // ── Icon glyphs ──────────────────────────────────────────────────────────

    /// <summary>Builds a small element for a service icon: a built-in vector glyph for the default icon
    /// names, else the icon name (or a generic box) as a caption fallback.</summary>
    private static FrameworkElement GlyphElement(string? icon, double size, Brush brush)
    {
        string name = (icon ?? string.Empty).Trim().ToLowerInvariant();
        // Custom "pack:name" icons fall back to the trailing name.
        int colon = name.IndexOf(':');
        if (colon >= 0) name = name[(colon + 1)..];

        var canvas = new Canvas { Width = size, Height = size, Background = Brushes.Transparent };
        double s = size;
        switch (name)
        {
            case "cloud":               DrawCloud(canvas, s, brush);    break;
            case "database" or "db":    DrawDatabase(canvas, s, brush); break;
            case "disk":                DrawDisk(canvas, s, brush);     break;
            case "internet":            DrawInternet(canvas, s, brush); break;
            case "server":              DrawServer(canvas, s, brush);   break;
            default:                    return Caption(icon, brush, size);
        }
        return canvas;
    }

    private static FrameworkElement Caption(string? icon, Brush brush, double size)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return new Border
            {
                Width = size, Height = size * 0.7, BorderBrush = brush, BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(3), Background = Brushes.Transparent,
            };
        return new TextBlock
        {
            Text = icon, Foreground = brush, FontFamily = BodyFont, FontSize = 10,
            FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
        };
    }

    private static void DrawServer(Canvas c, double s, Brush b)
    {
        var rect = new Rectangle { Width = s * 0.8, Height = s * 0.8, Stroke = b, StrokeThickness = 1.4, RadiusX = 2, RadiusY = 2 };
        Canvas.SetLeft(rect, s * 0.1); Canvas.SetTop(rect, s * 0.1); c.Children.Add(rect);
        for (int i = 0; i < 3; i++)
        {
            double y = s * (0.28 + i * 0.18);
            c.Children.Add(Ln(s * 0.22, y, s * 0.62, y, b));
            var dot = new Ellipse { Width = 2.4, Height = 2.4, Fill = b };
            Canvas.SetLeft(dot, s * 0.68); Canvas.SetTop(dot, y - 1.2); c.Children.Add(dot);
        }
    }

    private static void DrawDatabase(Canvas c, double s, Brush b)
    {
        double w = s * 0.7, x = s * 0.15, top = s * 0.12, h = s * 0.62, eh = s * 0.16;
        c.Children.Add(Ellipse(x, top, w, eh, b));
        c.Children.Add(Ln(x, top + eh / 2, x, top + h - eh / 2, b));
        c.Children.Add(Ln(x + w, top + eh / 2, x + w, top + h - eh / 2, b));
        c.Children.Add(Ellipse(x, top + h - eh, w, eh, b));
    }

    private static void DrawDisk(Canvas c, double s, Brush b)
    {
        var rect = new Rectangle { Width = s * 0.76, Height = s * 0.76, Stroke = b, StrokeThickness = 1.4, RadiusX = 2, RadiusY = 2 };
        Canvas.SetLeft(rect, s * 0.12); Canvas.SetTop(rect, s * 0.12); c.Children.Add(rect);
        var inner = new Rectangle { Width = s * 0.34, Height = s * 0.22, Stroke = b, StrokeThickness = 1 };
        Canvas.SetLeft(inner, s * 0.33); Canvas.SetTop(inner, s * 0.12); c.Children.Add(inner);
    }

    private static void DrawInternet(Canvas c, double s, Brush b)
    {
        double d = s * 0.76, x = s * 0.12, y = s * 0.12, r = d / 2;
        c.Children.Add(Ellipse(x, y, d, d, b));
        c.Children.Add(Ln(x, y + r, x + d, y + r, b));
        c.Children.Add(Ln(x + r, y, x + r, y + d, b));
        var v = new Ellipse { Width = d * 0.5, Height = d, Stroke = b, StrokeThickness = 1 };
        Canvas.SetLeft(v, x + r - d * 0.25); Canvas.SetTop(v, y); c.Children.Add(v);
    }

    private static void DrawCloud(Canvas c, double s, Brush b)
    {
        c.Children.Add(Ellipse(s * 0.10, s * 0.42, s * 0.34, s * 0.30, b));
        c.Children.Add(Ellipse(s * 0.30, s * 0.28, s * 0.40, s * 0.40, b));
        c.Children.Add(Ellipse(s * 0.54, s * 0.42, s * 0.34, s * 0.30, b));
        c.Children.Add(Ln(s * 0.16, s * 0.66, s * 0.84, s * 0.66, b));
    }

    private static Ellipse Ellipse(double x, double y, double w, double h, Brush b)
    {
        var e = new Ellipse { Width = w, Height = h, Stroke = b, StrokeThickness = 1.3, Fill = Brushes.Transparent };
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
        return e;
    }

    private static Line Ln(double x1, double y1, double x2, double y2, Brush b) =>
        new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = 1.3 };
}
