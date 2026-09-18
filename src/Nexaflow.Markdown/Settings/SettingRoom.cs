namespace Nexaflow.Markdown.Settings;

/// <summary>
/// How big a block draws itself when it was given a column to fit into rather than a size.
///
/// <para>
/// The same answer for every block that draws a picture rather than a line of text: as wide as the
/// column, up to a limit past which a picture stops being easier to read for being bigger, and as tall
/// as its own proportion of that — unless the block said otherwise, in which case it said otherwise.
/// </para>
/// </summary>
public static class SettingRoom
{
    public const double MinSide = 80;
    public const double MaxSide = 4000;

    /// <summary>The widest a block with no width of its own is drawn, however wide the column is.</summary>
    public const double RoomLimit = 900;

    /// <summary>
    /// The size to draw at: what the block asked for where it asked, and the column it is in otherwise.
    /// </summary>
    /// <param name="width">What the block wrote, or nought for "as wide as the column".</param>
    /// <param name="height">What the block wrote, or nought for <paramref name="share"/> of the width.</param>
    /// <param name="room">How much room the column leaves.</param>
    /// <param name="share">How tall the block is for how wide it is, where it was not told.</param>
    public static (double Wide, double Tall) Fit(double width, double height, double room, double share)
    {
        var wide = Math.Clamp(width > 0 ? width : Math.Min(room, RoomLimit), MinSide, MaxSide);
        var tall = Math.Clamp(height > 0 ? height : wide * share, MinSide, MaxSide);

        return (wide, tall);
    }
}
