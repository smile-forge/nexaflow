namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// The arithmetic behind a rubber-band selection over a uniform-height, item-scrolling list:
/// a pointer position becomes an item index, and two indices become the run of rows the band covers.
/// <para>
/// It works in <em>index</em> space rather than pixel space, and that is the whole point. The lists
/// this serves virtualise with recycling, so a container exists only for a row currently on screen —
/// a band that reaches past the viewport (or auto-scrolls into rows that were never realised) cannot
/// be resolved by hit-testing the visual tree at all. Two properties of the lists make the index
/// exact rather than a guess: every row is the same height, and the list scrolls by item, so a
/// <c>ScrollViewer</c>'s vertical offset <em>is</em> the index of the first visible row.
/// </para>
/// Kept free of WPF types so it can be tested as the arithmetic it is, with no dispatcher and no
/// STA thread.
/// </summary>
public static class MarqueeRange
{
    /// <summary>
    /// The index of the row at <paramref name="y"/>, measured from the top of the first visible row.
    /// <para>
    /// Returns <paramref name="itemCount"/> — one past the last row — when the point is below the
    /// last one. That sentinel is not an error case but the ordinary one: a band is started in the
    /// empty space <em>under</em> the rows, which is the only place a press can land without hitting
    /// a row and being a click on it instead.
    /// </para>
    /// </summary>
    public static int IndexAt(double y, double rowHeight, int firstVisibleIndex, int itemCount)
    {
        if (itemCount <= 0 || rowHeight <= 0 || double.IsNaN(y) || double.IsInfinity(y)) return itemCount;

        // Floor, not truncate: a point above the first visible row has a negative y and must resolve
        // to the row before it, which truncation-toward-zero would round the wrong way.
        double raw = firstVisibleIndex + Math.Floor(y / rowHeight);

        if (raw < 0) return 0;
        if (raw >= itemCount) return itemCount;
        return (int)raw;
    }

    /// <summary>
    /// The inclusive run of rows a band between two indices covers, or <c>null</c> when it covers
    /// none — a band drawn entirely in the empty space below the last row, which is how "drag a box
    /// over nothing" ends up clearing the selection rather than being a special case anywhere else.
    /// </summary>
    public static (int First, int Last)? Resolve(int anchorIndex, int currentIndex, int itemCount)
    {
        if (itemCount <= 0) return null;

        int first = Math.Max(0, Math.Min(anchorIndex, currentIndex));
        int last  = Math.Min(itemCount - 1, Math.Max(anchorIndex, currentIndex));

        return first > last ? null : (first, last);
    }
}
