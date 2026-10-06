namespace Nexaflow.IO.Hdf5;

/// <summary>
/// The elements of one selection of a dataset, in C order over <see cref="Shape"/>. A compound block names
/// its members in <see cref="Fields"/>, so a table can give each one a column.
/// </summary>
public sealed class Hdf5Block
{
    private readonly IBlockData _data;

    internal Hdf5Block(Hdf5Type type, IReadOnlyList<ulong> shape, IBlockData data)
    {
        Type  = type;
        Shape = shape;
        Count = (long)shape.Aggregate(1UL, (n, d) => n * d);
        _data = data;
    }

    internal static Hdf5Block Empty(Hdf5Type type, IReadOnlyList<ulong> shape) =>
        new(type, shape.Count == 0 ? [0] : shape, EmptyBlockData.Instance);

    public Hdf5Type Type { get; }
    public IReadOnlyList<ulong> Shape { get; }
    public long Count { get; }

    public IReadOnlyList<string> Fields => Type.Class == Hdf5TypeClass.Compound ? [.. Type.Fields.Select(f => f.Name)] : [];

    /// <summary>
    /// One element as the viewer shows it, culture-invariant. <paramref name="field"/> picks a compound member
    /// by index; -1 is the whole element, a compound one reading <c>{t: 0.001, ch: 1}</c>.
    /// </summary>
    public string Format(long element, int field = -1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(element);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(element, Count);
        return _data.Format(element, field);
    }

    /// <summary>
    /// Writes every element packed little-endian, element <c>i</c> at <c>i × Type.Size</c> with compound members
    /// at their file offsets — the layout NumPy reads. Only a fixed-size type has one.
    /// </summary>
    public void CopyTo(Span<byte> destination)
    {
        if (!Type.IsFixedSize)
            throw new InvalidOperationException($"{Type.DisplayName} elements have no fixed-size layout.");
        long bytes = Count * Type.Size;
        if (destination.Length < bytes)
            throw new ArgumentException($"The destination holds {destination.Length} bytes; the block needs {bytes}.");
        _data.CopyTo(destination[..(int)bytes]);
    }
}
