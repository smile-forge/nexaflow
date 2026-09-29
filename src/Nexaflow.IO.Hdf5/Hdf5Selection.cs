namespace Nexaflow.IO.Hdf5;

/// <summary>
/// A rectangular block of a dataset: for each dimension, the first index and how many follow. A scalar
/// dataset takes the empty selection.
/// </summary>
public sealed record Hdf5Selection(IReadOnlyList<ulong> Start, IReadOnlyList<ulong> Count)
{
    public static Hdf5Selection Scalar { get; } = new([], []);

    public int Rank => Start.Count;

    public ulong ElementCount => Count.Aggregate(1UL, (n, c) => n * c);

    /// <summary>The whole of a dataspace.</summary>
    public static Hdf5Selection All(Hdf5Dataspace space) =>
        space.Kind == Hdf5SpaceKind.Simple ? new([.. space.Shape.Select(_ => 0UL)], space.Shape) : Scalar;

    /// <summary>
    /// Covers a shape in C order with selections of at most <paramref name="elementBudget"/> elements each (one
    /// element at least), so a whole dataset streams through bounded memory. Trailing dimensions that fit are
    /// taken whole; the first that does not is stepped through; the ones before it one index at a time.
    /// </summary>
    public static IEnumerable<Hdf5Selection> Walk(IReadOnlyList<ulong> shape, ulong elementBudget)
    {
        int rank = shape.Count;
        if (rank == 0) { yield return Scalar; yield break; }
        if (shape.Any(d => d == 0)) yield break;
        elementBudget = Math.Max(1, elementBudget);

        int k = rank - 1;
        ulong inner = 1;
        while (k >= 0 && inner * shape[k] <= elementBudget) inner *= shape[k--];
        if (k < 0) { yield return new([.. shape.Select(_ => 0UL)], shape); yield break; }

        ulong step  = Math.Max(1, elementBudget / inner);
        var   index = new ulong[k];
        while (true)
        {
            for (ulong s = 0; s < shape[k]; s += step)
            {
                var start = new ulong[rank];
                var count = new ulong[rank];
                for (int j = 0; j < k; j++) { start[j] = index[j]; count[j] = 1; }
                start[k] = s;
                count[k] = Math.Min(step, shape[k] - s);
                for (int j = k + 1; j < rank; j++) count[j] = shape[j];
                yield return new(start, count);
            }
            int d = k - 1;
            while (d >= 0 && ++index[d] == shape[d]) index[d--] = 0;
            if (d < 0) yield break;
        }
    }

    /// <summary>Throws unless this selection has the space's rank and lies inside it.</summary>
    public void Validate(Hdf5Dataspace space)
    {
        if (Start.Count != Count.Count)
            throw new ArgumentException("A selection needs a start and a count for every dimension.");
        if (space.Kind != Hdf5SpaceKind.Simple)
        {
            if (Rank != 0) throw new ArgumentException("A scalar or null dataspace takes the empty selection.");
            return;
        }
        if (Rank != space.Rank)
            throw new ArgumentException($"The selection has rank {Rank} but the dataspace has rank {space.Rank}.");
        for (int d = 0; d < Rank; d++)
            if (Start[d] + Count[d] > space.Shape[d])
                throw new ArgumentOutOfRangeException(nameof(Start),
                    $"Dimension {d}: {Start[d]} + {Count[d]} is past its extent {space.Shape[d]}.");
    }
}
