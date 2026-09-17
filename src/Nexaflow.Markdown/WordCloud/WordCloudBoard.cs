namespace Nexaflow.Markdown.WordCloud;

/// <summary>Where a word landed: the top left of its shape, in pixels from the top left of the picture.</summary>
public readonly record struct WordSpot(double X, double Y);

/// <summary>
/// The picture as the packing sees it — a grid of cells, each either free or taken — and the rule for finding
/// a word a place in it.
///
/// <para>
/// The rule is <c>wordcloud2.js</c>'s and it is worth saying plainly, because everything else here serves it.
/// Words are put down heaviest first, and each takes the first place it fits, searching outwards from the
/// middle: the places at radius one, then at radius two, and so on to the corners. Nothing is ever moved once
/// it is down and nothing is backtracked, which is why the order matters and why the heaviest word is always
/// the one in the middle.
/// </para>
/// <para>
/// The <see cref="WordCloudSettings.Shape"/> is not a boundary — nothing is clipped by it. It pulls the ring
/// in at the angles where the outline is nearer the middle, so the ring at a given radius is the outline at
/// that scale, and the words simply run out where the outline is. That is the trick that makes a star-shaped
/// cloud out of nothing but a spiral.
/// </para>
/// <para>
/// The places at one radius are tried in a shuffled order. Tried in the order they were computed, every word
/// takes the first place going clockwise from the same angle, and the cloud combs — the words stack into
/// spokes with gaps between them.
/// </para>
/// </summary>
public sealed class WordCloudBoard
{
    private readonly bool[] _taken;
    private readonly WordCloudSettings _settings;
    private readonly WordCloudRandom _random;
    private readonly Dictionary<int, List<WordSpot>> _rings = [];
    private readonly double _grid;

    /// <param name="stencil">
    /// The shape the cloud is packed into, or null for the whole picture. Every cell outside it is taken
    /// before a word is placed, which is the whole of how a cloud comes out letter-shaped: the search knows
    /// nothing about it and simply finds those cells occupied.
    /// </param>
    public WordCloudBoard(double width, double height, WordCloudSettings settings, WordCloudRandom random,
                          WordCloudStencil? stencil = null)
    {
        _settings = settings;
        _random = random;
        _grid = Math.Max(settings.GridSize, WordCloudSettings.MinGridSize);

        Across = Math.Max(1, (int)Math.Ceiling(width / _grid));
        Down = Math.Max(1, (int)Math.Ceiling(height / _grid));

        _taken = new bool[Across * Down];

        if (stencil is null) return;

        // Sampled by share rather than by cell, so the stencil is free to be as fine or as coarse as whatever
        // made it — a letter's curves want more resolution than a board's four-pixel cells.
        for (var y = 0; y < Down; y++)
            for (var x = 0; x < Across; x++)
                _taken[y * Across + x] = !stencil.Holds((x + 0.5) / Across, (y + 0.5) / Down);
    }

    /// <summary>How many cells across the picture is.</summary>
    public int Across { get; }

    /// <summary>How many cells down it is.</summary>
    public int Down { get; }

    /// <summary>How far out the search goes: far enough for the middle of a word to reach any corner.</summary>
    private int Reach => (int)Math.Ceiling(Math.Sqrt((double)Across * Across + (double)Down * Down) / 2) + 1;

    /// <summary>
    /// Finds <paramref name="mask"/> a place and takes it, or false where the picture has no room left for
    /// that shape at all.
    /// </summary>
    public bool TryPlace(WordMask mask, out WordSpot at)
    {
        at = default;

        for (var radius = 0; radius <= Reach; radius++)
        {
            foreach (var spot in Ring(radius))
            {
                var x = (int)Math.Floor(spot.X - mask.Across / 2.0);
                var y = (int)Math.Floor(spot.Y - mask.Down / 2.0);

                if (!Fits(mask, x, y)) continue;

                Take(mask, x, y);
                at = new WordSpot(x * _grid, y * _grid);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The places to try at one radius, in cells from the top left. Worked out once per radius and kept,
    /// because every word asks for the same rings in the same order.
    /// </summary>
    private IReadOnlyList<WordSpot> Ring(int radius)
    {
        if (_rings.TryGetValue(radius, out var kept)) return Shuffled(kept);

        var middle = new WordSpot(Across / 2.0, Down / 2.0);
        var spots = new List<WordSpot>();

        if (radius == 0)
        {
            spots.Add(middle);
        }
        else
        {
            // Eight to a ring for each cell of radius, which keeps the places about a cell apart however far
            // out the ring is — the figure wordcloud2 uses, and the reason a big cloud does not get holes.
            var places = radius * 8;

            for (var step = 0; step < places; step++)
            {
                var angle = step / (double)places * Math.PI * 2;
                var reach = radius * WordCloudShapes.Reach(_settings.Shape, angle);

                spots.Add(new WordSpot(
                    middle.X + reach * Math.Cos(-angle),
                    middle.Y + reach * Math.Sin(-angle) * _settings.Ellipticity));
            }
        }

        _rings[radius] = spots;
        return Shuffled(spots);
    }

    private IReadOnlyList<WordSpot> Shuffled(List<WordSpot> spots)
    {
        if (!_settings.Shuffle || spots.Count < 2) return spots;

        var order = new List<WordSpot>(spots);
        _random.Shuffle(order);
        return order;
    }

    /// <summary>Whether every cell the word has ink in is inside the picture and still free.</summary>
    private bool Fits(WordMask mask, int x, int y)
    {
        if (x < 0 || y < 0 || x + mask.Across > Across || y + mask.Down > Down) return false;

        foreach (var cell in mask.Ink)
            if (_taken[(y + cell / mask.Across) * Across + x + cell % mask.Across]) return false;

        return true;
    }

    private void Take(WordMask mask, int x, int y)
    {
        foreach (var cell in mask.Ink)
            _taken[(y + cell / mask.Across) * Across + x + cell % mask.Across] = true;
    }
}
