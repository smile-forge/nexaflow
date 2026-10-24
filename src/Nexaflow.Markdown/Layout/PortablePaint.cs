namespace Nexaflow.Markdown.Layout;

/// <summary>
/// How a piece is painted beyond the marks it holds: turned, scaled, snapped to a pixel grid, clipped, faded, masked, and
/// kept as a picture once painted. A side table, null for nearly every piece.
/// </summary>
/// <param name="Turn">How the piece is turned inside whatever holds it, applied in order about its own anchor.</param>
/// <param name="Snap">The pixel grid the piece's edges land on, in its own frame.</param>
/// <param name="Kept">Where the piece's picture is kept once painted.</param>
/// <param name="Scale">How much larger than it was laid out the piece is drawn, inside any turn.</param>
/// <param name="Clip">The only part of the piece that shows, in its own frame.</param>
/// <param name="Opacity">How strongly the piece shows, from 0 to 1.</param>
/// <param name="Mask">How strongly each part shows, as the alpha of an ink laid over it.</param>
public sealed record PortablePaint(IReadOnlyList<LayoutTurn>? Turn = null, LayoutGuides? Snap = null, PortableKept? Kept = null,
                                   double Scale = 1, LayoutShape? Clip = null, double Opacity = 1, LayoutInk? Mask = null)
{
    public bool Matters => Turn is { Count: > 0 } || Snap is not null || Kept is not null || Scale != 1 || Clip is not null
                           || Opacity < 1 || Mask is not null;

    public bool Applies => Turn is { Count: > 0 } || Scale != 1;

    /// <summary>How what the piece holds is turned and scaled about its anchor, as one matrix — or null where it is neither.</summary>
    public LayoutMatrix? Applied
    {
        get
        {
            if (!Applies) return null;

            var applied = LayoutMatrix.Identity;
            if (Scale != 1) applied = LayoutMatrix.Scaling(Scale, Scale);

            if (Turn is { } turns)
                for (var at = turns.Count - 1; at >= 0; at--) applied *= turns[at].Matrix;

            return applied;
        }
    }
}



/// <summary>
/// A piece's picture, recorded by the medium the first time it is painted and drawn whole from then on. What a picture is
/// belongs to the medium; this only keeps it, by the ink it was painted with.
/// </summary>
public sealed class PortableKept
{
    private (LayoutInk Ink, object Picture)[] _kept = [];

    /// <summary>The picture painted with <paramref name="ink"/>, recorded by <paramref name="record"/> the first time it is asked for.</summary>
    public TPicture For<TPicture>(LayoutInk ink, Func<TPicture> record) where TPicture : class
    {
        foreach (var (kept, picture) in _kept)
            if (ReferenceEquals(kept, ink) && picture is TPicture known) return known;

        var recorded = record();
        _kept = _kept.Length < 4 ? [.. _kept, (ink, recorded)] : [(ink, recorded)];
        return recorded;
    }

    public bool Holds => _kept.Length > 0;

    public void Forget() => _kept = [];
}

/// <summary>A run of text as one piece, with a caret position between any two of its letters — see the WPF layout's words.</summary>
public sealed record PortableWords(IShapedText Glyphs, LayoutPoint At, bool Maps, bool Writes = false)
{
    public int Length => Glyphs.Text.Length;

    public int IndexAt(double x) => IndexAt(new LayoutPoint(x, double.NegativeInfinity));

    public int IndexAt(LayoutPoint at)
    {
        var letters = new LayoutRect[Length];
        for (var index = 0; index < Length; index++) letters[index] = Box(index);
        if (letters.Length == 0) return 0;

        var line = letters.Min(letter => letter.Y);
        foreach (var letter in letters)
            if (letter.Y <= at.Y) line = Math.Max(line, letter.Y);

        var last = -1;
        for (var index = 0; index < letters.Length; index++)
        {
            var letter = letters[index];
            if (Math.Abs(letter.Y - line) > Level) continue;

            last = index;
            if (at.X > letter.Right) continue;
            return at.X <= letter.X + (letter.Width / 2) ? index : index + 1;
        }

        return last + 1;
    }

    private const double Level = 0.5;

    public LayoutRect Caret(int index)
    {
        if (Length == 0) return new LayoutRect(At.X, At.Y, 0, Glyphs.Height);

        var at = Math.Clamp(index, 0, Length);
        var letter = Box(at == Length ? Length - 1 : at);
        return new LayoutRect(at == Length ? letter.Right : letter.X, letter.Y, 0, letter.Height);
    }

    public LayoutRect Covers(int from, int to)
    {
        var start = Math.Clamp(Math.Min(from, to), 0, Length);
        var end = Math.Clamp(Math.Max(from, to), 0, Length);
        return end <= start ? LayoutRect.Empty : Glyphs.Covers(At, start, end - start);
    }

    private LayoutRect Box(int index) =>
        Glyphs.Covers(At, index, 1) is { IsEmpty: false } box ? box : new LayoutRect(At.X, At.Y, 0, Glyphs.Height);
}
