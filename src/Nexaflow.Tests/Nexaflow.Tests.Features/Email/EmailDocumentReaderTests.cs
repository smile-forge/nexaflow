using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexaflow.Features.Email.Model;
using Nexaflow.Features.Email.Reading;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Email;

/// <summary>Parsing an <c>.eml</c> into the shared <see cref="EmailDocument"/>: bodies, the attachment vs
/// inline split, embedded messages, header capture, and filename de-duplication.</summary>
[TestClass]
[CoversNode("email-parse")]
public class EmailDocumentReaderTests
{
    private static EmailDocument Read(string sampleFile)
    {
        var path = TestSampleData.Path("email", sampleFile);
        using var fs = File.OpenRead(path);
        return EmailDocumentReader.Instance.Read(fs, Path.GetFileName(path));
    }

    private static EmailDocument ReadRaw(string mime, string fileName)
    {
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(mime));
        return EmailDocumentReader.Instance.Read(ms, fileName);
    }

    [TestMethod]
    public void Simple_HasBothBodies_AndOneAttachment()
    {
        var doc = Read("simple.eml");

        Assert.AreEqual("Quarterly report", doc.Subject);
        Assert.IsNotNull(doc.From);
        StringAssert.Contains(doc.From!, "alice@example.com");
        Assert.IsFalse(string.IsNullOrEmpty(doc.HtmlBody), "html body expected");
        Assert.IsNotNull(doc.TextBody);
        StringAssert.Contains(doc.TextBody!, "Hello Bob");

        var attachments = doc.Attachments.Where(a => !a.IsInline).ToList();
        Assert.AreEqual(1, attachments.Count);
        Assert.AreEqual("notes.txt", attachments[0].DisplayName);
        StringAssert.Contains(Encoding.UTF8.GetString(attachments[0].Content), "revenue up 12%");
    }

    [TestMethod]
    public void InlineImage_IsInline_NotAttachment()
    {
        var doc = Read("inline-image.eml");

        Assert.AreEqual(0, doc.Attachments.Count(a => !a.IsInline), "the cid: image is inline, not an attachment");
        var inline = doc.Attachments.Single(a => a.IsInline);
        Assert.AreEqual("logo123", inline.ContentId);
        StringAssert.Contains(inline.ContentType, "image/png");
        Assert.IsTrue(inline.Content.Length > 0, "inline image should have decoded bytes");
    }

    [TestMethod]
    public void Forwarded_EmbeddedMessage_BecomesEmlAttachment()
    {
        var doc = Read("forwarded.eml");

        var embedded = doc.Attachments.Single(a => a.EntryName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(embedded.DisplayName, "Meeting notes");
        StringAssert.Contains(Encoding.UTF8.GetString(embedded.Content), "confirmed for Friday");
    }

    [TestMethod]
    public void AllHeaders_AreCaptured()
    {
        var doc = Read("simple.eml");

        Assert.IsTrue(doc.Headers.Any(h => h.Field.Equals("Subject", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(doc.Headers.Any(h => h.Field.Equals("Message-ID", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(doc.Headers.Any(h => h.Field.Equals("Date", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void DuplicateFilenames_AreDeduplicated()
    {
        var mime = string.Join("\r\n",
            "From: a@example.com",
            "Subject: dup",
            "MIME-Version: 1.0",
            "Content-Type: multipart/mixed; boundary=B",
            "",
            "--B",
            "Content-Type: text/plain; name=\"a.txt\"",
            "Content-Disposition: attachment; filename=\"a.txt\"",
            "",
            "one",
            "--B",
            "Content-Type: text/plain; name=\"a.txt\"",
            "Content-Disposition: attachment; filename=\"a.txt\"",
            "",
            "two",
            "--B--",
            "");

        var doc = ReadRaw(mime, "dup.eml");
        var names = doc.Attachments.Select(a => a.EntryName).ToList();

        Assert.AreEqual(2, names.Count);
        Assert.AreEqual(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "collision-free entry names expected: " + string.Join(", ", names));
    }

    [TestMethod]
    public void MissingFilename_IsSynthesised()
    {
        var mime = string.Join("\r\n",
            "From: a@example.com",
            "Subject: no-name",
            "MIME-Version: 1.0",
            "Content-Type: multipart/mixed; boundary=B",
            "",
            "--B",
            "Content-Type: text/plain",
            "",
            "body text",
            "--B",
            "Content-Type: application/pdf",
            "Content-Disposition: attachment",
            "",
            "%PDF-1.4 fake",
            "--B--",
            "");

        var doc = ReadRaw(mime, "noname.eml");
        var att = doc.Attachments.Single(a => !a.IsInline);
        Assert.IsFalse(string.IsNullOrWhiteSpace(att.EntryName));
        StringAssert.EndsWith(att.EntryName, ".pdf");   // extension synthesised from content type
    }
}
