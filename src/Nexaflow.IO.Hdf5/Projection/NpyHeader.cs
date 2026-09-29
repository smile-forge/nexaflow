using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Nexaflow.IO.Hdf5.Projection;

/// <summary>
/// The header of a NumPy <c>.npy</c> file: magic, version, and a Python dict literal naming the element dtype
/// and the shape, padded so the data starts on a 64-byte boundary. Version 1.0 unless the header outgrows its
/// 16-bit length (2.0) or a field name is not Latin-1 (3.0, UTF-8).
/// </summary>
internal static class NpyHeader
{
    private static readonly byte[] Magic = [0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y'];

    public static byte[] Build(Hdf5Type type, Hdf5Dataspace space)
    {
        var (descr, shape) = Describe(type, space);
        var dict = $"{{'descr': {descr}, 'fortran_order': False, 'shape': {Tuple(shape)}, }}";

        bool utf8     = dict.Any(c => c > 0xFF);
        var  encoding = utf8 ? Encoding.UTF8 : Encoding.Latin1;
        int  dictLen  = encoding.GetByteCount(dict);
        byte major    = utf8 ? (byte)3 : 10 + dictLen + 1 > ushort.MaxValue ? (byte)2 : (byte)1;
        int  prefix   = major == 1 ? 10 : 12;
        int  total    = (prefix + dictLen + 1 + 63) / 64 * 64;
        int  headerLen = total - prefix;

        var bytes = new byte[total];
        Magic.CopyTo(bytes, 0);
        bytes[6] = major;
        bytes[7] = 0;
        if (major == 1) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)headerLen);
        else            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)headerLen);
        encoding.GetBytes(dict, bytes.AsSpan(prefix));
        bytes.AsSpan(prefix + dictLen, total - prefix - dictLen - 1).Fill((byte)' ');
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    /// <summary>
    /// The dtype and shape NumPy reads. An array-typed element becomes trailing dimensions of the base type —
    /// how NumPy itself unpacks a sub-array dtype at the top level.
    /// </summary>
    private static (string Descr, IReadOnlyList<ulong> Shape) Describe(Hdf5Type type, Hdf5Dataspace space)
    {
        IReadOnlyList<ulong> shape = space.Kind switch
        {
            Hdf5SpaceKind.Scalar => [],
            Hdf5SpaceKind.Null   => [0],
            _                    => space.Shape,
        };
        if (type is { Class: Hdf5TypeClass.Array, Base: { Size: > 0 } b })
            return (Descr(b), [.. shape, (ulong)(type.Size / b.Size)]);
        return (Descr(type), shape);
    }

    private static string Descr(Hdf5Type t) => t.Class switch
    {
        Hdf5TypeClass.Integer when t.Size is 1 or 2 or 4 or 8 => Quote($"{Order(t.Size)}{(t.IsSigned ? 'i' : 'u')}{t.Size}"),
        Hdf5TypeClass.Float   when t.Size is 2 or 4 or 8      => Quote($"<f{t.Size}"),
        Hdf5TypeClass.BitField when t.Size is 1 or 2 or 4 or 8 => Quote($"{Order(t.Size)}u{t.Size}"),
        Hdf5TypeClass.Enum when t.Base is { } b                => Descr(b),
        Hdf5TypeClass.String                                   => Quote($"|S{t.Size}"),
        Hdf5TypeClass.Compound                                 => Fields(t),
        Hdf5TypeClass.Array when t.Base is { Size: > 0 } b     => $"[('', {Descr(b)}, ({t.Size / b.Size},))]",
        _                                                      => Quote($"|V{t.Size}"),
    };

    /// <summary>A structured dtype, fields in offset order with unnamed void fields filling any padding.</summary>
    private static string Fields(Hdf5Type t)
    {
        var parts  = new List<string>();
        int cursor = 0;
        foreach (var f in t.Fields.OrderBy(f => f.Offset))
        {
            if (f.Offset > cursor) parts.Add($"('', {Quote($"|V{f.Offset - cursor}")})");
            parts.Add(f.Type is { Class: Hdf5TypeClass.Array, Base: { Size: > 0 } b }
                ? $"({Quote(f.Name)}, {Descr(b)}, ({f.Type.Size / b.Size},))"
                : $"({Quote(f.Name)}, {Descr(f.Type)})");
            cursor = f.Offset + f.Type.Size;
        }
        if (t.Size > cursor) parts.Add($"('', {Quote($"|V{t.Size - cursor}")})");
        return "[" + string.Join(", ", parts) + "]";
    }

    private static char Order(int size) => size == 1 ? '|' : '<';

    private static string Quote(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static string Tuple(IReadOnlyList<ulong> shape) => shape.Count switch
    {
        0 => "()",
        1 => $"({shape[0].ToString(CultureInfo.InvariantCulture)},)",
        _ => "(" + string.Join(", ", shape.Select(d => d.ToString(CultureInfo.InvariantCulture))) + ")",
    };
}
