namespace Nexaflow.Markdown.WordCloud;

/// <summary>
/// The shape a cloud is packed <em>into</em> — a grid of cells saying where a word may go and where it may
/// not.
///
/// <para>
/// This is what turns a cloud into a picture of something: letters spelling a word, or a silhouette taken
/// from an image. It is not the <see cref="WordCloudShape"/>: a shape pulls the search's rings in and the
/// words simply run out at the outline, which is enough for a circle or a star and hopeless for a letter,
/// whose edge is not a function of its angle. A stencil is the other way round — every cell outside it is
/// taken before a single word is placed, so the packing fills the shape from the inside and never has to
/// know what shape it is.
/// </para>
/// <para>
/// It is asked about in shares of itself rather than in cells, so whoever builds one chooses its
/// resolution — a letter is filled as finely as its curves deserve, a picture at whatever size it came —
/// and the board samples it against its own grid without either having to agree with the other.
/// </para>
/// </summary>
public sealed class WordCloudStencil
{
    private readonly bool[] _inside;

    public WordCloudStencil(bool[] inside, int across, int down)
    {
        _inside = inside;
        Across = across;
        Down = down;

        var room = 0;
        foreach (var cell in inside)
            if (cell) room++;

        Room = room;
    }

    public int Across { get; }

    public int Down { get; }

    /// <summary>How many of its cells are inside the shape — nought for a stencil nothing could be packed into.</summary>
    public int Room { get; }

    /// <summary>Whether a word may stand at this point, given as a share of the stencil's width and height.</summary>
    public bool Holds(double across, double down)
    {
        var x = (int)(across * Across);
        var y = (int)(down * Down);

        if (x < 0 || y < 0 || x >= Across || y >= Down) return false;

        return _inside[y * Across + x];
    }

    /// <summary>
    /// The shape an outline encloses — letters, or any other closed figures, in pixels — filled at
    /// <paramref name="grid"/> pixels to the cell.
    /// </summary>
    public static WordCloudStencil? Of(IReadOnlyList<IReadOnlyList<(double X, double Y)>> outline, double grid)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

        foreach (var figure in outline)
            foreach (var (x, y) in figure)
            {
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }

        if (left > right || top > bottom) return null;

        var across = Math.Max(1, (int)Math.Ceiling((right - left) / grid));
        var down = Math.Max(1, (int)Math.Ceiling((bottom - top) / grid));

        var cells = new bool[across * down];
        WordMask.Fill(outline, cells, across, down, left, top, grid);

        var stencil = new WordCloudStencil(cells, across, down);
        return stencil.Room == 0 ? null : stencil;
    }

    /// <summary>
    /// The shape a picture holds, asked a pixel at a time — <paramref name="holds"/> says whether the pixel
    /// at that column and row is part of the silhouette.
    /// </summary>
    public static WordCloudStencil? Of(Func<int, int, bool> holds, int across, int down)
    {
        if (across <= 0 || down <= 0) return null;

        var cells = new bool[across * down];

        for (var y = 0; y < down; y++)
            for (var x = 0; x < across; x++)
                cells[y * across + x] = holds(x, y);

        var stencil = new WordCloudStencil(cells, across, down);
        return stencil.Room == 0 ? null : stencil;
    }
}
