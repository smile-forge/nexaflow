namespace Nexaflow.Markdown.Layout.Treemap;

/// <summary>
/// Shares of a rectangle laid out as tiles as near square as they will go — Bruls, Huizing and van Wijk's squarified treemap, which
/// is the tiling d3 and so Mermaid lay a treemap with.
///
/// <para>
/// The shares are laid largest first, in rows along the shorter side of what is left, a row taking another share for as long as
/// that makes its least square tile squarer, then closing and leaving the rest of the rectangle to the next row.
/// </para>
/// </summary>
internal static class TreemapTiles
{
    /// <summary>
    /// The tile each of <paramref name="shares"/> takes of <paramref name="room"/>, in the order given — which is largest first, the
    /// order the tiling works in. A share of nothing takes a tile of nothing.
    /// </summary>
    public static IReadOnlyList<LayoutRect> Squarified(IReadOnlyList<double> shares, LayoutRect room)
    {
        var tiles = new LayoutRect[shares.Count];
        var total = shares.Sum(share => Math.Max(0, share));
        if (total <= 0 || room.Width <= 0 || room.Height <= 0)
        {
            for (var at = 0; at < tiles.Length; at++) tiles[at] = new LayoutRect(room.X, room.Y, 0, 0);
            return tiles;
        }

        var scale = room.Width * room.Height / total;
        var areas = shares.Select(share => Math.Max(0, share) * scale).ToArray();
        var left = room;

        for (var first = 0; first < areas.Length;)
        {
            var side = Math.Min(left.Width, left.Height);
            var end = first + 1;
            var worst = Worst(areas, first, end, side);

            while (end < areas.Length && Worst(areas, first, end + 1, side) <= worst)
                worst = Worst(areas, first, ++end, side);

            left = Laid(areas, first, end, left, tiles);
            first = end;
        }

        return tiles;
    }

    /// <summary>How far from square the least square tile of the row from <paramref name="first"/> to <paramref name="end"/> is, laid along <paramref name="side"/>.</summary>
    private static double Worst(double[] areas, int first, int end, double side)
    {
        var sum = 0.0;
        var most = 0.0;
        var least = double.PositiveInfinity;

        for (var at = first; at < end; at++)
        {
            sum += areas[at];
            most = Math.Max(most, areas[at]);
            least = Math.Min(least, areas[at]);
        }

        if (sum <= 0 || least <= 0) return double.PositiveInfinity;

        var squared = side * side;
        return Math.Max(squared * most / (sum * sum), sum * sum / (squared * least));
    }

    /// <summary>The row from <paramref name="first"/> to <paramref name="end"/> laid along the shorter side of <paramref name="room"/>, and what is left of it.</summary>
    private static LayoutRect Laid(double[] areas, int first, int end, LayoutRect room, LayoutRect[] tiles)
    {
        var sum = 0.0;
        for (var at = first; at < end; at++) sum += areas[at];

        if (room.Width >= room.Height)
        {
            // A column down the left, as wide as its share of the height.
            var across = room.Height > 0 ? sum / room.Height : 0;
            var down = room.Y;

            for (var at = first; at < end; at++)
            {
                var tall = across > 0 ? areas[at] / across : 0;
                tiles[at] = new LayoutRect(room.X, down, across, tall);
                down += tall;
            }

            return new LayoutRect(room.X + across, room.Y, Math.Max(0, room.Width - across), room.Height);
        }

        // A row across the top, as tall as its share of the width.
        var high = room.Width > 0 ? sum / room.Width : 0;
        var along = room.X;

        for (var at = first; at < end; at++)
        {
            var wide = high > 0 ? areas[at] / high : 0;
            tiles[at] = new LayoutRect(along, room.Y, wide, high);
            along += wide;
        }

        return new LayoutRect(room.X, room.Y + high, room.Width, Math.Max(0, room.Height - high));
    }
}
