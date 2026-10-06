using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Nexaflow.IO.Hdf5;

/// <summary>
/// Formats and encodes single HDF5 values. Formatting is culture-invariant, so what the viewer shows is what a
/// copy or export writes. Stored bytes are read little-endian: the reader refuses a big-endian dataset outright,
/// so raw bytes only ever reach here from little-endian data.
/// </summary>
internal static class Hdf5ValueCodec
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A value as the reader decoded it into a compound record or sequence.</summary>
    public static string Format(object? value, Hdf5Type type) => value switch
    {
        null                              => string.Empty,
        string s                          => s,
        Dictionary<string, object> record => "{" + string.Join(", ", type.Fields.Select(f =>
                                                 $"{f.Name}: {Format(record.GetValueOrDefault(f.Name), f.Type)}")) + "}",
        byte[] b when type.Class is Hdf5TypeClass.Opaque or Hdf5TypeClass.BitField or Hdf5TypeClass.Time => Hex(b),
        Array a                           => "[" + string.Join(", ", a.Cast<object?>().Select(e => Format(e, type.Base ?? type))) + "]",
        _ when type.Class == Hdf5TypeClass.Enum => EnumName(Convert.ToInt64(value, Inv), type),
        IFormattable f                    => f.ToString(null, Inv),
        _                                 => value.ToString() ?? string.Empty,
    };

    /// <summary>One element from its stored bytes.</summary>
    public static string FormatRaw(ReadOnlySpan<byte> bytes, Hdf5Type type)
    {
        switch (type.Class)
        {
            case Hdf5TypeClass.Integer when TryInteger(bytes, type, out var n):
                return n;
            case Hdf5TypeClass.Float when type.Size is 2 or 4 or 8:
                return type.Size switch
                {
                    2 => BinaryPrimitives.ReadHalfLittleEndian(bytes).ToString(null, Inv),
                    4 => BinaryPrimitives.ReadSingleLittleEndian(bytes).ToString(null, Inv),
                    _ => BinaryPrimitives.ReadDoubleLittleEndian(bytes).ToString(null, Inv),
                };
            case Hdf5TypeClass.Enum when type.Base is { } b && TryInteger(bytes, b, out var raw):
                return EnumName(long.Parse(raw, Inv), type);
            case Hdf5TypeClass.String:
                int end = bytes.IndexOf((byte)0);
                return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
            case Hdf5TypeClass.Array when type.Base is { Size: > 0 } b:
                var parts = new List<string>(type.Size / b.Size);
                for (int o = 0; o + b.Size <= bytes.Length; o += b.Size) parts.Add(FormatRaw(bytes.Slice(o, b.Size), b));
                return "[" + string.Join(", ", parts) + "]";
            case Hdf5TypeClass.Compound:
                var fields = new List<string>(type.Fields.Count);
                foreach (var f in type.Fields) fields.Add($"{f.Name}: {FormatRaw(bytes.Slice(f.Offset, f.Type.Size), f.Type)}");
                return "{" + string.Join(", ", fields) + "}";
            default:
                return Hex(bytes);
        }
    }

    /// <summary>
    /// Writes a decoded value into its packed little-endian slot: <paramref name="destination"/> is exactly
    /// <c>type.Size</c> bytes, and anything a value does not cover (compound padding) is zero.
    /// </summary>
    public static void Encode(object? value, Hdf5Type type, Span<byte> destination)
    {
        destination.Clear();
        switch (value)
        {
            case null:
                return;
            case Dictionary<string, object> record:
                foreach (var f in type.Fields)
                    Encode(record.GetValueOrDefault(f.Name), f.Type, destination.Slice(f.Offset, f.Type.Size));
                return;
            case byte[] bytes when type.Class is not (Hdf5TypeClass.Array or Hdf5TypeClass.VariableLength):
                bytes.AsSpan(0, Math.Min(bytes.Length, destination.Length)).CopyTo(destination);
                return;
            case string s:
                var utf8 = Encoding.UTF8.GetBytes(s);
                utf8.AsSpan(0, Math.Min(utf8.Length, destination.Length)).CopyTo(destination);
                return;
            case Array a when type.Base is { Size: > 0 } b:
                int i = 0;
                foreach (var e in a)
                {
                    if ((i + 1) * b.Size > destination.Length) break;
                    Encode(e, b, destination.Slice(i++ * b.Size, b.Size));
                }
                return;
            case bool flag: destination[0] = flag ? (byte)1 : (byte)0; return;
            case Half h:   BinaryPrimitives.WriteHalfLittleEndian(destination, h);   return;
            case float f:  BinaryPrimitives.WriteSingleLittleEndian(destination, f); return;
            case double d: BinaryPrimitives.WriteDoubleLittleEndian(destination, d); return;
            case sbyte or short or int or long:
                WriteInteger(unchecked((ulong)Convert.ToInt64(value, Inv)), destination);
                return;
            case byte or ushort or uint or ulong:
                WriteInteger(Convert.ToUInt64(value, Inv), destination);
                return;
            default:
                throw new NotSupportedException($"A {value.GetType().Name} cannot be stored as {type.DisplayName}.");
        }
    }

    public static string Hex(ReadOnlySpan<byte> bytes) => bytes.IsEmpty ? string.Empty : "0x" + Convert.ToHexString(bytes);

    private static string EnumName(long value, Hdf5Type type) =>
        type.Members.FirstOrDefault(m => m.Value == value)?.Name ?? value.ToString(Inv);

    private static bool TryInteger(ReadOnlySpan<byte> bytes, Hdf5Type type, out string text)
    {
        text = (type.Size, type.IsSigned) switch
        {
            (1, true)  => ((sbyte)bytes[0]).ToString(Inv),
            (1, false) => bytes[0].ToString(Inv),
            (2, true)  => BinaryPrimitives.ReadInt16LittleEndian(bytes).ToString(Inv),
            (2, false) => BinaryPrimitives.ReadUInt16LittleEndian(bytes).ToString(Inv),
            (4, true)  => BinaryPrimitives.ReadInt32LittleEndian(bytes).ToString(Inv),
            (4, false) => BinaryPrimitives.ReadUInt32LittleEndian(bytes).ToString(Inv),
            (8, true)  => BinaryPrimitives.ReadInt64LittleEndian(bytes).ToString(Inv),
            (8, false) => BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString(Inv),
            _          => string.Empty,
        };
        return text.Length > 0;
    }

    private static void WriteInteger(ulong bits, Span<byte> destination)
    {
        for (int i = 0; i < destination.Length && i < 8; i++)
            destination[i] = (byte)(bits >> (8 * i));
    }
}
