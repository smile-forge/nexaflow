using SkiaSharp;

namespace Nexaflow.Features.Images.Services.Printing;

/// <summary>How big a picture is printed.</summary>
public enum PrintSize
{
    /// <summary>As large as fits inside the margins, whole.</summary>
    Fit,

    /// <summary>Filling the page inside the margins, what overflows cropped away.</summary>
    Fill,

    /// <summary>At its own resolution, across as many pages as it takes.</summary>
    Actual,

    /// <summary>At a width set in millimetres, across as many pages as it takes.</summary>
    Width,
}

/// <summary>Which way up the page is: as the picture's shape suggests, or as chosen.</summary>
public enum PrintOrientation
{
    Auto,
    Portrait,
    Landscape,
}

/// <summary>How a picture is laid on paper.</summary>
public sealed record PrintOptions(PrintSize Size, double WidthMillimetres, PrintOrientation Orientation, double MarginMillimetres, bool Centre);

/// <summary>One page of a printed picture: the part of the picture it shows, in its pixels, and where on the page, in device-independent
/// pixels from the printable area's corner.</summary>
public sealed record PrintedTile(SKRectI Source, SKRect Page);

/// <summary>
/// A picture laid out on paper: a page for Fit and Fill, and for Actual size or a set width as many pages as the picture runs
/// across, each showing its tile from the printable area's corner. Pure layout — sizes in, pages out — so it is the same for
/// any printer.
/// </summary>
public static class PicturePrintLayout
{
    private const double DipsPerInch = 96;
    private const double MillimetresPerInch = 25.4;

    /// <summary>Whether the page is turned landscape for <paramref name="picture"/>: as chosen, or for a picture wider than tall.</summary>
    public static bool Landscape(SKSizeI picture, PrintOptions options) => options.Orientation switch
    {
        PrintOrientation.Landscape => true,
        PrintOrientation.Portrait  => false,
        _                          => picture.Width > picture.Height,
    };

    /// <summary>The pages a <paramref name="picture"/> of <paramref name="dpi"/> prints on, inside a printable area of
    /// <paramref name="printable"/> device-independent pixels.</summary>
    public static IReadOnlyList<PrintedTile> Tiles(SKSizeI picture, double dpi, PrintOptions options, SKSize printable)
    {
        if (picture.Width <= 0 || picture.Height <= 0) return [];
        double margin = Math.Max(0, options.MarginMillimetres) / MillimetresPerInch * DipsPerInch;
        double left = Math.Min(margin, printable.Width / 2), top = Math.Min(margin, printable.Height / 2);
        double width = Math.Max(1, printable.Width - 2 * left), height = Math.Max(1, printable.Height - 2 * top);
        double pw = picture.Width, ph = picture.Height;
        var whole = new SKRectI(0, 0, picture.Width, picture.Height);

        PrintedTile Placed(SKRectI source, double w, double h)
        {
            double x = options.Centre ? left + (width - w) / 2 : left, y = options.Centre ? top + (height - h) / 2 : top;
            return new PrintedTile(source, SKRect.Create((float)x, (float)y, (float)w, (float)h));
        }

        IReadOnlyList<PrintedTile> Spread(double scale)
        {
            if (pw * scale <= width + 0.5 && ph * scale <= height + 0.5) return [Placed(whole, pw * scale, ph * scale)];
            int across = Math.Max(1, (int)Math.Floor(width / scale)), down = Math.Max(1, (int)Math.Floor(height / scale));
            var tiles = new List<PrintedTile>();
            for (int y = 0; y < picture.Height; y += down)
                for (int x = 0; x < picture.Width; x += across)
                {
                    var source = new SKRectI(x, y, Math.Min(picture.Width, x + across), Math.Min(picture.Height, y + down));
                    tiles.Add(new PrintedTile(source, SKRect.Create((float)left, (float)top, (float)(source.Width * scale), (float)(source.Height * scale))));
                }
            return tiles;
        }

        switch (options.Size)
        {
            case PrintSize.Fill:
            {
                double scale = Math.Max(width / pw, height / ph);
                double sw = Math.Min(pw, width / scale), sh = Math.Min(ph, height / scale);
                int sx = (int)Math.Round((pw - sw) / 2), sy = (int)Math.Round((ph - sh) / 2);
                var source = new SKRectI(sx, sy, sx + Math.Max(1, (int)Math.Round(sw)), sy + Math.Max(1, (int)Math.Round(sh)));
                return [new PrintedTile(source, SKRect.Create((float)left, (float)top, (float)width, (float)height))];
            }
            case PrintSize.Actual:
                return Spread(DipsPerInch / Math.Max(1, dpi));
            case PrintSize.Width:
                return Spread(Math.Max(1, options.WidthMillimetres) / MillimetresPerInch * DipsPerInch / pw);
            default:
            {
                double scale = Math.Min(width / pw, height / ph);
                return [Placed(whole, pw * scale, ph * scale)];
            }
        }
    }

    /// <summary>Device-independent pixels a pixel of the picture covers on paper, as <paramref name="tiles"/> lay it.</summary>
    public static double Scale(IReadOnlyList<PrintedTile> tiles)
        => tiles.Count == 0 || tiles[0].Source.Width == 0 ? 1 : tiles[0].Page.Width / tiles[0].Source.Width;
}
