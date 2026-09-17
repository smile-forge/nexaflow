namespace Nexaflow.Markdown.WordCloud;

/// <summary>
/// The shape of one word, coarsely: which cells of a grid its letters have ink in, and where that grid sits
/// relative to where the word is set.
///
/// <para>
/// <strong>This is what makes a cloud a cloud.</strong> Packed by their boxes, words are bricks and the
/// picture is a wall; packed by their letters, a short word slides under a capital's arm and into the bowl of
/// a <c>g</c>, which is the whole look of the thing. <c>wordcloud2.js</c> gets the shape by drawing each word
/// on a canvas and reading the pixels back; there is no canvas here, so the outlines the type engine hands
/// over are filled straight onto the grid — the same answer, without a rendering pass, and without needing a
/// desktop to ask the question.
/// </para>
/// <para>
/// The grid is deliberately coarse. It is what the fitting reads, once per candidate place and tens of
/// thousands of times per cloud, so a cell is a few pixels rather than one: at four pixels a cell, a word is
/// a few hundred of them instead of tens of thousands, and the packing is tighter than the eye can tell from
/// exact.
/// </para>
/// </summary>
public sealed class WordMask
{
    /// <summary>
    /// How many rows are filled per row of cells. Ink narrower than the gap between two samples is missed, so
    /// this is what decides whether the stem of an <c>l</c> at a small size is part of the word's shape.
    /// </summary>
    private const int Samples = 3;

    private WordMask(int across, int down, double left, double top, IReadOnlyList<int> ink)
    {
        Across = across;
        Down = down;
        Left = left;
        Top = top;
        Ink = ink;
    }

    /// <summary>How many cells across the word's shape is.</summary>
    public int Across { get; }

    /// <summary>How many cells down it is.</summary>
    public int Down { get; }

    /// <summary>Where the grid's left edge sits, in pixels, from where the word is set.</summary>
    public double Left { get; }

    /// <summary>Where the grid's top edge sits, in pixels, from where the word is set.</summary>
    public double Top { get; }

    /// <summary>The cells with ink in them, each as <c>y * <see cref="Across"/> + x</c>.</summary>
    public IReadOnlyList<int> Ink { get; }

    /// <summary>
    /// The shape of the letters in <paramref name="outline"/> — closed figures in pixels, already turned to
    /// the angle the word is set at — or null where they enclose nothing at all.
    /// </summary>
    /// <param name="grid">How many pixels a cell is.</param>
    /// <param name="gap">Clear air kept around the letters, in pixels.</param>
    public static WordMask? Of(IReadOnlyList<IReadOnlyList<(double X, double Y)>> outline, double grid, double gap)
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

        left -= gap;
        top -= gap;
        right += gap;
        bottom += gap;

        var across = Math.Max(1, (int)Math.Ceiling((right - left) / grid));
        var down = Math.Max(1, (int)Math.Ceiling((bottom - top) / grid));

        var cells = new bool[across * down];
        Fill(outline, cells, across, down, left, top, grid);
        Widen(cells, across, down, (int)Math.Ceiling(gap / grid));

        var ink = new List<int>();
        for (var at = 0; at < cells.Length; at++)
            if (cells[at]) ink.Add(at);

        return ink.Count == 0 ? null : new WordMask(across, down, left, top, ink);
    }

    /// <summary>
    /// Fills the outline onto the grid a row of samples at a time: where a row crosses the outline an odd
    /// number of turns deep, everything between that crossing and the next is inside the letters, and every
    /// cell that stretch touches has ink in it.
    /// </summary>
    /// <summary>
    /// Fills an outline onto a grid of cells a row of samples at a time: where a row crosses the outline an
    /// odd number of turns deep, everything between that crossing and the next is inside it, and every cell
    /// that stretch touches is filled.
    ///
    /// <para>
    /// Shared with <see cref="WordCloudStencil"/>, which fills the shape a cloud is packed into by exactly
    /// the same rule — a letter is a letter whether a word is made of it or packed into it.
    /// </para>
    /// </summary>
    public static void Fill(IReadOnlyList<IReadOnlyList<(double X, double Y)>> outline, bool[] cells,
                            int across, int down, double left, double top, double grid)
    {
        var step = grid / Samples;
        var crossings = new List<(double At, int Way)>();

        for (var row = 0; row < down * Samples; row++)
        {
            var y = top + (row + 0.5) * step;
            var line = (int)((y - top) / grid);
            if (line < 0 || line >= down) continue;

            crossings.Clear();

            foreach (var figure in outline)
                for (var at = 0; at < figure.Count; at++)
                {
                    var (x1, y1) = figure[at];
                    var (x2, y2) = figure[(at + 1) % figure.Count];

                    if (y1 == y2) continue;
                    if (y < Math.Min(y1, y2) || y >= Math.Max(y1, y2)) continue;

                    crossings.Add((x1 + (y - y1) * (x2 - x1) / (y2 - y1), y2 > y1 ? 1 : -1));
                }

            if (crossings.Count < 2) continue;
            crossings.Sort((one, other) => one.At.CompareTo(other.At));

            var depth = 0;
            for (var at = 0; at < crossings.Count - 1; at++)
            {
                depth += crossings[at].Way;
                if (depth == 0) continue;

                var from = Math.Max(0, (int)((crossings[at].At - left) / grid));
                var to = Math.Min(across - 1, (int)((crossings[at + 1].At - left) / grid));

                for (var column = from; column <= to; column++) cells[line * across + column] = true;
            }
        }
    }

    /// <summary>
    /// Spreads the ink <paramref name="by"/> cells in every direction — the clear air a word keeps around it,
    /// made part of its shape so the fitting has nothing extra to know.
    /// </summary>
    private static void Widen(bool[] cells, int across, int down, int by)
    {
        if (by <= 0) return;

        // Across, then down. Spreading a square is spreading a line twice, which is the difference between
        // touching every cell within the square of each one and touching each of them twice.
        var wide = new bool[cells.Length];

        for (var y = 0; y < down; y++)
            for (var x = 0; x < across; x++)
            {
                if (!cells[y * across + x]) continue;

                var to = Math.Min(across - 1, x + by);
                for (var column = Math.Max(0, x - by); column <= to; column++) wide[y * across + column] = true;
            }

        var tall = new bool[cells.Length];

        for (var y = 0; y < down; y++)
            for (var x = 0; x < across; x++)
            {
                if (!wide[y * across + x]) continue;

                var to = Math.Min(down - 1, y + by);
                for (var row = Math.Max(0, y - by); row <= to; row++) tall[row * across + x] = true;
            }

        Array.Copy(tall, cells, cells.Length);
    }
}
