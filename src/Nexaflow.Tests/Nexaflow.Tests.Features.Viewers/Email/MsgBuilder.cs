using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nexaflow.IO.CompoundFile;

namespace Nexaflow.Tests.Features.Email;

/// <summary>
/// Writes <c>.msg</c> files as MS-OXMSG lays them out — a property stream with fixed-size values, a <c>__substg1.0_</c>
/// stream for each variable-size one, a storage for each recipient and each attachment, an embedded message as an object
/// storage — so the reader is tested against the format rather than against what any one client happens to write.
/// </summary>
internal sealed class MsgBuilder
{
    // MAPI property types.
    private const ushort Integer32 = 0x0003;
    private const ushort BooleanType = 0x000B;
    private const ushort Time64 = 0x0040;
    private const ushort Narrow = 0x001E;
    private const ushort Wide = 0x001F;
    private const ushort Bytes = 0x0102;

    private readonly List<(uint Tag, ulong Value)> _fixed = [];
    private readonly List<(string Name, byte[] Bytes)> _streams = [];
    private readonly List<MsgBuilder> _recipients = [];
    private readonly List<MsgBuilder> _attachments = [];
    private MsgBuilder? _embedded;
    private CompoundFolder? _object;

    public MsgBuilder String(ushort id, string value) => Variable(id, Wide, Encoding.Unicode.GetBytes(value + "\0"));

    public MsgBuilder Narrow8(ushort id, byte[] bytes) => Variable(id, Narrow, [.. bytes, 0]);

    public MsgBuilder Binary(ushort id, byte[] value) => Variable(id, Bytes, value);

    public MsgBuilder Integer(ushort id, int value)
    {
        _fixed.Add(((uint)(id << 16) | Integer32, (uint)value));
        return this;
    }

    public MsgBuilder Flag(ushort id, bool value)
    {
        _fixed.Add(((uint)(id << 16) | BooleanType, value ? 1ul : 0ul));
        return this;
    }

    public MsgBuilder Time(ushort id, DateTime utc)
    {
        _fixed.Add(((uint)(id << 16) | Time64, (ulong)utc.ToFileTimeUtc()));
        return this;
    }

    /// <summary>A recipient: 1 To, 2 Cc, 3 Bcc.</summary>
    public MsgBuilder Recipient(int kind, string name, string address, string type = "SMTP")
    {
        _recipients.Add(new MsgBuilder().Integer(0x0C15, kind).String(0x3001, name).String(0x3002, type).String(0x3003, address));
        return this;
    }

    public MsgBuilder Attachment(MsgBuilder attachment)
    {
        _attachments.Add(attachment);
        return this;
    }

    /// <summary>On an attachment: the message it holds, embedded (PR_ATTACH_METHOD 5).</summary>
    public MsgBuilder Embedding(MsgBuilder message)
    {
        _embedded = message;
        return Integer(0x3705, 5);
    }

    /// <summary>On an attachment: the OLE object it holds (PR_ATTACH_METHOD 6).</summary>
    public MsgBuilder Ole(CompoundFolder storage)
    {
        _object = storage;
        return Integer(0x3705, 6);
    }

    /// <summary>The message as a <c>.msg</c> file, with a named-property map at its root as Outlook keeps one.</summary>
    public byte[] Build()
    {
        var root = Folder("Root Entry", headerSize: 32);

        var named = new CompoundFolder("__nameid_version1.0");
        named.Add("__substg1.0_00020102", new byte[16]);
        named.Add("__substg1.0_00030102", new byte[8]);
        named.Add("__substg1.0_00040102", Encoding.Unicode.GetBytes("Keywords"));
        root.Add(named);

        return CompoundFileWriter.Write(root);
    }

    private CompoundFolder Folder(string name, int headerSize)
    {
        var folder = new CompoundFolder(name);

        var properties = new byte[headerSize + (_fixed.Count * 16)];
        BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(8), (uint)_recipients.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(12), (uint)_attachments.Count);

        if (headerSize >= 24)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(16), (uint)_recipients.Count);
            BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(20), (uint)_attachments.Count);
        }

        for (var at = 0; at < _fixed.Count; at++)
        {
            var entry = properties.AsSpan(headerSize + (at * 16));
            BinaryPrimitives.WriteUInt32LittleEndian(entry, _fixed[at].Tag);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], 6);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], _fixed[at].Value);
        }

        folder.Add("__properties_version1.0", properties);
        foreach (var (stream, bytes) in _streams) folder.Add(stream, bytes);

        for (var at = 0; at < _recipients.Count; at++) folder.Add(_recipients[at].Folder($"__recip_version1.0_#{at:X8}", 8));
        for (var at = 0; at < _attachments.Count; at++) folder.Add(_attachments[at].Folder($"__attach_version1.0_#{at:X8}", 8));

        if (_embedded is not null) folder.Add(_embedded.Folder("__substg1.0_3701000D", 24));

        if (_object is not null)
        {
            var copy = new CompoundFolder("__substg1.0_3701000D");
            foreach (var (stream, bytes) in _object.Streams) copy.Add(stream, bytes);
            foreach (var inner in _object.Folders) copy.Add(inner);
            folder.Add(copy);
        }

        return folder;
    }

    private MsgBuilder Variable(ushort id, ushort type, byte[] bytes)
    {
        _fixed.Add(((uint)(id << 16) | type, (uint)bytes.Length));
        _streams.Add(($"__substg1.0_{id:X4}{type:X4}", bytes));
        return this;
    }

    // ── Compressed RTF (MS-OXRTFCP) ─────────────────────────────────────────

    /// <summary>RTF stored uncompressed, as the format allows: its header, then the RTF as it is.</summary>
    public static byte[] Stored(string rtf)
    {
        var raw = Encoding.Latin1.GetBytes(rtf);
        var bytes = new byte[16 + raw.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)(raw.Length + 12));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)raw.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0x414C454D);
        raw.CopyTo(bytes, 16);

        return bytes;
    }

    /// <summary>
    /// RTF compressed (LZFu) — every byte a literal, eight to each control byte, and an end-of-stream reference after the
    /// last — with the CRC the format checks over everything after the header.
    /// </summary>
    public static byte[] Compressed(string rtf, bool corrupt = false)
    {
        var raw = Encoding.Latin1.GetBytes(rtf);
        using var body = new MemoryStream();

        // The dictionary starts with 207 bytes of RTF prologue already written, so the next write is at 207.
        var end = (207 + raw.Length) % 4096;

        for (var at = 0; at <= raw.Length; at += 8)
        {
            var take = Math.Min(8, raw.Length - at);
            var control = take < 8 ? (byte)(1 << take) : (byte)0;

            body.WriteByte(control);
            body.Write(raw, at, take);

            if (take < 8)
            {
                // A reference to where the next byte would be written ends the stream.
                body.WriteByte((byte)(end >> 4));
                body.WriteByte((byte)((end & 0x0F) << 4));
                break;
            }

            if (at + 8 == raw.Length)
            {
                body.WriteByte(1);
                body.WriteByte((byte)(end >> 4));
                body.WriteByte((byte)((end & 0x0F) << 4));
                break;
            }
        }

        var compressed = body.ToArray();
        var bytes = new byte[16 + compressed.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)(compressed.Length + 12));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)raw.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0x75465A4C);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), Crc(compressed) ^ (corrupt ? 1u : 0u));
        compressed.CopyTo(bytes, 16);

        return bytes;
    }

    /// <summary>The format's CRC-32: the usual table, started at nought and never inverted.</summary>
    private static uint Crc(byte[] bytes)
    {
        var crc = 0u;

        foreach (var b in bytes)
        {
            var value = (crc ^ b) & 0xFF;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            crc = value ^ (crc >> 8);
        }

        return crc;
    }
}
