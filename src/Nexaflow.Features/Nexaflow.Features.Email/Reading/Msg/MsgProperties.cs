using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Nexaflow.IO.CompoundFile;
using Nexaflow.Markdown.Rtf;

namespace Nexaflow.Features.Email.Reading.Msg;

/// <summary>What a property stream heads, which decides how long its header is (MS-OXMSG 2.4).</summary>
internal enum MsgPropertyOwner
{
    /// <summary>The message a <c>.msg</c> file is: a 32-byte header.</summary>
    Message = 32,

    /// <summary>A message embedded as an attachment: a 24-byte header.</summary>
    Embedded = 24,

    /// <summary>A recipient or an attachment: an 8-byte header.</summary>
    Part = 8,
}

/// <summary>
/// The MAPI properties of one storage of a <c>.msg</c> file (MS-OXMSG): the fixed-size ones out of its
/// <c>__properties_version1.0</c> stream, and the variable-size ones out of the <c>__substg1.0_IIIITTTT</c> stream or
/// storage each is kept in.
///
/// <para>
/// <strong>A string is read whichever way it was written.</strong> A Unicode property is UTF-16; an eight-bit one is in the
/// message's code page, which a recipient's and an attachment's strings share with the message holding them — so the code
/// page is the message's, handed down.
/// </para>
/// </summary>
internal sealed class MsgProperties
{
    private const string PropertyStream = "__properties_version1.0";

    // MAPI property types (MS-OXCDATA 2.11.1).
    private const ushort Integer32Type = 0x0003;
    private const ushort BooleanType = 0x000B;
    private const ushort Integer64Type = 0x0014;
    private const ushort SystemTimeType = 0x0040;
    private const ushort ObjectType = 0x000D;
    private const ushort String8Type = 0x001E;
    private const ushort UnicodeType = 0x001F;
    private const ushort BinaryType = 0x0102;

    private readonly Dictionary<ushort, (ushort Type, ulong Value)> _fixed;

    private MsgProperties(CompoundStorage storage, Dictionary<ushort, (ushort Type, ulong Value)> @fixed, int codePage)
    {
        Storage = storage;
        _fixed = @fixed;
        CodePage = codePage;
    }

    /// <summary>The storage the properties are kept in.</summary>
    public CompoundStorage Storage { get; }

    /// <summary>The code page an eight-bit string is read in.</summary>
    public int CodePage { get; }

    /// <summary>
    /// The properties of <paramref name="storage"/>, whose property stream heads <paramref name="owner"/> — read in the code
    /// page it names (a message's), or <paramref name="codePage"/> where it names none (a recipient's or an attachment's,
    /// which is the message's).
    /// </summary>
    public static MsgProperties Of(CompoundStorage storage, MsgPropertyOwner owner, int? codePage = null)
    {
        var @fixed = new Dictionary<ushort, (ushort, ulong)>();

        if (storage.Stream(PropertyStream)?.Read() is { } bytes)
        {
            for (var at = (int)owner; at + 16 <= bytes.Length; at += 16)
            {
                var tag = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
                @fixed[(ushort)(tag >> 16)] = ((ushort)tag, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at + 8)));
            }
        }

        var read = new MsgProperties(storage, @fixed, codePage ?? RtfCodePages.Western);

        return codePage is null && read.Integer(MsgTags.MessageCodePage) is > 0 and var page
            ? new MsgProperties(storage, @fixed, page)
            : read;
    }

    /// <summary>A string property, Unicode or eight-bit, without the terminator it is stored with — or null where it is not set.</summary>
    public string? String(ushort id)
    {
        if (Bytes(id, UnicodeType) is { } wide) return Trimmed(Encoding.Unicode.GetString(wide));
        if (Bytes(id, String8Type) is { } narrow) return Trimmed(RtfCodePages.Decode(narrow, CodePage, symbol: false));

        return null;
    }

    /// <summary>A binary property, or null where it is not set.</summary>
    public byte[]? Binary(ushort id) => Bytes(id, BinaryType);

    /// <summary>A string property written in <paramref name="codePage"/> where it is eight-bit — what HTML's own code page says it is.</summary>
    public string? Text(ushort id, int codePage)
    {
        if (Bytes(id, UnicodeType) is { } wide) return Trimmed(Encoding.Unicode.GetString(wide));
        if (Bytes(id, String8Type) is { } narrow) return Trimmed(RtfCodePages.Decode(narrow, codePage, symbol: false));
        if (Bytes(id, BinaryType) is { } binary) return Trimmed(RtfCodePages.Decode(binary, codePage, symbol: false));

        return null;
    }

    /// <summary>A 32-bit integer property, or null.</summary>
    public int? Integer(ushort id) =>
        _fixed.TryGetValue(id, out var value) && value.Type == Integer32Type ? (int)(uint)value.Value : null;

    /// <summary>A boolean property, or null.</summary>
    public bool? Flag(ushort id) =>
        _fixed.TryGetValue(id, out var value) && value.Type == BooleanType ? (value.Value & 0xFF) != 0 : null;

    /// <summary>A time property, which MAPI keeps as a UTC <c>FILETIME</c> — or null where it is not set or not a time.</summary>
    public DateTimeOffset? Time(ushort id)
    {
        if (!_fixed.TryGetValue(id, out var value) || value.Type is not (SystemTimeType or Integer64Type)) return null;

        var ticks = (long)value.Value;
        if (ticks <= 0 || ticks > DateTime.MaxValue.ToFileTimeUtc()) return null;

        return new DateTimeOffset(DateTime.FromFileTimeUtc(ticks), TimeSpan.Zero);
    }

    /// <summary>An object property — an embedded message or an OLE object — as the storage it is kept in, or null.</summary>
    public CompoundStorage? Embedded(ushort id) => Storage.Storage(Named(id, ObjectType));

    /// <summary>The storages under this one whose names start <paramref name="prefix"/>, in the order they are numbered.</summary>
    public IEnumerable<CompoundStorage> Numbered(string prefix)
    {
        var found = new List<CompoundStorage>();
        foreach (var storage in Storage.Storages)
            if (storage.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) found.Add(storage);

        found.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

        return found;
    }

    private byte[]? Bytes(ushort id, ushort type) => Storage.Stream(Named(id, type))?.Read();

    private static string Named(ushort id, ushort type) => $"__substg1.0_{id:X4}{type:X4}";

    /// <summary>A string as stored, less the terminator and anything a writer left after it.</summary>
    private static string Trimmed(string text)
    {
        var end = text.IndexOf('\0');
        return end < 0 ? text : text[..end];
    }
}

/// <summary>The MAPI properties a message is read for (MS-OXPROPS), by id.</summary>
internal static class MsgTags
{
    public const ushort MessageClass = 0x001A;
    public const ushort Subject = 0x0037;
    public const ushort ClientSubmitTime = 0x0039;
    public const ushort SentRepresentingName = 0x0042;
    public const ushort SentRepresentingAddressType = 0x0064;
    public const ushort SentRepresentingEmailAddress = 0x0065;
    public const ushort TransportMessageHeaders = 0x007D;
    public const ushort RecipientType = 0x0C15;
    public const ushort SenderName = 0x0C1A;
    public const ushort SenderAddressType = 0x0C1E;
    public const ushort SenderEmailAddress = 0x0C1F;
    public const ushort MessageDeliveryTime = 0x0E06;
    public const ushort Body = 0x1000;
    public const ushort RtfCompressed = 0x1009;
    public const ushort Html = 0x1013;
    public const ushort DisplayName = 0x3001;
    public const ushort AddressType = 0x3002;
    public const ushort EmailAddress = 0x3003;
    public const ushort AttachData = 0x3701;
    public const ushort AttachExtension = 0x3703;
    public const ushort AttachFilename = 0x3704;
    public const ushort AttachMethod = 0x3705;
    public const ushort AttachLongFilename = 0x3707;
    public const ushort AttachMimeTag = 0x370E;
    public const ushort AttachContentId = 0x3712;
    public const ushort AttachFlags = 0x3714;
    public const ushort SmtpAddress = 0x39FE;
    public const ushort InternetCodePage = 0x3FDE;
    public const ushort MessageCodePage = 0x3FFD;
    public const ushort SenderSmtpAddress = 0x5D01;
    public const ushort SentRepresentingSmtpAddress = 0x5D02;
    public const ushort AttachmentHidden = 0x7FFE;
}
