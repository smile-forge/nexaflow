using System;
using System.Linq;
using Nexaflow.IO.CompoundFile;

namespace Nexaflow.Features.Email.Reading.Msg;

/// <summary>
/// A message embedded in another as an attachment, made into a <c>.msg</c> file of its own (MS-OXMSG 2.2.2.1) — so it opens
/// as a message wherever the attachment is taken, exactly as a message saved from Outlook would.
///
/// <para>
/// Two things set an embedded message apart from a file of its own, and both are put right: its property stream's header is
/// the eight bytes shorter one an embedded message has, and the names of its named properties are kept once, by the message
/// holding it, so they are copied in.
/// </para>
/// </summary>
internal static class MsgStandalone
{
    private const string Properties = "__properties_version1.0";
    private const string NamedProperties = "__nameid_version1.0";

    /// <summary>The message <paramref name="embedded"/> holds, as a <c>.msg</c> file; <paramref name="root"/> is the file it is embedded in.</summary>
    public static byte[] Of(CompoundStorage embedded, CompoundStorage root)
    {
        var message = embedded.Copied();

        if (message.Streams.FirstOrDefault(stream => Named(stream.Key, Properties)).Value is { } properties)
            message.Add(Properties, TopLevel(properties));

        if (!message.Folders.Any(folder => Named(folder.Name, NamedProperties)) && root.Storage(NamedProperties) is { } named)
            message.Add(named.Copied());

        return CompoundFileWriter.Write(message);
    }

    /// <summary>
    /// An embedded message's property stream with a file's header: the same counts, then the eight reserved bytes a file's
    /// header has after them.
    /// </summary>
    private static byte[] TopLevel(byte[] properties)
    {
        var embeddedHeader = (int)MsgPropertyOwner.Embedded;
        if (properties.Length < embeddedHeader) return properties;

        var file = new byte[properties.Length + ((int)MsgPropertyOwner.Message - embeddedHeader)];

        properties.AsSpan(0, embeddedHeader).CopyTo(file);
        properties.AsSpan(embeddedHeader).CopyTo(file.AsSpan((int)MsgPropertyOwner.Message));

        return file;
    }

    private static bool Named(string name, string wanted) => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase);
}
