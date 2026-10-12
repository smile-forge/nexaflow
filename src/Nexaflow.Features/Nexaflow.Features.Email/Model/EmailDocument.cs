using System;
using System.Collections.Generic;

namespace Nexaflow.Features.Email.Model;

/// <summary>
/// A parsed email, normalised so a <c>.eml</c> (MimeKit) and a <c>.msg</c> (MS-OXMSG, read over the compound file it is)
/// present one shape to the view-model and the VFS session. Envelope fields are pre-formatted display strings;
/// <see cref="Headers"/> is the full raw header list (for the expandable "all headers" panel); the body is available as
/// HTML, RTF and/or plain text; <see cref="Attachments"/> is every non-body part (regular <i>and</i> inline), in message
/// order, each carrying its decoded bytes and a stable VFS entry name.
/// </summary>
internal sealed class EmailDocument
{
    public required IReadOnlyList<EmailHeader> Headers { get; init; }

    public string? From { get; init; }
    public string? To { get; init; }
    public string? Cc { get; init; }
    public string? Subject { get; init; }
    public DateTimeOffset? Date { get; init; }

    /// <summary>Plain-text body, or null when the message has none.</summary>
    public string? TextBody { get; init; }

    /// <summary>HTML body, or null when the message has none.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>
    /// The body as RTF, where that is the best the message kept — an Outlook message written as rich text rather than as HTML
    /// — or null. Never set beside <see cref="HtmlBody"/>: RTF that was made from HTML is read back as that HTML. The RTF's
    /// bytes, one character to each, as the RTF reader reads them.
    /// </summary>
    public string? RtfBody { get; init; }

    public required IReadOnlyList<EmailAttachment> Attachments { get; init; }
}

/// <summary>One raw header line, e.g. <c>("Received", "from mx.example …")</c>.</summary>
internal sealed record EmailHeader(string Field, string Value);
