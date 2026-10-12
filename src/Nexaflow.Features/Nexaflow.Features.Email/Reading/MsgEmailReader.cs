using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MimeKit;
using MimeKit.IO;
using MimeKit.Tnef;
using Nexaflow.Features.Email.Model;
using Nexaflow.Features.Email.Reading.Msg;
using Nexaflow.IO.CompoundFile;
using Nexaflow.Markdown.Rtf;

namespace Nexaflow.Features.Email.Reading;

/// <summary>
/// Reads Outlook <c>.msg</c> messages (MS-OXMSG): a compound file whose storages hold the message's MAPI properties, one
/// storage for each recipient and each attachment.
///
/// <para>
/// <strong>The body is whichever the message kept best.</strong> HTML where it kept HTML; otherwise its compressed RTF,
/// decompressed — and where that RTF was made from HTML (MS-OXRTFEX), the HTML it was made from, so it is shown exactly as a
/// message kept as HTML is; otherwise the RTF itself (<see cref="EmailDocument.RtfBody"/>), drawn as RTF. The plain text is
/// always read as well.
/// </para>
/// <para>
/// <strong>A signed message is its signed content.</strong> Outlook keeps an S/MIME signed message as one attachment
/// holding the whole MIME entity, so its body and its attachments are read out of that, as a <c>.eml</c>'s would be.
/// </para>
/// </summary>
internal sealed class MsgEmailReader : IEmailFormatReader
{
    // PR_ATTACH_METHOD (MS-OXPROPS 2.581).
    private const int ByEmbeddedMessage = 5;
    private const int ByOleObject = 6;

    /// <summary>PR_ATTACH_FLAGS' ATT_MHTML_REF: the attachment is referred to from the HTML body.</summary>
    private const int ReferredToFromHtml = 0x4;

    private const string SignedClass = "IPM.Note.SMIME.MultipartSigned";

    public EmailDocument Read(Stream stream, string fileName)
    {
        // The compound file is read from the stream it is handed, which stays open — the caller (EmailDocumentReader / the
        // VFS session) owns its lifetime.
        var root = CompoundFile.Open(stream).Root;
        var message = MsgProperties.Of(root, MsgPropertyOwner.Message);

        var headers = Headers(message);
        var (text, html, rtf) = Bodies(message);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var attachments = new List<EmailAttachment>();

        if (Signed(message) is { } signed)
        {
            var (signedText, signedHtml, parts) = MimeKitEmailReader.Contents(signed, used);

            text = signedText ?? text;
            if (signedHtml is not null) (html, rtf) = (signedHtml, null);
            attachments.AddRange(parts);
        }
        else
        {
            Attachments(message, root, html, attachments, used);
        }

        return new EmailDocument
        {
            Headers  = headers,
            From     = From(message, headers),
            To       = Recipients(message, 1),
            Cc       = Recipients(message, 2),
            Subject  = NullIfEmpty(message.String(MsgTags.Subject)),
            Date     = message.Time(MsgTags.ClientSubmitTime) ?? message.Time(MsgTags.MessageDeliveryTime),
            TextBody = text,
            HtmlBody = html,
            RtfBody  = rtf,
            Attachments = attachments,
        };
    }

    // ── Headers and addresses ───────────────────────────────────────────────

    /// <summary>The internet headers the message arrived with — none for one that never crossed the internet.</summary>
    private static List<EmailHeader> Headers(MsgProperties message)
    {
        var headers = new List<EmailHeader>();
        if (message.String(MsgTags.TransportMessageHeaders) is not { Length: > 0 } raw) return headers;

        // A block of headers is ended by a blank line, which the property does not always keep.
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(raw.TrimEnd() + "\r\n\r\n"));

        foreach (var header in HeaderList.Load(bytes)) headers.Add(new EmailHeader(header.Field, header.Value));

        return headers;
    }

    /// <summary>
    /// Who it is from: the mailbox it was sent as, where it was sent on someone's behalf, and the sender's otherwise — by the
    /// internet address wherever one is known, since an Exchange address names a directory entry nobody can write to, and
    /// failing every property the <c>From:</c> header it arrived with.
    /// </summary>
    private static string? From(MsgProperties message, List<EmailHeader> headers)
    {
        var name = NullIfEmpty(message.String(MsgTags.SentRepresentingName)) ?? NullIfEmpty(message.String(MsgTags.SenderName));
        var address = NullIfEmpty(message.String(MsgTags.SentRepresentingSmtpAddress))
                   ?? Internet(message, MsgTags.SentRepresentingAddressType, MsgTags.SentRepresentingEmailAddress)
                   ?? NullIfEmpty(message.String(MsgTags.SenderSmtpAddress))
                   ?? Internet(message, MsgTags.SenderAddressType, MsgTags.SenderEmailAddress)
                   ?? FromHeader(headers);

        return FormatAddress(name, address);
    }

    /// <summary>The recipients of one kind — 1 for To, 2 for Cc — each as its name and internet address.</summary>
    private static string? Recipients(MsgProperties message, int kind)
    {
        var parts = new List<string>();

        foreach (var storage in message.Numbered("__recip_version1.0_"))
        {
            var recipient = MsgProperties.Of(storage, MsgPropertyOwner.Part, message.CodePage);
            if (((recipient.Integer(MsgTags.RecipientType) ?? 0) & 0x0F) != kind) continue;

            var address = NullIfEmpty(recipient.String(MsgTags.SmtpAddress))
                       ?? Internet(recipient, MsgTags.AddressType, MsgTags.EmailAddress);

            if (FormatAddress(recipient.String(MsgTags.DisplayName), address) is { } said) parts.Add(said);
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    /// <summary>An address where it is an internet one — its type says SMTP, or it is written as one.</summary>
    private static string? Internet(MsgProperties properties, ushort type, ushort address)
    {
        var written = NullIfEmpty(properties.String(address));
        if (written is null) return null;

        // An Exchange address is a path into a directory (/o=…/cn=…), which is no address anybody can write to.
        return string.Equals(properties.String(type), "SMTP", StringComparison.OrdinalIgnoreCase) || (written.Contains('@') && !written.StartsWith('/'))
            ? written
            : null;
    }

    private static string? FromHeader(List<EmailHeader> headers)
    {
        foreach (var header in headers)
        {
            if (!string.Equals(header.Field, "From", StringComparison.OrdinalIgnoreCase)) continue;
            if (!InternetAddressList.TryParse(header.Value, out var list)) continue;

            foreach (var mailbox in list.Mailboxes)
                if (NullIfEmpty(mailbox.Address) is { } address) return address;
        }

        return null;
    }

    private static string? FormatAddress(string? name, string? email)
    {
        name = name?.Trim();
        email = email?.Trim();
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(email))
            return string.Equals(name, email, StringComparison.OrdinalIgnoreCase) ? email : $"{name} <{email}>";
        return NullIfEmpty(name) ?? NullIfEmpty(email);
    }

    // ── The body ────────────────────────────────────────────────────────────

    /// <summary>The plain text, and the best the message kept of the rest: its HTML, the HTML its RTF was made from, or its RTF.</summary>
    private static (string? Text, string? Html, string? Rtf) Bodies(MsgProperties message)
    {
        var text = NullIfEmpty(message.String(MsgTags.Body));

        // HTML is written in the code page the message went out in, which is not always the one its strings are in.
        var page = message.Integer(MsgTags.InternetCodePage) is > 0 and var internet ? internet : message.CodePage;
        var html = NullIfEmpty(message.Text(MsgTags.Html, page));

        if (html is not null || message.Binary(MsgTags.RtfCompressed) is not { Length: > 0 } compressed) return (text, html, null);
        if (Decompressed(compressed) is not { } rtf) return (text, null, null);

        return RtfHtmlUnwrapper.Unwrap(rtf) is { } unwrapped ? (text, unwrapped, null) : (text, null, rtf);
    }

    /// <summary>
    /// Compressed RTF (MS-OXRTFCP) as the RTF it holds — its bytes one character to each, as an RTF reader reads them — or
    /// null where it is not compressed RTF or fails its check.
    /// </summary>
    private static string? Decompressed(byte[] compressed)
    {
        var filter = new RtfCompressedToRtf();

        using var filtered = new FilteredStream(new MemoryStream(compressed, writable: false));
        filtered.Add(filter);

        using var rtf = new MemoryStream();
        filtered.CopyTo(rtf);

        // Stored uncompressed it carries no check; compressed, its check has to hold.
        var valid = filter.CompressionMode == RtfCompressionMode.Uncompressed
                 || (filter.CompressionMode == RtfCompressionMode.Compressed && filter.IsValidCrc32);

        return valid ? Encoding.Latin1.GetString(rtf.GetBuffer(), 0, (int)rtf.Length) : null;
    }

    /// <summary>The MIME entity a signed message's one attachment holds, or null for any other message.</summary>
    private static MimeEntity? Signed(MsgProperties message)
    {
        if (message.String(MsgTags.MessageClass)?.StartsWith(SignedClass, StringComparison.OrdinalIgnoreCase) != true) return null;

        foreach (var storage in message.Numbered("__attach_version1.0_"))
        {
            var attachment = MsgProperties.Of(storage, MsgPropertyOwner.Part, message.CodePage);

            if (string.Equals(attachment.String(MsgTags.AttachMimeTag), "multipart/signed", StringComparison.OrdinalIgnoreCase) &&
                attachment.Binary(MsgTags.AttachData) is { Length: > 0 } entity)
                return MimeEntity.Load(new MemoryStream(entity, writable: false));
        }

        return null;
    }

    // ── Attachments ─────────────────────────────────────────────────────────

    /// <summary>
    /// Every attachment, in order: a file as its bytes, an embedded message as a <c>.msg</c> of its own, an OLE object as the
    /// compound file it is, and one that only refers to a file elsewhere as an entry with nothing in it.
    /// </summary>
    private static void Attachments(MsgProperties message, CompoundStorage root, string? html,
                                    List<EmailAttachment> attachments, ISet<string> used)
    {
        var index = 0;

        foreach (var storage in message.Numbered("__attach_version1.0_"))
        {
            index++;

            var attachment = MsgProperties.Of(storage, MsgPropertyOwner.Part, message.CodePage);
            var method = attachment.Integer(MsgTags.AttachMethod);

            if (method == ByEmbeddedMessage && attachment.Embedded(MsgTags.AttachData) is { } embedded)
            {
                var subject = MsgProperties.Of(embedded, MsgPropertyOwner.Embedded, message.CodePage).String(MsgTags.Subject);
                var named = $"{Safe(subject, "message")}.msg";

                attachments.Add(new EmailAttachment
                {
                    EntryName   = EmailEntryNaming.SafeUnique(named, used),
                    DisplayName = named,
                    ContentType = "application/vnd.ms-outlook",
                    Content     = MsgStandalone.Of(embedded, root),
                    IsInline    = false,
                    ContentId   = null,
                });

                continue;
            }

            var bytes = method == ByOleObject && attachment.Embedded(MsgTags.AttachData) is { } ole
                ? CompoundFileWriter.Write(ole.Copied())
                : attachment.Binary(MsgTags.AttachData) ?? [];

            var mime = NullIfEmpty(attachment.String(MsgTags.AttachMimeTag)) ?? "application/octet-stream";
            var display = NullIfEmpty(attachment.String(MsgTags.AttachLongFilename))
                       ?? NullIfEmpty(attachment.String(MsgTags.AttachFilename))
                       ?? NullIfEmpty(attachment.String(MsgTags.DisplayName))
                       ?? $"part{index}{EmailMimeTypes.Extension(mime)}";
            var contentId = NullIfEmpty(attachment.String(MsgTags.AttachContentId)?.Trim().Trim('<', '>'));

            var inline = ((attachment.Integer(MsgTags.AttachFlags) ?? 0) & ReferredToFromHtml) != 0
                      || attachment.Flag(MsgTags.AttachmentHidden) == true
                      || (contentId is not null && html?.Contains("cid:" + contentId, StringComparison.OrdinalIgnoreCase) == true);

            attachments.Add(new EmailAttachment
            {
                EntryName   = EmailEntryNaming.SafeUnique(display, used),
                DisplayName = display,
                ContentType = mime,
                Content     = bytes,
                IsInline    = inline,
                ContentId   = contentId,
            });
        }
    }

    private static string Safe(string? s, string fallback)
        => string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
