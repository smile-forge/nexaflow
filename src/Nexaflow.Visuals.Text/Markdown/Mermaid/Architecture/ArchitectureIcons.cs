using System;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Architecture;

/// <summary>
/// The icons an architecture diagram draws over its services. Mermaid ships five — <c>cloud</c>, <c>database</c>,
/// <c>disk</c>, <c>internet</c> and <c>server</c> — and takes any of an icon pack's besides, written <c>pack:name</c>.
/// Those five are drawn here; anything else is written out as what it is called, which says as much as a picture nobody
/// has would.
/// </summary>
internal static class ArchitectureIcons
{
    /// <summary>The icons drawn as pictures rather than written out.</summary>
    public static readonly string[] Drawn = ["cloud", "database", "disk", "internet", "server"];

    /// <summary>Whether an icon is one of the five, ignoring the pack it may be written with.</summary>
    public static bool Known(string? icon) => Array.IndexOf(Drawn, Named(icon)) >= 0;

    /// <summary>What an icon is called, without the pack written before it.</summary>
    public static string Named(string? icon)
    {
        var said = (icon ?? string.Empty).Trim();
        var pack = said.LastIndexOf(':');

        return (pack >= 0 ? said[(pack + 1)..] : said).ToLowerInvariant();
    }

    /// <summary>The picture an icon is drawn as, filling <paramref name="bounds"/> — or null where it is not one of the five.</summary>
    public static Geometry? Picture(string? icon, Rect bounds) => Named(icon) switch
    {
        "cloud" => DiagramShapes.Outline(DiagramShape.Cloud, bounds),
        "database" => DiagramShapes.Outline(DiagramShape.Cylinder, bounds),
        "disk" => Disk(bounds),
        "internet" => Globe(bounds),
        "server" => Rack(bounds),
        _ => null,
    };

    /// <summary>A disk: its case, with the platter inside it.</summary>
    private static Geometry Disk(Rect bounds)
    {
        var platter = Math.Min(bounds.Width, bounds.Height) * 0.3;
        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

        var shape = new GeometryGroup
        {
            Children =
            {
                new RectangleGeometry(bounds, 3, 3),
                new EllipseGeometry(middle, platter, platter),
                new EllipseGeometry(middle, platter / 3, platter / 3),
            },
        };

        shape.Freeze();
        return shape;
    }

    /// <summary>A globe: its outline, the equator, and a meridian round the back of it.</summary>
    private static Geometry Globe(Rect bounds)
    {
        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

        var shape = new GeometryGroup
        {
            Children =
            {
                new EllipseGeometry(bounds),
                new EllipseGeometry(middle, bounds.Width / 4, bounds.Height / 2),
                new LineGeometry(new Point(bounds.Left, middle.Y), new Point(bounds.Right, middle.Y)),
            },
        };

        shape.Freeze();
        return shape;
    }

    /// <summary>A rack: three units stacked, each with a light on it.</summary>
    private static Geometry Rack(Rect bounds)
    {
        var tall = bounds.Height / 3;
        var shape = new GeometryGroup();

        for (var unit = 0; unit < 3; unit++)
        {
            var box = new Rect(bounds.X, bounds.Y + (unit * tall), bounds.Width, tall);

            shape.Children.Add(new RectangleGeometry(Rect.Inflate(box, -0.5, -0.5), 2, 2));
            shape.Children.Add(new EllipseGeometry(new Point(box.Right - (tall / 2), box.Y + (tall / 2)), tall / 6, tall / 6));
        }

        shape.Freeze();
        return shape;
    }
}
