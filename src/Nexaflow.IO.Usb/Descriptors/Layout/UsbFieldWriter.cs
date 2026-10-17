using System.Buffers.Binary;

namespace Nexaflow.IO.Usb.Descriptors.Layout;

/// <summary>
/// Reads one descriptor field by field, recording each as a <see cref="UsbDescriptorField"/> at its blob offset.
/// </summary>
/// <remarks>
/// A descriptor shorter than its standard says is ordinary on hardware nobody tested, so a field that would run
/// past the end is not an error: whatever bytes remain become one truncated field, and every read after it
/// returns zero and records nothing.
/// </remarks>
public sealed class UsbFieldWriter
{
    readonly byte[] _blob;
    readonly int _start;
    readonly List<UsbDescriptorField> _fields = [];
    int _cursor;
    bool _truncated;

    public UsbFieldWriter(byte[] blob, int start, int length)
    {
        _blob = blob;
        _start = start;
        Length = length;
    }

    public int Length { get; }
    public ReadOnlySpan<byte> Bytes => _blob.AsSpan(_start, Length);
    public byte Type => Length > 1 ? _blob[_start + 1] : (byte)0;

    /// <summary>The third byte, which class-specific descriptors use as bDescriptorSubtype.</summary>
    public byte Subtype => Length > 2 ? _blob[_start + 2] : (byte)0;

    public int Remaining => _truncated ? 0 : Length - _cursor;
    public IReadOnlyList<UsbDescriptorField> Fields => _fields;

    /// <summary>Forgets every field written, for a decoder that looked and declined.</summary>
    public void Reset()
    {
        _fields.Clear();
        _cursor = 0;
        _truncated = false;
    }

    public byte U8(string name, Func<byte, string?>? meaning = null)
    {
        if (!Take(name, 1, out int at)) return 0;
        byte v = _blob[at];
        Add(name, at, 1, Number(v, 2), meaning?.Invoke(v));
        return v;
    }

    public ushort U16(string name, Func<ushort, string?>? meaning = null)
    {
        if (!Take(name, 2, out int at)) return 0;
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_blob.AsSpan(at));
        Add(name, at, 2, Number(v, 4), meaning?.Invoke(v));
        return v;
    }

    public uint U24(string name, Func<uint, string?>? meaning = null)
    {
        if (!Take(name, 3, out int at)) return 0;
        uint v = (uint)(_blob[at] | _blob[at + 1] << 8 | _blob[at + 2] << 16);
        Add(name, at, 3, Number(v, 6), meaning?.Invoke(v));
        return v;
    }

    public uint U32(string name, Func<uint, string?>? meaning = null)
    {
        if (!Take(name, 4, out int at)) return 0;
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_blob.AsSpan(at));
        Add(name, at, 4, Number(v, 8), meaning?.Invoke(v));
        return v;
    }

    /// <summary>A binary-coded-decimal version (bcdUSB, bcdDevice, bcdHID…), shown as the version it spells.</summary>
    public UsbBcd Bcd(string name)
    {
        if (!Take(name, 2, out int at)) return default;
        var v = new UsbBcd(BinaryPrimitives.ReadUInt16LittleEndian(_blob.AsSpan(at)));
        Add(name, at, 2, $"0x{v.Raw:X4}", v.ToString());
        return v;
    }

    /// <summary>A string descriptor index, with the string the device answered for it.</summary>
    public byte StringIndex(string name, UsbDescriptorScope scope)
    {
        var context = scope.Context;
        return U8(name, context.StringMeaning);
    }

    public Guid Uuid(string name, Func<Guid, string?>? meaning = null)
    {
        if (!Take(name, 16, out int at)) return Guid.Empty;
        var v = new Guid(_blob.AsSpan(at, 16));
        Add(name, at, 16, v.ToString("B"), meaning?.Invoke(v));
        return v;
    }

    /// <summary>A run of bytes the standard defines as a unit (a bitmap wider than four bytes, a filter list).</summary>
    public void Raw(string name, int count, string? meaning = null)
    {
        if (count <= 0 || !Take(name, count, out int at)) return;
        Add(name, at, count, Hex(_blob.AsSpan(at, count)), meaning);
    }

    /// <summary>Whatever the decoder did not read, as one field — nothing when it read every byte.</summary>
    public void Rest(string name, string? meaning = null)
    {
        int count = Remaining;
        if (count <= 0) return;
        int at = _start + _cursor;
        Add(name, at, count, Hex(_blob.AsSpan(at, count)), meaning);
        _cursor = Length;
    }

    bool Take(string name, int size, out int at)
    {
        at = _start + _cursor;
        if (_truncated) return false;
        if (_cursor + size <= Length)
        {
            _cursor += size;
            return true;
        }
        int left = Length - _cursor;
        if (left > 0) Add(name, at, left, Hex(_blob.AsSpan(at, left)), $"truncated: {size} byte(s) expected, {left} present");
        _truncated = true;
        _cursor = Length;
        return false;
    }

    void Add(string name, int offset, int size, string value, string? meaning) =>
        _fields.Add(new UsbDescriptorField(name, offset, size, value, meaning));

    /// <summary>Hex at the field's width, with the decimal beside it once the two differ.</summary>
    public static string Number(ulong value, int digits) =>
        value < 10 ? $"0x{value.ToString($"X{digits}")}" : $"0x{value.ToString($"X{digits}")} ({value})";

    static string Hex(ReadOnlySpan<byte> data) => UsbHex.Format(data);
}
