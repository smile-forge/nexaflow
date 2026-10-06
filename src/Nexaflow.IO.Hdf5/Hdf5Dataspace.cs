namespace Nexaflow.IO.Hdf5;

/// <summary>A dataspace holds one element (scalar), an N-D array of them (simple), or nothing (null).</summary>
public enum Hdf5SpaceKind { Scalar, Simple, Null }

/// <summary>
/// The shape of a dataset or attribute. <see cref="MaxShape"/> holds null for an unlimited dimension — one a
/// chunked dataset can still grow along.
/// </summary>
public sealed record Hdf5Dataspace(Hdf5SpaceKind Kind, IReadOnlyList<ulong> Shape, IReadOnlyList<ulong?> MaxShape)
{
    public static Hdf5Dataspace Scalar { get; } = new(Hdf5SpaceKind.Scalar, [], []);

    public int Rank => Shape.Count;

    public ulong ElementCount => Kind switch
    {
        Hdf5SpaceKind.Null   => 0,
        Hdf5SpaceKind.Scalar => 1,
        _                    => Shape.Aggregate(1UL, (n, d) => n * d),
    };

    /// <summary>True when some dimension may grow beyond its current extent.</summary>
    public bool IsResizable => MaxShape.Where((m, i) => m is null || m.Value != Shape[i]).Any();

    /// <summary><c>500 × 16</c>; an unlimited dimension prints as <c>∞</c>.</summary>
    public static string FormatDims(IEnumerable<ulong?> dims) =>
        string.Join(" × ", dims.Select(d => d is { } v ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "∞"));
}
