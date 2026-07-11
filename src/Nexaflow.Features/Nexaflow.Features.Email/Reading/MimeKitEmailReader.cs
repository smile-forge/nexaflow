using System;
using System.Collections.Generic;
using System.IO;
using MimeKit;
using Nexaflow.Features.Email.Model;

namespace Nexaflow.Features.Email.Reading;

/// <summary>Reads RFC-822 <c>.eml</c> messages with MimeKit.</summary>
internal sealed class MimeKitEmailReader : IEmailFormatReader
{
    public EmailDocument Read(Stream stream, string fileName)
    {
        // Non-persistent load: MimeKit copies part content into memory, so the message (and the bytes we
        // decode below) outlive the incoming stream and are safe to cache.
        var message = MimeMessage.Load(stream);

        var headers = new List<EmailHeader>(message.Headers.Count);
        foreach (var h in message.Headers)
            headers.Add(new EmailHeader(h.Field, h.Value));

        var attachments = new List<EmailAttachment>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        Walk(message.Body, attachments, used, ref index);

        return new EmailDocument
        {
            Headers  = headers,
            From     = NullIfEmpty(message.From.ToString()),
            To       = NullIfEmpty(message.To.ToString()),
            Cc       = NullIfEmpty(message.Cc.ToString()),
            Subject  = NullIfEmpty(message.Subject),
            Date     = message.Date == DateTimeOffset.MinValue ? null : message.Date,
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
            Attachments = attachments,
        };
    }

    /// <summary>Recurses the MIME tree, collecting non-body leaf parts. Deliberately does <b>not</b> descend
    /// into a <see cref="MessagePart"/> (an embedded <c>message/rfc822</c>) — it stays one opaque <c>.eml</c>
    /// attachment rather than flattening a forwarded message's own parts into this one.</summary>
    private static void Walk(MimeEntity? entity, List<EmailAttachment> outp, ISet<string> used, ref int index)
    {
        switch (entity)
        {
            case Multipart multipart:
                foreach (var child in multipart) Walk(child, outp, used, ref index);
                return;

            case MessagePart rfc822:
            {
                using var ms = new MemoryStream();
                rfc822.Message?.WriteTo(ms);
                var subject = rfc822.Message?.Subject;
                var display = rfc822.ContentDisposition?.FileName
                              ?? rfc822.ContentType?.Name
                              ?? $"{Safe(subject, "message")}.eml";
                outp.Add(Build(display, "message/rfc822", ms.ToArray(), inline: false, contentId: null, used, ref index));
                return;
            }

            case MimePart part:
            {
                bool isAttachment = part.IsAttachment;
                bool hasCid       = !string.IsNullOrEmpty(part.ContentId);
                bool inlineDisp   = string.Equals(part.ContentDisposition?.Disposition,
                                                   ContentDisposition.Inline, StringComparison.OrdinalIgnoreCase);

                // A plain text/html leaf with no attachment/inline markers is the message body — not a file.
                if (!isAttachment && part is TextPart && !hasCid && !inlineDisp) return;

                bool inline = !isAttachment && (hasCid || inlineDisp);

                using var ms = new MemoryStream();
                part.Content?.DecodeTo(ms);

                var mime = part.ContentType?.MimeType ?? "application/octet-stream";
                var display = part.FileName ?? $"part{index + 1}{ExtensionFor(mime)}";
                outp.Add(Build(display, mime, ms.ToArray(), inline, part.ContentId, used, ref index));
                return;
            }
        }
    }

    private static EmailAttachment Build(string display, string contentType, byte[] bytes,
                                         bool inline, string? contentId, ISet<string> used, ref int index)
    {
        index++;
        return new EmailAttachment
        {
            EntryName   = EmailEntryNaming.SafeUnique(display, used),
            DisplayName = display,
            ContentType = contentType,
            Content     = bytes,
            IsInline    = inline,
            ContentId   = contentId,
        };
    }

    private static string ExtensionFor(string mimeType) => EmailMimeTypes.Extension(mimeType);

    private static string Safe(string? s, string fallback)
        => string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
