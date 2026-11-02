namespace Nexaflow.Markdown.Layout;

/// <summary>
/// What of a content's height a builder is asked to lay — from <paramref name="Top"/> to <paramref name="Bottom"/> of its own frame,
/// the screen and a screen either side of it — and places in what it reads that must stand laid besides: where the caret is, each end of
/// what is picked out, where the reader went. A builder that can lay part of what it reads lays at least these, leaves room for the
/// rest, and says what it laid (<see cref="LayoutReached"/>); one that cannot lays all of it.
/// </summary>
public sealed record LayoutReach(double Top, double Bottom, IReadOnlyList<int> Places);

/// <summary>
/// What a builder laid where it laid part of what it read (<see cref="LayoutReach"/>): the stretch of its height it was asked for, and
/// the stretches of what it read that it laid, each from where it starts to where it ends, in order.
/// </summary>
public sealed record LayoutReached(double Top, double Bottom, IReadOnlyList<(int Start, int End)> Stretches)
{
    /// <summary>Whether what stands at <paramref name="offset"/> was laid — one of the stretches holds it, either end included.</summary>
    public bool Holds(int offset)
    {
        var (low, high) = (0, this.Stretches.Count - 1);

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var (start, end) = this.Stretches[middle];

            if (offset < start) high = middle - 1;
            else if (offset > end) low = middle + 1;
            else return true;
        }

        return false;
    }

    /// <summary>
    /// Whether this, everything it laid standing <paramref name="by"/> further along now, lays all <paramref name="reach"/> asks of content
    /// <paramref name="height"/> tall: the stretch of its height asked for, as far as the content reaches, and every place.
    /// </summary>
    public bool Covers(LayoutReach reach, double height, int by)
    {
        var (top, bottom) = (Math.Max(reach.Top, 0), Math.Min(reach.Bottom, height));
        if (top < bottom && (top < this.Top || bottom > this.Bottom)) return false;

        foreach (var place in reach.Places)
            if (!this.Holds(place - by)) return false;

        return true;
    }
}
