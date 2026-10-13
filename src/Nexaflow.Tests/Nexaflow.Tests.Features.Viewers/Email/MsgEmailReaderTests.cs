using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexaflow.Features.Email.Model;
using Nexaflow.Features.Email.Reading;
using Nexaflow.Features.Email.Vfs;
using Nexaflow.IO.Common;
using Nexaflow.IO.CompoundFile;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Email;

/// <summary>
/// Reading an Outlook <c>.msg</c> into the shared <see cref="EmailDocument"/>: its envelope however its addresses were
/// kept, the best body it kept — HTML, the HTML its RTF was made from, or its RTF — its attachments of every kind, and a
/// signed message read as what it signs.
/// </summary>
[TestClass]
[CoversNode("email-parse")]
public class MsgEmailReaderTests
{
    private static readonly DateTime Sent = new(2026, 7, 6, 9, 30, 0, DateTimeKind.Utc);

    // ── The envelope ────────────────────────────────────────────────────────

    [TestMethod]
    public void TheEnvelopeIsReadFromItsProperties()
    {
        var doc = Read(Message()
            .String(0x0037, "Quarterly report")
            .String(0x0C1A, "Alice Example").String(0x0C1E, "SMTP").String(0x0C1F, "alice@example.com")
            .Time(0x0039, Sent)
            .Recipient(1, "Bob Example", "bob@example.com")
            .Recipient(1, "Carol", "carol@example.com")
            .Recipient(2, "Dave", "dave@example.com")
            .Recipient(3, "Hidden", "bcc@example.com"));

        Assert.AreEqual("Quarterly report", doc.Subject);
        Assert.AreEqual("Alice Example <alice@example.com>", doc.From);
        Assert.AreEqual("Bob Example <bob@example.com>, Carol <carol@example.com>", doc.To);
        Assert.AreEqual("Dave <dave@example.com>", doc.Cc);
        Assert.AreEqual(new DateTimeOffset(Sent), doc.Date);
    }

    [TestMethod]
    public void AnExchangeSenderIsGivenTheirInternetAddress()
    {
        var doc = Read(Message()
            .String(0x0C1A, "Alice").String(0x0C1E, "EX").String(0x0C1F, "/o=Example/ou=Admin/cn=Recipients/cn=alice")
            .String(0x5D01, "alice@example.com"));

        Assert.AreEqual("Alice <alice@example.com>", doc.From);
    }

    [TestMethod]
    public void AMessageSentOnSomeonesBehalfIsFromThem()
    {
        var doc = Read(Message()
            .String(0x0C1A, "Assistant").String(0x5D01, "assistant@example.com")
            .String(0x0042, "Boss").String(0x5D02, "boss@example.com"));

        Assert.AreEqual("Boss <boss@example.com>", doc.From);
    }

    [TestMethod]
    public void WithNoInternetAddressAnywhere_TheFromHeaderSaysIt()
    {
        var doc = Read(Message()
            .String(0x0C1A, "Alice").String(0x0C1E, "EX").String(0x0C1F, "/o=Example/cn=alice")
            .String(0x007D, "From: Alice <alice@example.com>\r\nSubject: hi\r\n"));

        Assert.AreEqual("Alice <alice@example.com>", doc.From);
    }

    [TestMethod]
    public void TheHeadersItArrivedWithAreKept()
    {
        var doc = Read(Message().String(0x007D,
            "Received: from mx.example.com\r\nMessage-ID: <one@example.com>\r\nSubject: Quarterly report\r\n"));

        CollectionAssert.AreEqual(new[] { "Received", "Message-ID", "Subject" }, doc.Headers.Select(h => h.Field).ToArray());
        Assert.AreEqual("from mx.example.com", doc.Headers[0].Value.Trim());
    }

    [TestMethod]
    public void AMessageThatNeverCrossedTheInternetHasNoHeaders()
    {
        Assert.AreEqual(0, Read(Message().String(0x0037, "internal")).Headers.Count);
    }

    [TestMethod]
    public void AnEightBitStringIsReadInTheMessagesCodePage()
    {
        // Russian for "hello", in Windows-1251.
        var doc = Read(Message().Integer(0x3FFD, 1251).Narrow8(0x0037, [0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2]));

        Assert.AreEqual("\u041F\u0440\u0438\u0432\u0435\u0442", doc.Subject);
    }

    // ── The body ────────────────────────────────────────────────────────────

    [TestMethod]
    public void AnHtmlBodyIsReadInTheCodePageItWentOutIn()
    {
        var doc = Read(Message()
            .String(0x1000, "plain words")
            .Integer(0x3FDE, 1252)
            .Binary(0x1013, Encoding.Latin1.GetBytes("<p>caf\u00E9</p>")));

        Assert.AreEqual("plain words", doc.TextBody);
        Assert.AreEqual("<p>caf\u00E9</p>", doc.HtmlBody);
        Assert.IsNull(doc.RtfBody);
    }

    [TestMethod]
    public void RtfMadeFromHtmlIsReadAsTheHtmlItWasMadeFrom()
    {
        const string rtf = @"{\rtf1\ansi\fromhtml1 {\*\htmltag1 <p>}Hello{\*\htmltag2 </p>}}";

        var doc = Read(Message().Binary(0x1009, MsgBuilder.Stored(rtf)));

        Assert.AreEqual("<p>Hello</p>", doc.HtmlBody);
        Assert.IsNull(doc.RtfBody);
    }

    [TestMethod]
    public void RtfThatWasNeverHtmlIsKeptAsRtf_WhetherStoredOrCompressed()
    {
        const string rtf = @"{\rtf1\ansi Rich {\b text}\par}";

        foreach (var stored in new[] { MsgBuilder.Stored(rtf), MsgBuilder.Compressed(rtf) })
        {
            var doc = Read(Message().Binary(0x1009, stored));

            Assert.AreEqual(rtf, doc.RtfBody);
            Assert.IsNull(doc.HtmlBody);
        }
    }

    [TestMethod]
    public void CompressedRtfFailingItsCheckIsNotRead()
    {
        var doc = Read(Message().String(0x1000, "words").Binary(0x1009, MsgBuilder.Compressed(@"{\rtf1 x}", corrupt: true)));

        Assert.IsNull(doc.RtfBody);
        Assert.AreEqual("words", doc.TextBody);
    }

    [TestMethod]
    public void HtmlKeptBesideRtfIsTheBody()
    {
        var doc = Read(Message().Binary(0x1013, Encoding.UTF8.GetBytes("<p>html</p>")).Binary(0x1009, MsgBuilder.Stored(@"{\rtf1 rtf}")));

        Assert.AreEqual("<p>html</p>", doc.HtmlBody);
        Assert.IsNull(doc.RtfBody);
    }

    // ── Attachments ─────────────────────────────────────────────────────────

    [TestMethod]
    public void AFileAttachmentIsItsBytes_UnderItsLongName()
    {
        var doc = Read(Message().Attachment(new MsgBuilder()
            .Integer(0x3705, 1)
            .String(0x3704, "REPORT~1.PDF").String(0x3707, "Quarterly report.pdf")
            .String(0x370E, "application/pdf")
            .Binary(0x3701, [1, 2, 3])));

        var attachment = doc.Attachments.Single();
        Assert.AreEqual("Quarterly report.pdf", attachment.DisplayName);
        Assert.AreEqual("application/pdf", attachment.ContentType);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, attachment.Content);
        Assert.IsFalse(attachment.IsInline);
    }

    [TestMethod]
    public void AnAttachmentTheHtmlShowsIsInline()
    {
        var doc = Read(Message()
            .Binary(0x1013, Encoding.UTF8.GetBytes("<img src=\"cid:logo@1\">"))
            .Attachment(Picture("logo.png").String(0x3712, "<logo@1>"))
            .Attachment(Picture("flagged.png").Integer(0x3714, 4))
            .Attachment(Picture("hidden.png").Flag(0x7FFE, true))
            .Attachment(Picture("attached.png")));

        var inline = doc.Attachments.ToDictionary(a => a.DisplayName, a => a.IsInline);
        Assert.IsTrue(inline["logo.png"], "named by a cid in the HTML");
        Assert.IsTrue(inline["flagged.png"], "flagged as the HTML's");
        Assert.IsTrue(inline["hidden.png"], "hidden from the attachment list");
        Assert.IsFalse(inline["attached.png"]);
        Assert.AreEqual("logo@1", doc.Attachments[0].ContentId);
    }

    [TestMethod]
    public void TwoAttachmentsOfOneNameAreTwoEntries_AndOneWithNoNameIsGivenOne()
    {
        var doc = Read(Message()
            .Attachment(new MsgBuilder().String(0x3707, "notes.txt").Binary(0x3701, [1]))
            .Attachment(new MsgBuilder().String(0x3707, "notes.txt").Binary(0x3701, [2]))
            .Attachment(new MsgBuilder().String(0x370E, "image/png").Binary(0x3701, [3])));

        Assert.AreEqual(3, doc.Attachments.Select(a => a.EntryName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual("part3.png", doc.Attachments[2].DisplayName);
    }

    [TestMethod]
    public void AnAttachmentByReferenceIsAnEntryWithNothingInIt()
    {
        var doc = Read(Message().Attachment(new MsgBuilder().Integer(0x3705, 2).String(0x3707, "on the share.docx")));

        Assert.AreEqual(0, doc.Attachments.Single().Content.Length);
    }

    [TestMethod]
    public void AnEmbeddedMessageIsAMsgOfItsOwn_ThatReadsAsItself()
    {
        var inner = new MsgBuilder()
            .String(0x0037, "Meeting notes")
            .String(0x1000, "The meeting is confirmed.")
            .Recipient(1, "Carol", "carol@example.com")
            .Attachment(new MsgBuilder().String(0x3707, "agenda.txt").Binary(0x3701, Encoding.ASCII.GetBytes("agenda")));

        var doc = Read(Message().String(0x0037, "Fwd: Meeting notes").Attachment(new MsgBuilder().Embedding(inner)));

        var attachment = doc.Attachments.Single();
        Assert.AreEqual("Meeting notes.msg", attachment.DisplayName);
        Assert.AreEqual("application/vnd.ms-outlook", attachment.ContentType);

        var standalone = CompoundFile.Open(new MemoryStream(attachment.Content)).Root;
        Assert.IsNotNull(standalone.Storage("__nameid_version1.0"), "the named-property map is carried in");
        Assert.AreEqual(32 + (2 * 16), standalone.Stream("__properties_version1.0")!.Length, "a file's header, not an embedded one's");

        var read = ReadBytes(attachment.Content);
        Assert.AreEqual("Meeting notes", read.Subject);
        Assert.AreEqual("The meeting is confirmed.", read.TextBody);
        Assert.AreEqual("Carol <carol@example.com>", read.To);
        Assert.AreEqual("agenda.txt", read.Attachments.Single().DisplayName);
    }

    [TestMethod]
    public void AnOleObjectIsTheCompoundFileItIs()
    {
        var ole = new CompoundFolder("object");
        ole.Add("\u0001Ole10Native", [9, 8, 7]);

        var doc = Read(Message().Attachment(new MsgBuilder().Ole(ole).String(0x3707, "drawing.bin")));

        var read = CompoundFile.Open(new MemoryStream(doc.Attachments.Single().Content)).Root;
        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, read.Stream("\u0001Ole10Native")!.Read());
    }

    [TestMethod]
    public void ASignedMessageIsReadAsWhatItSigns()
    {
        const string signed =
            "Content-Type: multipart/signed; protocol=\"application/pkcs7-signature\"; micalg=sha-256; boundary=\"SIG\"\r\n\r\n" +
            "--SIG\r\n" +
            "Content-Type: multipart/mixed; boundary=\"MIX\"\r\n\r\n" +
            "--MIX\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nSigned words.\r\n" +
            "--MIX\r\nContent-Type: text/plain; name=\"terms.txt\"\r\nContent-Disposition: attachment; filename=\"terms.txt\"\r\n\r\nTerms.\r\n" +
            "--MIX--\r\n" +
            "--SIG\r\nContent-Type: application/pkcs7-signature; name=\"smime.p7s\"\r\nContent-Disposition: attachment; filename=\"smime.p7s\"\r\n\r\nAAAA\r\n" +
            "--SIG--\r\n";

        var doc = Read(Message()
            .String(0x001A, "IPM.Note.SMIME.MultipartSigned")
            .String(0x1000, "")
            .Attachment(new MsgBuilder().String(0x370E, "multipart/signed").String(0x3707, "smime.p7m").Binary(0x3701, Encoding.ASCII.GetBytes(signed))));

        Assert.AreEqual("Signed words.", doc.TextBody?.Trim());
        CollectionAssert.AreEquivalent(new[] { "terms.txt", "smime.p7s" }, doc.Attachments.Select(a => a.DisplayName).ToArray());
    }

    // ── Through the reader every caller uses ────────────────────────────────

    [TestMethod]
    public void AMsgIsReadByItsExtension_AndOpensAsAContainer()
    {
        var bytes = Message().String(0x0037, "Weekly")
            .Attachment(new MsgBuilder().String(0x3707, "notes.txt").Binary(0x3701, Encoding.ASCII.GetBytes("revenue up 12%")))
            .Build();

        var path = Path.Combine(Path.GetTempPath(), $"nexaflow-{Guid.NewGuid():N}.msg");
        File.WriteAllBytes(path, bytes);

        try
        {
            using (var fs = File.OpenRead(path))
                Assert.AreEqual("Weekly", EmailDocumentReader.Instance.Read(fs, Path.GetFileName(path)).Subject);

            var vfs = new VirtualFileSystem();
            vfs.RegisterHandler(new EmailArchiveHandler());

            Assert.IsTrue(vfs.IsContainer(path));
            StringAssert.Contains(Encoding.ASCII.GetString(vfs.ReadAllBytes(Path.Combine(path, "notes.txt"))), "revenue up 12%");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WhatIsNotACompoundFileIsRefused()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ReadBytes(Encoding.ASCII.GetBytes("From: not a msg\r\n\r\nbody")));
    }

    // ── Making messages ─────────────────────────────────────────────────────

    private static MsgBuilder Message() => new();

    private static MsgBuilder Picture(string name) =>
        new MsgBuilder().String(0x3707, name).String(0x370E, "image/png").Binary(0x3701, [0x89, 0x50, 0x4E, 0x47]);

    private static EmailDocument Read(MsgBuilder message) => ReadBytes(message.Build());

    private static EmailDocument ReadBytes(byte[] bytes) => new MsgEmailReader().Read(new MemoryStream(bytes), "message.msg");
}
