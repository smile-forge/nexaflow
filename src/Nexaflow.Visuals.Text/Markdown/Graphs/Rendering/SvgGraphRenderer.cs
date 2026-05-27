using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using System.Globalization;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Converts a <see cref="LayoutedGraph"/> to an SVG string.
///
/// This is the portable, export-oriented renderer.  The WPF UI uses
/// <see cref="WpfGraphRenderer"/> for live display; this class is for
/// file export, clipboard, and round-tripping.
/// </summary>
public static class SvgGraphRenderer
{
    // ── Palette (matches WpfGraphRenderer) ────────────────────────────────

    private const string BgColor         = "#0D101A";
    private const string NodeFill        = "#1E2438";
    private const string NodeStroke      = "#4F8EF7";
    private const string DiamondFill     = "#2A1A3A";
    private const string DiamondStroke   = "#A060FF";
    private const string EdgeColor       = "#4F8EF7";
    private const string EdgeDashedColor = "#7880A0";
    private const string EdgeThickColor  = "#FFD060";
    private const string TextColor       = "#E8EAF2";
    private const string LabelColor      = "#7880A0";
    private const string TitleColor      = "#A8D4FF";

    // ── Public API ─────────────────────────────────────────────────────────

    public static string Render(LayoutedGraph lg)
    {
        var sb  = new StringBuilder();
        double w = Math.Max(lg.Width,  80);
        double h = Math.Max(lg.Height, 60);

        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{F(w)}" height="{F(h)}" viewBox="0 0 {F(w)} {F(h)}">""");
        sb.AppendLine($"""  <rect width="100%" height="100%" fill="{BgColor}"/>""");

        // Title
        if (!string.IsNullOrWhiteSpace(lg.Source.Title))
            sb.AppendLine($"""  <text x="{F(SugiyamaLayout.MarginX)}" y="20" font-family="Segoe UI,Arial" font-size="14" font-weight="bold" fill="{TitleColor}">{Esc(lg.Source.Title)}</text>""");

        // Edges (below nodes)
        foreach (var le in lg.Edges)
            RenderEdge(sb, le);

        // Nodes
        foreach (var ln in lg.AllNodes.Where(n => !n.IsDummy))
            RenderNode(sb, ln);

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    // ── Node rendering ─────────────────────────────────────────────────────

    private static void RenderNode(StringBuilder sb, LayoutNode ln)
    {
        string fill   = ln.Source?.FillColor   ?? NodeFill;
        string stroke = ln.Source?.StrokeColor ?? NodeStroke;
        string text   = Esc(ln.Source?.Label ?? string.Empty);
        double tx     = ln.X;
        double ty     = ln.Y + 4; // vertical centering offset

        switch (ln.Source?.Shape)
        {
            case NodeShape.Diamond:
            {
                double hw = ln.Width / 2, hh = ln.Height / 2;
                string pts = $"{F(ln.X)},{F(ln.Y - hh)} {F(ln.X + hw)},{F(ln.Y)} {F(ln.X)},{F(ln.Y + hh)} {F(ln.X - hw)},{F(ln.Y)}";
                sb.AppendLine($"""  <polygon points="{pts}" fill="{DiamondFill}" stroke="{DiamondStroke}" stroke-width="1.5"/>""");
                break;
            }
            case NodeShape.Circle:
            {
                double r = ln.Width / 2;
                sb.AppendLine($"""  <ellipse cx="{F(ln.X)}" cy="{F(ln.Y)}" rx="{F(r)}" ry="{F(ln.Height / 2)}" fill="{fill}" stroke="{stroke}" stroke-width="1.5"/>""");
                break;
            }
            case NodeShape.Stadium:
            case NodeShape.RoundedRect:
            {
                double rx = ln.Source.Shape == NodeShape.Stadium ? ln.Height / 2 : 8;
                sb.AppendLine($"""  <rect x="{F(ln.X - ln.Width / 2)}" y="{F(ln.Y - ln.Height / 2)}" width="{F(ln.Width)}" height="{F(ln.Height)}" rx="{F(rx)}" fill="{fill}" stroke="{stroke}" stroke-width="1.5"/>""");
                break;
            }
            case NodeShape.Hexagon:
            {
                double hw = ln.Width / 2, hh = ln.Height / 2, qw = hw * 0.4;
                string pts = $"{F(ln.X - hw)},{F(ln.Y)} {F(ln.X - hw + qw)},{F(ln.Y - hh)} {F(ln.X + hw - qw)},{F(ln.Y - hh)} {F(ln.X + hw)},{F(ln.Y)} {F(ln.X + hw - qw)},{F(ln.Y + hh)} {F(ln.X - hw + qw)},{F(ln.Y + hh)}";
                sb.AppendLine($"""  <polygon points="{pts}" fill="{fill}" stroke="{stroke}" stroke-width="1.5"/>""");
                break;
            }
            default:
            {
                sb.AppendLine($"""  <rect x="{F(ln.X - ln.Width / 2)}" y="{F(ln.Y - ln.Height / 2)}" width="{F(ln.Width)}" height="{F(ln.Height)}" fill="{fill}" stroke="{stroke}" stroke-width="1.5"/>""");
                break;
            }
        }

        // Label
        sb.AppendLine($"""  <text x="{F(tx)}" y="{F(ty)}" text-anchor="middle" font-family="Segoe UI,Arial" font-size="12" fill="{TextColor}">{text}</text>""");
    }

    // ── Edge rendering ─────────────────────────────────────────────────────

    private static void RenderEdge(StringBuilder sb, LayoutEdge le)
    {
        if (le.Waypoints.Count < 2) return;

        var edge  = le.Source;
        string color = edge?.Style switch
        {
            EdgeStyle.Thick  => EdgeThickColor,
            EdgeStyle.Dashed => EdgeDashedColor,
            EdgeStyle.Dotted => EdgeDashedColor,
            _                => EdgeColor,
        };
        double thickness = edge?.Style == EdgeStyle.Thick ? 2.5 : 1.5;
        string dash = edge?.Style switch
        {
            EdgeStyle.Dashed => " stroke-dasharray=\"5,3\"",
            EdgeStyle.Dotted => " stroke-dasharray=\"2,3\"",
            _                => string.Empty,
        };

        var pts = string.Join(" ", le.Waypoints.Select(p => $"{F(p.X)},{F(p.Y)}"));
        sb.AppendLine($"""  <polyline points="{pts}" fill="none" stroke="{color}" stroke-width="{F(thickness)}"{dash} stroke-linejoin="round" stroke-linecap="round"/>""");

        // Arrowhead
        if (edge?.Arrow != EdgeArrow.None)
        {
            var tip  = le.Waypoints[^1];
            var prev = le.Waypoints[^2];
            double ang  = Math.Atan2(tip.Y - prev.Y, tip.X - prev.X);
            double len  = 10;
            double aOff = 25 * Math.PI / 180;
            double a1   = ang + Math.PI - aOff, a2 = ang + Math.PI + aOff;
            double p1x  = tip.X + len * Math.Cos(a1), p1y = tip.Y + len * Math.Sin(a1);
            double p2x  = tip.X + len * Math.Cos(a2), p2y = tip.Y + len * Math.Sin(a2);

            if (edge.Arrow == EdgeArrow.Open)
                sb.AppendLine($"""  <polyline points="{F(p1x)},{F(p1y)} {F(tip.X)},{F(tip.Y)} {F(p2x)},{F(p2y)}" fill="none" stroke="{color}" stroke-width="1.5" stroke-linejoin="round"/>""");
            else
                sb.AppendLine($"""  <polygon points="{F(tip.X)},{F(tip.Y)} {F(p1x)},{F(p1y)} {F(p2x)},{F(p2y)}" fill="{color}" stroke="{color}" stroke-width="0.5"/>""");
        }

        // Edge label
        if (!string.IsNullOrWhiteSpace(edge?.Label))
        {
            var mid = le.Waypoints[le.Waypoints.Count / 2];
            sb.AppendLine($"""  <text x="{F(mid.X + 4)}" y="{F(mid.Y - 4)}" font-family="Segoe UI,Arial" font-size="11" fill="{LabelColor}">{Esc(edge.Label)}</text>""");
        }
    }

    // ── Utilities ──────────────────────────────────────────────────────────

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Esc(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
