using Nexaflow.IO.Usb.Naming;

namespace Nexaflow.IO.Usb.Descriptors.Layout;

/// <summary>One field of one descriptor: where its bytes sit in the blob, what they hold, and what that means.</summary>
/// <param name="Offset">From the start of the blob, not of the descriptor — so a view can highlight it directly.</param>
/// <param name="Value">The raw value as written: hex, with the decimal beside it where that reads better.</param>
/// <param name="Meaning">What the value says, when the standard or the device gives it a name.</param>
public sealed record UsbDescriptorField(string Name, int Offset, int Size, string Value, string? Meaning = null)
{
    public bool Contains(int offset) => offset >= Offset && offset < Offset + Size;
}

/// <summary>
/// One descriptor in a blob, with its place in the tree the blob describes: a configuration's interfaces sit
/// under it (or under the association that groups them), endpoints under their interface, class-specific
/// descriptors under whatever they follow.
/// </summary>
/// <param name="Parent">The <see cref="Index"/> of the entry this one belongs to; null for the blob's first.</param>
public sealed record UsbDescriptorEntry(
    int Index, int? Parent, int Offset, int Length, byte Type, string Kind, string Summary, IReadOnlyList<UsbDescriptorField> Fields)
{
    public bool Contains(int offset) => offset >= Offset && offset < Offset + Length;

    public UsbDescriptorField? FieldAt(int offset)
    {
        foreach (var field in Fields)
            if (field.Contains(offset)) return field;
        return null;
    }
}

/// <summary>Which request returned a blob.</summary>
public enum UsbDescriptorBlobKind { Device, Configuration, Bos }

/// <summary>
/// A descriptor blob as the device returned it, every byte accounted to a descriptor and every descriptor to its
/// fields. A malformed tail is kept as bytes and named in <see cref="Problems"/> rather than dropped.
/// </summary>
public sealed record UsbDescriptorMap(
    UsbDescriptorBlobKind Kind, string Title, byte[] Bytes, IReadOnlyList<UsbDescriptorEntry> Entries, IReadOnlyList<string> Problems)
{
    /// <summary>The descriptor holding a byte. Entries never overlap and run in blob order, so this is a binary search.</summary>
    public UsbDescriptorEntry? EntryAt(int offset)
    {
        int lo = 0, hi = Entries.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            var entry = Entries[mid];
            if (offset < entry.Offset) hi = mid - 1;
            else if (offset >= entry.Offset + entry.Length) lo = mid + 1;
            else return entry;
        }
        return null;
    }

    public UsbDescriptorField? FieldAt(int offset) => EntryAt(offset)?.FieldAt(offset);

    public IEnumerable<UsbDescriptorEntry> ChildrenOf(int? parent) => Entries.Where(e => e.Parent == parent);
}

/// <summary>
/// What a decoder may consult beyond the bytes: the speed (bMaxPower and bInterval scale with it), the device's
/// strings for its string indices, and usb.ids for vendor, product and class names.
/// </summary>
public sealed record UsbDescriptorContext(UsbSpeed Speed, Func<byte, string?>? StringOf, UsbIdsDatabase? Names)
{
    public static readonly UsbDescriptorContext Bare = new(UsbSpeed.Unknown, null, null);

    /// <summary>"Product" for an index the device answered, "none" for zero, null when unread.</summary>
    public string? StringMeaning(byte index) => index == 0 ? "none" : StringOf?.Invoke(index) is { } text ? $"\"{text}\"" : null;
}

/// <summary>The interface (alternate setting) a descriptor follows in a configuration.</summary>
public readonly record struct UsbInterfaceScope(byte Number, byte Alternate, byte Class, byte SubClass, byte Protocol);

/// <summary>Where in its blob a descriptor was met: what a class-specific descriptor means depends on it.</summary>
public readonly record struct UsbDescriptorScope(
    UsbDescriptorBlobKind Blob, UsbDescriptorContext Context, UsbInterfaceScope? Interface, bool AfterEndpoint);

/// <summary>What a decoder calls a descriptor it recognised: its kind, and one line for a tree.</summary>
public sealed record UsbDescriptorHeading(string Kind, string Summary);

/// <summary>
/// Decodes the descriptors it knows into fields. Decoders are tried in order and the first to answer wins, so a
/// class decoder listed ahead of the standard one claims its class-specific descriptors, and a decoder for a
/// new class — or one device's vendor descriptors — is one more entry in the list rather than an edit here.
/// </summary>
public interface IUsbDescriptorDecoder
{
    /// <summary>
    /// Writes the descriptor's fields and names it, or returns null without writing when it is not one this
    /// decoder knows in this scope.
    /// </summary>
    UsbDescriptorHeading? TryDecode(UsbFieldWriter fields, UsbDescriptorScope scope);
}

/// <summary>Builds <see cref="UsbDescriptorMap"/>s: the walk and the parent links, with the decoding left to the decoders.</summary>
public static class UsbDescriptorMaps
{
    /// <summary>The decoders Nexaflow ships, class-specific first.</summary>
    public static readonly IReadOnlyList<IUsbDescriptorDecoder> Default =
    [
        new HidDescriptorDecoder(),
        new CdcDescriptorDecoder(),
        new AudioDescriptorDecoder(),
        new VideoDescriptorDecoder(),
        new DfuDescriptorDecoder(),
        new StandardDescriptorDecoder(),
    ];

    public static UsbDescriptorMap Device(byte[] bytes, UsbDescriptorContext context, IReadOnlyList<IUsbDescriptorDecoder>? decoders = null) =>
        Walk(UsbDescriptorBlobKind.Device, "Device descriptor", bytes, context, decoders ?? Default);

    public static UsbDescriptorMap Configuration(byte[] bytes, UsbDescriptorContext context, IReadOnlyList<IUsbDescriptorDecoder>? decoders = null) =>
        Walk(UsbDescriptorBlobKind.Configuration, bytes.Length >= 6 ? $"Configuration {bytes[5]}" : "Configuration", bytes, context, decoders ?? Default);

    public static UsbDescriptorMap Bos(byte[] bytes, UsbDescriptorContext context, IReadOnlyList<IUsbDescriptorDecoder>? decoders = null) =>
        Walk(UsbDescriptorBlobKind.Bos, "BOS", bytes, context, decoders ?? Default);

    static UsbDescriptorMap Walk(UsbDescriptorBlobKind kind, string title, byte[] bytes, UsbDescriptorContext context, IReadOnlyList<IUsbDescriptorDecoder> decoders)
    {
        var entries = new List<UsbDescriptorEntry>();
        var problems = new List<string>();
        var tree = new TreeState();
        UsbInterfaceScope? currentInterface = null;
        bool afterEndpoint = false;

        int offset = 0;
        while (offset < bytes.Length)
        {
            int remaining = bytes.Length - offset;
            int length = bytes[offset];
            if (remaining < 2 || length < 2 || length > remaining)
            {
                problems.Add(remaining < 2
                    ? $"one stray byte at offset {offset}"
                    : $"descriptor at offset {offset} claims bLength {length} with {remaining} byte(s) left; the rest is shown as bytes");
                entries.Add(Leftover(entries.Count, tree.Root, offset, remaining, bytes));
                break;
            }

            byte type = bytes[offset + 1];
            var scope = new UsbDescriptorScope(kind, context, currentInterface, afterEndpoint);
            var writer = new UsbFieldWriter(bytes, offset, length);
            var heading = Decode(writer, scope, decoders);

            int? parent = tree.ParentFor(kind, type, bytes.AsSpan(offset, length));
            entries.Add(new UsbDescriptorEntry(entries.Count, parent, offset, length, type, heading.Kind, heading.Summary, writer.Fields));
            tree.Placed(kind, type, bytes.AsSpan(offset, length), entries.Count - 1);

            if (kind == UsbDescriptorBlobKind.Configuration)
            {
                if (type == UsbDescriptorType.Interface && length >= 9)
                {
                    currentInterface = new UsbInterfaceScope(bytes[offset + 2], bytes[offset + 3], bytes[offset + 5], bytes[offset + 6], bytes[offset + 7]);
                    afterEndpoint = false;
                }
                else if (type == UsbDescriptorType.Endpoint) afterEndpoint = true;
            }
            offset += length;
        }

        if (kind != UsbDescriptorBlobKind.Device && bytes.Length >= 4)
        {
            int declared = bytes[2] | bytes[3] << 8;
            if (declared != bytes.Length) problems.Add($"wTotalLength says {declared} bytes; {bytes.Length} were returned");
        }
        return new UsbDescriptorMap(kind, title, bytes, entries, problems);
    }

    static UsbDescriptorHeading Decode(UsbFieldWriter writer, UsbDescriptorScope scope, IReadOnlyList<IUsbDescriptorDecoder> decoders)
    {
        foreach (var decoder in decoders)
        {
            if (decoder.TryDecode(writer, scope) is { } heading)
            {
                writer.Rest("Unparsed", "bytes past the fields this descriptor's standard defines");
                return heading;
            }
            writer.Reset();
        }
        // Nothing claimed it: the header every descriptor shares, the subtype class-specific ones add, the rest as bytes.
        writer.U8("bLength");
        writer.U8("bDescriptorType", UsbDescriptorNames.Type);
        bool classSpecific = writer.Type is UsbDescriptorType.ClassSpecificInterface or UsbDescriptorType.ClassSpecificEndpoint;
        if (classSpecific) writer.U8("bDescriptorSubtype");
        writer.Rest("Data");
        string kind = UsbDescriptorNames.Type(writer.Type) ?? $"Descriptor 0x{writer.Type:X2}";
        return new UsbDescriptorHeading(kind, classSpecific ? $"subtype 0x{writer.Subtype:X2}, {writer.Length} bytes" : $"{writer.Length} bytes");
    }

    static UsbDescriptorEntry Leftover(int index, int? parent, int offset, int count, byte[] bytes) =>
        new(index, parent, offset, count, 0, "Malformed", $"{count} byte(s) that do not form a descriptor",
            [new UsbDescriptorField("Bytes", offset, count, UsbHex.Format(bytes.AsSpan(offset, count)))]);

    /// <summary>Tracks the last entry of each level, so each descriptor finds the one it belongs under.</summary>
    sealed class TreeState
    {
        public int? Root;
        int? _association;
        (byte First, byte Count) _associationSpan;
        int? _interface;
        int? _endpoint;

        public int? ParentFor(UsbDescriptorBlobKind kind, byte type, ReadOnlySpan<byte> d)
        {
            if (Root is null) return null;
            if (kind != UsbDescriptorBlobKind.Configuration) return Root;
            return type switch
            {
                UsbDescriptorType.InterfaceAssociation => Root,
                UsbDescriptorType.Interface when d.Length >= 3 && _association is not null
                    && d[2] >= _associationSpan.First && d[2] < _associationSpan.First + _associationSpan.Count => _association,
                UsbDescriptorType.Interface => Root,
                UsbDescriptorType.Endpoint => _interface ?? Root,
                UsbDescriptorType.ClassSpecificEndpoint or UsbDescriptorType.SuperSpeedEndpointCompanion
                    or UsbDescriptorType.SuperSpeedPlusIsochronousCompanion => _endpoint ?? _interface ?? Root,
                _ => _interface ?? Root,
            };
        }

        public void Placed(UsbDescriptorBlobKind kind, byte type, ReadOnlySpan<byte> d, int index)
        {
            if (Root is null) { Root = index; return; }
            if (kind != UsbDescriptorBlobKind.Configuration) return;
            switch (type)
            {
                case UsbDescriptorType.InterfaceAssociation when d.Length >= 4:
                    _association = index;
                    _associationSpan = (d[2], d[3]);
                    break;
                case UsbDescriptorType.Interface:
                    _interface = index;
                    _endpoint = null;
                    break;
                case UsbDescriptorType.Endpoint:
                    _endpoint = index;
                    break;
            }
        }
    }
}
