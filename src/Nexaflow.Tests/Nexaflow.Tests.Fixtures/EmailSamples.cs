namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Deterministic <c>.eml</c> fixtures for the Email viewer. An <c>.eml</c> is plain RFC-822 text, so these
/// are generated with no parser dependency (keeping this project dependency-free). Covers the shapes the
/// viewer and the VFS attachment path must handle: a multipart/alternative body with a real attachment, an
/// inline <c>cid:</c> image, and an embedded <c>message/rfc822</c>. Outlook <c>.msg</c> (OLE/CFBF) is not
/// hand-buildable here — it is covered by a dedicated unit test in the Email test project instead.
/// </summary>
internal sealed class EmailSamples : ISampleSet
{
    public string SubDirectory => "email";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("simple.eml", Simple),
        SampleFile.Text("inline-image.eml", InlineImage),
        SampleFile.Text("forwarded.eml", Forwarded),
    ];

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    // multipart/mixed → (multipart/alternative: text + html) + a text attachment.
    private static readonly string Simple = Lines(
        "From: Alice Example <alice@example.com>",
        "To: Bob Example <bob@example.com>",
        "Subject: Quarterly report",
        "Date: Mon, 06 Jul 2026 09:30:00 +0000",
        "Message-ID: <simple-001@example.com>",
        "MIME-Version: 1.0",
        "Content-Type: multipart/mixed; boundary=\"MIXED\"",
        "",
        "--MIXED",
        "Content-Type: multipart/alternative; boundary=\"ALT\"",
        "",
        "--ALT",
        "Content-Type: text/plain; charset=\"utf-8\"",
        "",
        "Hello Bob,",
        "",
        "Please find the quarterly report summary attached.",
        "",
        "Regards,",
        "Alice",
        "--ALT",
        "Content-Type: text/html; charset=\"utf-8\"",
        "",
        "<html><body><p>Hello Bob,</p><p>Please find the quarterly report summary attached.</p>" +
        "<p>Regards,<br>Alice</p></body></html>",
        "--ALT--",
        "--MIXED",
        "Content-Type: text/plain; charset=\"utf-8\"; name=\"notes.txt\"",
        "Content-Disposition: attachment; filename=\"notes.txt\"",
        "",
        "Line-item notes: revenue up 12% quarter over quarter.",
        "--MIXED--",
        "");

    // multipart/related: an HTML body referencing an inline PNG by Content-ID (a 1x1 transparent png).
    private static readonly string InlineImage = Lines(
        "From: Newsletter <news@example.com>",
        "To: Reader <reader@example.com>",
        "Subject: Weekly digest",
        "Date: Tue, 07 Jul 2026 12:00:00 +0000",
        "Message-ID: <digest-002@example.com>",
        "MIME-Version: 1.0",
        "Content-Type: multipart/related; boundary=\"REL\"",
        "",
        "--REL",
        "Content-Type: text/html; charset=\"utf-8\"",
        "",
        "<html><body><h1>Weekly digest</h1><img src=\"cid:logo123\" alt=\"logo\">" +
        "<p>Thanks for reading.</p></body></html>",
        "--REL",
        "Content-Type: image/png",
        "Content-Transfer-Encoding: base64",
        "Content-ID: <logo123>",
        "Content-Disposition: inline; filename=\"logo.png\"",
        "",
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
        "--REL--",
        "");

    // multipart/mixed with an embedded message/rfc822 (a forwarded email).
    private static readonly string Forwarded = Lines(
        "From: Carol <carol@example.com>",
        "To: Dave <dave@example.com>",
        "Subject: Fwd: Meeting notes",
        "Date: Wed, 08 Jul 2026 15:00:00 +0000",
        "Message-ID: <fwd-003@example.com>",
        "MIME-Version: 1.0",
        "Content-Type: multipart/mixed; boundary=\"FWD\"",
        "",
        "--FWD",
        "Content-Type: text/plain; charset=\"utf-8\"",
        "",
        "See the forwarded message below.",
        "--FWD",
        "Content-Type: message/rfc822",
        "Content-Disposition: attachment",
        "",
        "From: Erin <erin@example.com>",
        "To: Carol <carol@example.com>",
        "Subject: Meeting notes",
        "Date: Tue, 07 Jul 2026 10:00:00 +0000",
        "",
        "The meeting is confirmed for Friday at 10am.",
        "--FWD--",
        "");
}
