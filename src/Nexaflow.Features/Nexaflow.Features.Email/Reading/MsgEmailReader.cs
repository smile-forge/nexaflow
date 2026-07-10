using System;
using System.Collections.Generic;
using System.IO;
using MsgReader.Outlook;
using Nexaflow.Features.Email.Model;

namespace Nexaflow.Features.Email.Reading;

/// <summary>Reads Outlook <c>.msg</c> (OLE/CFBF) messages with MsgReader. MsgReader de-encapsulates an
/// RTF-compressed body into HTML internally, so <see cref="EmailDocument.HtmlBody"/> is populated the same
/// way it is for a MIME message.</summary>
internal sealed class MsgEmailReader : IEmailFormatReader
{
    public EmailDocument Read(Stream stream, string fileName)
    {
        // CFBF needs a seekable stream (VirtualFileSystem.OpenRead always returns one). Leave it open — the
        // caller (EmailDocumentReader / the VFS session) owns its lifetime.
        using var msg = new Storage.Message(stream, FileAccess.Read, leaveStreamOpen: true);

        var headers = new List<EmailHeader>();
        var raw = msg.Headers?.RawHeaders;   // null when the message never crossed the internet
        if (raw is not null)
            foreach (string? key in raw.AllKeys)
            {
                if (key is null) continue;
                foreach (var value in raw.GetValues(key) ?? [])
                    headers.Add(new EmailHeader(key, value));
            }

        var attachments = new List<EmailAttachment>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (var obj in msg.Attachments)
        {
            index++;
            switch (obj)
            {
                case Storage.Attachment att:
                {
                    var display = string.IsNullOrWhiteSpace(att.FileName)
                        ? $"part{index}{EmailMimeTypes.Extension(att.MimeType)}"
                        : att.FileName;
                    attachments.Add(new EmailAttachment
                    {
                        EntryName   = EmailEntryNaming.SafeUnique(display, used),
                        DisplayName = display,
                        ContentType = string.IsNullOrWhiteSpace(att.MimeType) ? "application/octet-stream" : att.MimeType,
                        Content     = att.Data ?? [],
                        IsInline    = att.IsInline,
                        ContentId   = string.IsNullOrEmpty(att.ContentId) ? null : att.ContentId,
                    });
                    break;
                }
                case Storage.Message nested:
                {
                    using var ms = new MemoryStream();
                    nested.Save(ms);
                    var display = $"{Safe(nested.Subject, "message")}.msg";
                    attachments.Add(new EmailAttachment
                    {
                        EntryName   = EmailEntryNaming.SafeUnique(display, used),
                        DisplayName = display,
                        ContentType = "application/vnd.ms-outlook",
                        Content     = ms.ToArray(),
                        IsInline    = false,
                        ContentId   = null,
                    });
                    break;
                }
            }
        }

        return new EmailDocument
        {
            Headers  = headers,
            From     = FormatSender(msg.Sender),
            To       = FormatRecipients(msg, RecipientType.To),
            Cc       = FormatRecipients(msg, RecipientType.Cc),
            Subject  = NullIfEmpty(msg.Subject),
            Date     = msg.SentOn ?? msg.ReceivedOn,
            TextBody = msg.BodyText,
            HtmlBody = msg.BodyHtml,
            Attachments = attachments,
        };
    }

    private static string? FormatSender(Storage.Sender? sender)
        => sender is null ? null : FormatAddress(sender.DisplayName, sender.Email);

    private static string? FormatRecipients(Storage.Message msg, RecipientType type)
    {
        var parts = new List<string>();
        foreach (var r in msg.Recipients)
            if (r.Type == type && FormatAddress(r.DisplayName, r.Email) is { } a)
                parts.Add(a);
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static string? FormatAddress(string? name, string? email)
    {
        name = name?.Trim();
        email = email?.Trim();
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(email))
            return string.Equals(name, email, StringComparison.OrdinalIgnoreCase) ? email : $"{name} <{email}>";
        return NullIfEmpty(name) ?? NullIfEmpty(email);
    }

    private static string Safe(string? s, string fallback)
        => string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
