using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Qr;

namespace Nexaflow.Tests.Visuals.Markdown.Qr;

/// <summary>
/// The block body: which <c>key: value</c> lines are accepted, what each <c>type:</c> turns into, and
/// what the author is told when a line is wrong.
///
/// <para>
/// The payload assertions are the point of the type system, so they are written out in full rather
/// than checked for a substring: a Wi-Fi descriptor that forgets to escape a semicolon, or a vCard
/// missing its structured name, is exactly the failure that looks fine on screen and does nothing
/// when scanned.
/// </para>
/// </summary>
[TestClass]
[CoversNode("qr-block-syntax")]
public class QrBlockParserTests
{
    private static QrBlock Parse(string source)
    {
        Assert.IsTrue(QrBlockParser.TryParse(source, out var block, out string? error), error);
        return block!;
    }

    private static string Rejects(string source)
    {
        Assert.IsFalse(QrBlockParser.TryParse(source, out _, out string? error), "expected a rejection");
        Assert.IsFalse(string.IsNullOrWhiteSpace(error), "a rejection should say why");
        return error!;
    }

    // ΓöÇΓöÇ Payload types ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Text_EncodesItsLineVerbatim() =>
        Assert.AreEqual("Hello from markdown.org", Parse(
            """
            type: text
            text: Hello from markdown.org
            """).Payload);

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Url_KeepsItsScheme_AndSuppliesOneWhenAbsent()
    {
        Assert.AreEqual("https://markdown.org/tools/", Parse(
            """
            type: url
            url: https://markdown.org/tools/
            """).Payload);

        // A bare host would otherwise scan as plain text and open nothing.
        Assert.AreEqual("https://markdown.org", Parse("type: url\nurl: markdown.org").Payload);

        // A colon is not proof of a scheme ΓÇö this one introduces a port.
        Assert.AreEqual("https://markdown.org:8080/x", Parse("type: url\nurl: markdown.org:8080/x").Payload);

        // A scheme this block does not model passes through untouched.
        Assert.AreEqual("mailto:hi@example.com", Parse("type: url\nurl: mailto:hi@example.com").Payload);
        Assert.AreEqual("tel:+15551234567", Parse("type: url\nurl: tel:+15551234567").Payload);
    }

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Email_PercentEncodesSubjectAndBody() =>
        Assert.AreEqual("mailto:hi@example.com?subject=Hello&body=Just%20scanned%20your%20code", Parse(
            """
            type: email
            email: hi@example.com
            subject: Hello
            body: Just scanned your code
            """).Payload);

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Phone_StripsTheSpacingAWrittenNumberCarries() =>
        Assert.AreEqual("tel:+15551234567", Parse(
            """
            type: phone
            phone: +1 (555) 123-4567
            """).Payload);

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Sms_UsesTheFormBothPhonesActOn() =>
        Assert.AreEqual("SMSTO:+15551234567:Hello there", Parse(
            """
            type: sms
            number: +15551234567
            message: Hello there
            """).Payload);

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Wifi_BuildsTheJoinDescriptor()
    {
        Assert.AreEqual("WIFI:T:WPA;S:MyNetwork;P:s3cr3t-pass;;", Parse(
            """
            type: wifi
            ssid: MyNetwork
            password: s3cr3t-pass
            security: WPA
            hidden: false
            """).Payload);

        // A hidden network has to say so, or the phone will not look for it.
        StringAssert.Contains(Parse(
            """
            type: wifi
            ssid: MyNetwork
            password: s3cr3t-pass
            hidden: true
            """).Payload, "H:true;");
    }

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Wifi_EscapesTheCharactersThatWouldEndAField() =>
        Assert.AreEqual(@"WIFI:T:WPA;S:Cafe\; Bar;P:a\:b\,c;;", Parse(
            """
            type: wifi
            ssid: Cafe; Bar
            password: a:b,c
            """).Payload);

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Wifi_WithNoPassword_IsAnOpenNetwork() =>
        Assert.AreEqual("WIFI:T:nopass;S:Guest;;", Parse(
            """
            type: wifi
            ssid: Guest
            """).Payload);

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Vcard_CarriesAStructuredNameAndTheOptionalFields()
    {
        string payload = Parse(
            """
            type: vcard
            name: Ada Lovelace
            org: Analytical Engines
            title: Engineer
            phone: +15551234567
            email: ada@example.com
            url: https://example.com
            address: 12 Baker St, London
            """).Payload;

        Assert.AreEqual(
            "BEGIN:VCARD\r\n"
          + "VERSION:3.0\r\n"
          + "N:Lovelace;Ada;;;\r\n"
          + "FN:Ada Lovelace\r\n"
          + "ORG:Analytical Engines\r\n"
          + "TITLE:Engineer\r\n"
          + "TEL;TYPE=CELL:+15551234567\r\n"
          + "EMAIL;TYPE=INTERNET:ada@example.com\r\n"
          + "URL:https://example.com\r\n"
          + @"ADR;TYPE=WORK:;;12 Baker St\, London;;;;" + "\r\n"
          + "END:VCARD\r\n",
            payload);
    }

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Vcard_OmitsWhatWasNotGiven()
    {
        string payload = Parse(
            """
            type: vcard
            name: Ada
            """).Payload;

        Assert.AreEqual("BEGIN:VCARD\r\nVERSION:3.0\r\nN:Ada;;;;\r\nFN:Ada\r\nEND:VCARD\r\n", payload);
    }

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Geo_IsTheCoordinatePair() =>
        Assert.AreEqual("geo:51.5074,-0.1278", Parse(
            """
            type: geo
            lat: 51.5074
            lng: -0.1278
            """).Payload);

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Event_BecomesACalendarEntry() =>
        Assert.AreEqual(
            "BEGIN:VEVENT\r\n"
          + "SUMMARY:Launch party\r\n"
          + "LOCATION:The Office\r\n"
          + "DTSTART:20260801T180000\r\n"
          + "DTEND:20260801T210000\r\n"
          + "END:VEVENT\r\n",
            Parse(
            """
            type: event
            title: Launch party
            location: The Office
            start: 2026-08-01T18:00
            end: 2026-08-01T21:00
            """).Payload);

    [TestMethod]
    [CoversNode("qr-payloads")]
    public void Crypto_IsThePaymentUri()
    {
        Assert.AreEqual("bitcoin:1BoatSLRHtKNngkdXEeobR76b53LETtpyT?amount=0.01", Parse(
            """
            type: crypto
            coin: bitcoin
            address: 1BoatSLRHtKNngkdXEeobR76b53LETtpyT
            amount: 0.01
            """).Payload);

        // A ticker names the same scheme.
        StringAssert.StartsWith(Parse(
            """
            type: crypto
            coin: ETH
            address: 0xabc
            """).Payload, "ethereum:");
    }

    // ΓöÇΓöÇ Settings ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    [TestMethod]
    public void Settings_DefaultWhenUnstated()
    {
        var block = Parse(
            """
            type: url
            url: https://markdown.org
            """);

        Assert.AreEqual(QrErrorCorrection.Medium, block.ErrorCorrection);
        Assert.AreEqual(QrBlock.DefaultCellSize, block.CellSize);
        Assert.AreEqual(QrBlock.DefaultMargin, block.Margin);
        Assert.IsNull(block.Dark);
        Assert.IsNull(block.Light);
    }

    [TestMethod]
    public void Settings_AreReadFromTheBlock()
    {
        var block = Parse(
            """
            type: url
            url: https://markdown.org
            ec: M
            cellSize: 4
            margin: 4
            dark: #000000
            light: #ffffff
            """);

        Assert.AreEqual(QrErrorCorrection.Medium, block.ErrorCorrection);
        Assert.AreEqual(4, block.CellSize);
        Assert.AreEqual(4, block.Margin);
        Assert.AreEqual(new QrColor(0xFF, 0x00, 0x00, 0x00), block.Dark);
        Assert.AreEqual(new QrColor(0xFF, 0xFF, 0xFF, 0xFF), block.Light);
    }

    [TestMethod]
    public void ErrorCorrection_AcceptsEveryLevel()
    {
        (string Letter, QrErrorCorrection Level)[] levels =
        [
            ("L", QrErrorCorrection.Low),
            ("m", QrErrorCorrection.Medium),
            ("Q", QrErrorCorrection.Quartile),
            ("h", QrErrorCorrection.High),
        ];

        foreach (var (letter, level) in levels)
            Assert.AreEqual(level, Parse($"type: text\ntext: x\nec: {letter}").ErrorCorrection);
    }

    [TestMethod]
    public void Colour_AcceptsShortHexAndAnAlphaChannel()
    {
        Assert.AreEqual(new QrColor(0xFF, 0xAA, 0xBB, 0xCC), Parse("type: text\ntext: x\ndark: #abc").Dark);
        Assert.AreEqual(new QrColor(0x80, 0x11, 0x22, 0x33), Parse("type: text\ntext: x\nlight: #80112233").Light);
        Assert.AreEqual(new QrColor(0xFF, 0x00, 0x00, 0x00), Parse("type: text\ntext: x\ndark: 000000").Dark);
    }

    [TestMethod]
    public void BlankLinesAndComments_AreSkipped() =>
        Assert.AreEqual("x", Parse(
            """
            # the poster's code

            type: text

            text: x
            """).Payload);

    [TestMethod]
    public void ValueKeepsEverythingAfterTheFirstColon() =>
        Assert.AreEqual("https://example.com/a:b?q=1:2", Parse(
            """
            type: url
            url: https://example.com/a:b?q=1:2
            """).Payload);

    // ΓöÇΓöÇ Diagnostics ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    [TestMethod]
    public void MissingType_SaysWhatTheTypesAre() =>
        StringAssert.Contains(Rejects("text: hello"), "wifi");

    [TestMethod]
    public void UnknownType_NamesIt() =>
        StringAssert.Contains(Rejects("type: barcode\ntext: x"), "barcode");

    [TestMethod]
    public void MistypedSettingKey_IsRefused_NotIgnored()
    {
        // The failure this guards against is silent: a code that renders at the default size while the
        // author believes they changed it.
        StringAssert.Contains(Rejects("type: text\ntext: x\ncelsize: 8"), "celsize");
    }

    [TestMethod]
    public void FieldOfAnotherType_IsRefused() =>
        StringAssert.Contains(Rejects("type: url\nurl: https://x.test\nssid: Home"), "ssid");

    [TestMethod]
    public void MissingRequiredField_NamesIt() =>
        StringAssert.Contains(Rejects("type: wifi\npassword: hunter2"), "ssid");

    [TestMethod]
    public void LineWithoutAColon_IsRefused() =>
        StringAssert.Contains(Rejects("type: text\njust some prose"), "just some prose");

    [TestMethod]
    public void EmptyBlock_IsRefused() =>
        StringAssert.Contains(Rejects("   \n\n"), "empty");

    [TestMethod]
    public void BadSettingValues_AreEachExplained()
    {
        StringAssert.Contains(Rejects("type: text\ntext: x\nec: Z"),           "L, M, Q or H");
        StringAssert.Contains(Rejects("type: text\ntext: x\ncellSize: huge"),  "whole number");
        StringAssert.Contains(Rejects("type: text\ntext: x\ncellSize: 0"),     "range");
        StringAssert.Contains(Rejects("type: text\ntext: x\nmargin: 99"),      "range");
        StringAssert.Contains(Rejects("type: text\ntext: x\ndark: reddish"),   "hex colour");
        StringAssert.Contains(Rejects("type: geo\nlat: 91\nlng: 0"),           "latitude");
        StringAssert.Contains(Rejects("type: event\ntitle: t\nstart: someday"), "date/time");
    }
}
