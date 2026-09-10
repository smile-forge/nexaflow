using System.IO.Compression;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Word (OOXML) fixtures for the Office feature's search text extractor. Hand-written WordprocessingML zipped
/// here, so this library stays dependency-free and — more to the point — each fixture pins the exact markup
/// under test (a word split across runs, a space-only run, a text box written twice, Strict namespaces)
/// independently of the parser that reads it back.
/// <para>
/// Public, like <see cref="PdfSamples"/>, only so the words below agree between the fixtures and the tests
/// asserting on them. Each is a word that appears in exactly one place, so a hit proves where it came from.
/// </para>
/// </summary>
public sealed class OfficeSamples : ISampleSet
{
    // Fixed epoch so the produced packages are byte-identical every run.
    private static readonly DateTimeOffset Epoch = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public string SubDirectory => "office";

    /// <summary>In <c>text.docx</c>'s body, written as two runs — "Geor" (bold) + "giana".</summary>
    public const string SplitWord = "Georgiana";

    /// <summary>Two words in <c>text.docx</c> whose only separator is a space-only <c>xml:space="preserve"</c> run.</summary>
    public const string SpacedPhrase = "amber lantern";

    /// <summary>The two cells of <c>text.docx</c>'s one-row table.</summary>
    public const string LeftCell = "north", RightCell = "south";

    /// <summary>Separated in <c>text.docx</c> by a <c>w:tab</c> run element.</summary>
    public const string TabbedLeft = "port", TabbedRight = "starboard";

    public const string TitleNeedle    = "Heron Migration Survey";   // docProps/core.xml title
    public const string HeaderNeedle   = "letterhead";
    public const string FooterNeedle   = "colophon";
    public const string FootnoteNeedle = "ibidem";
    public const string EndnoteNeedle  = "addendum";
    public const string CommentNeedle  = "marginalia";
    public const string MathNeedle     = "hypotenuse";
    public const string FieldResult    = "gazetteer";                // the shown text of a HYPERLINK field

    /// <summary>In both the mc:Choice and the VML mc:Fallback of one text box — must be read once.</summary>
    public const string TextBoxNeedle = "sidebar";

    /// <summary>In both the w:moveFrom and the w:moveTo of one tracked move — must be read once.</summary>
    public const string MovedWord = "wanderer";

    /// <summary>Tracked-deleted (<c>w:delText</c>) — must not appear.</summary>
    public const string DeletedWord = "obsolete";

    /// <summary>Inside a field code (<c>w:instrText</c>) — must not appear.</summary>
    public const string FieldCodeWord = "fieldcodetarget";

    /// <summary><c>cp:lastModifiedBy</c> — not one of the searched properties, so must not appear.</summary>
    public const string LastModifiedBy = "Quillon Trask";

    public const string StrictBodyNeedle      = "tundra";
    public const string StrictHeaderNeedle    = "permafrost";
    public const string RelocatedBodyNeedle   = "estuary";
    public const string RelocatedHeaderNeedle = "lighthouse";
    public const string LongRunWord           = "lorem";

    private const string W       = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string Rel     = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string Core    = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";

    public IReadOnlyList<SampleFile> Files { get; } = Build();

    private static IReadOnlyList<SampleFile> Build() =>
    [
        SampleFile.Raw("text.docx", TextDocx()),
        SampleFile.Raw("strict.docx", StrictDocx()),
        SampleFile.Raw("relocated.docx", RelocatedDocx()),
        SampleFile.Raw("empty.docx", Package(
            ("[Content_Types].xml", ContentTypes("/word/document.xml")),
            ("_rels/.rels", Rels(($"{Rel}/officeDocument", "word/document.xml"))),
            ("word/document.xml", $"""<w:document xmlns:w="{W}"><w:body><w:p/><w:p><w:pPr/></w:p><w:sectPr/></w:body></w:document>"""))),
        SampleFile.Raw("long-run.docx", Package(
            ("[Content_Types].xml", ContentTypes("/word/document.xml")),
            ("_rels/.rels", Rels(($"{Rel}/officeDocument", "word/document.xml"))),
            ("word/document.xml", Document(W, $"<w:p><w:r><w:t>{string.Concat(Enumerable.Repeat(LongRunWord + " ", 50_000))}</w:t></w:r></w:p>")))),
        SampleFile.Raw("spreadsheet.docx", Package(
            ("[Content_Types].xml", ContentTypes("/xl/workbook.xml")),
            ("_rels/.rels", Rels(($"{Rel}/officeDocument", "xl/workbook.xml"))),
            ("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheets/></workbook>"""))),
        SampleFile.Raw("malformed.docx", Package(
            ("[Content_Types].xml", ContentTypes("/word/document.xml")),
            ("_rels/.rels", Rels(($"{Rel}/officeDocument", "word/document.xml"))),
            ("word/document.xml", $"""<w:document xmlns:w="{W}"><w:body><w:p><w:r><w:t>partial</w:t></w:r></w:p><w:p><w:r><w:t>broken"""))),
        SampleFile.Raw("corrupt.docx", Encoding.UTF8.GetBytes("this is not a zip archive")),
        // What Word writes for a password-protected document: an OLE compound file, not a zip.
        SampleFile.Raw("encrypted.docx", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, .. new byte[504]]),
    ];

    private static byte[] TextDocx()
    {
        const string mc  = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        const string wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        const string m   = "http://schemas.openxmlformats.org/officeDocument/2006/math";

        var body = $"""
            <w:p><w:pPr><w:tabs><w:tab w:val="left" w:pos="720"/></w:tabs></w:pPr><w:r><w:t xml:space="preserve">The survey of </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>Geor</w:t></w:r><w:r><w:t>giana</w:t></w:r><w:r><w:t xml:space="preserve"> began.</w:t></w:r><w:r><w:footnoteReference w:id="1"/></w:r></w:p>
            <w:p><w:r><w:t>amber</w:t></w:r><w:r><w:t xml:space="preserve"> </w:t></w:r><w:r><w:t>lantern</w:t></w:r></w:p>
            <w:p><w:r><w:t>{TabbedLeft}</w:t></w:r><w:r><w:tab/><w:t>{TabbedRight}</w:t></w:r></w:p>
            <w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="100"/><w:gridCol w:w="100"/></w:tblGrid><w:tr><w:tc><w:tcPr/><w:p><w:r><w:t>{LeftCell}</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>{RightCell}</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            <w:p><w:del w:id="1" w:author="a"><w:r><w:delText>{DeletedWord}</w:delText></w:r></w:del><w:r><w:t xml:space="preserve">kept </w:t></w:r><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve"> HYPERLINK "https://{FieldCodeWord}.invalid/" </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>{FieldResult}</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
            <w:p><w:moveFrom w:id="2" w:author="a"><w:r><w:t>{MovedWord}</w:t></w:r></w:moveFrom></w:p>
            <w:p><w:moveTo w:id="3" w:author="a"><w:r><w:t>{MovedWord}</w:t></w:r></w:moveTo></w:p>
            <w:p><w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing><wps:wsp><wps:txbx><w:txbxContent><w:p><w:r><w:t>{TextBoxNeedle}</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></w:drawing></mc:Choice><mc:Fallback><w:pict><v:shape><v:textbox><w:txbxContent><w:p><w:r><w:t>{TextBoxNeedle}</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>
            <w:p><m:oMath><m:r><m:t>{MathNeedle}</m:t></m:r></m:oMath></w:p>
            <w:sectPr><w:headerReference w:type="default" r:id="rId1"/><w:footerReference w:type="default" r:id="rId2"/></w:sectPr>
            """;

        return Package(
            ("[Content_Types].xml", ContentTypes("/word/document.xml")),
            ("_rels/.rels", Rels(($"{Rel}/officeDocument", "word/document.xml"), (Core, "docProps/core.xml"))),
            ("docProps/core.xml", $"""<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>{TitleNeedle}</dc:title><dc:creator>Fixture Author</dc:creator><cp:lastModifiedBy>{LastModifiedBy}</cp:lastModifiedBy></cp:coreProperties>"""),
            ("word/document.xml", Document(W, body, $"""xmlns:r="{Rel}" xmlns:mc="{mc}" xmlns:wps="{wps}" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:m="{m}" mc:Ignorable="wps" """)),
            ("word/_rels/document.xml.rels", Rels(
                ($"{Rel}/header", "header1.xml"),
                ($"{Rel}/footer", "footer1.xml"),
                ($"{Rel}/footnotes", "footnotes.xml"),
                ($"{Rel}/endnotes", "endnotes.xml"),
                ($"{Rel}/comments", "comments.xml"))),
            ("word/header1.xml", Story(W, "hdr", HeaderNeedle)),
            ("word/footer1.xml", Story(W, "ftr", FooterNeedle)),
            ("word/footnotes.xml", $"""<w:footnotes xmlns:w="{W}"><w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:id="1"><w:p><w:r><w:t>{FootnoteNeedle}</w:t></w:r></w:p></w:footnote></w:footnotes>"""),
            ("word/endnotes.xml", $"""<w:endnotes xmlns:w="{W}"><w:endnote w:id="1"><w:p><w:r><w:t>{EndnoteNeedle}</w:t></w:r></w:p></w:endnote></w:endnotes>"""),
            ("word/comments.xml", $"""<w:comments xmlns:w="{W}"><w:comment w:id="0" w:author="a"><w:p><w:r><w:t>{CommentNeedle}</w:t></w:r></w:p></w:comment></w:comments>"""));
    }

    // ISO 29500 Strict: every namespace and relationship type is spelled differently from Transitional.
    private static byte[] StrictDocx() => Package(
        ("[Content_Types].xml", ContentTypes("/word/document.xml")),
        ("_rels/.rels", Rels(($"{RStrict}/officeDocument", "word/document.xml"))),
        ("word/document.xml", Document(WStrict, $"<w:p><w:r><w:t>{StrictBodyNeedle}</w:t></w:r></w:p>")),
        ("word/_rels/document.xml.rels", Rels(($"{RStrict}/header", "header1.xml"))),
        ("word/header1.xml", Story(WStrict, "hdr", StrictHeaderNeedle)));

    // Nothing at the conventional paths: the main part is reached by an absolute, percent-encoded target, and
    // its header by a relative one that climbs out of its folder — the parser must follow relationships.
    private static byte[] RelocatedDocx() => Package(
        ("[Content_Types].xml", ContentTypes("/content/main story.xml")),
        ("_rels/.rels", Rels(($"{Rel}/officeDocument", "/content/main%20story.xml"))),
        ("content/main story.xml", Document(W, $"<w:p><w:r><w:t>{RelocatedBodyNeedle}</w:t></w:r></w:p>")),
        ("content/_rels/main story.xml.rels", Rels(($"{Rel}/header", "../shared/header1.xml"))),
        ("shared/header1.xml", Story(W, "hdr", RelocatedHeaderNeedle)));

    private static string Document(string ns, string body, string extraNamespaces = "") =>
        $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="{ns}" {extraNamespaces}><w:body>{body}</w:body></w:document>""";

    private static string Story(string ns, string root, string word) =>
        $"""<w:{root} xmlns:w="{ns}"><w:p><w:r><w:t>{word}</w:t></w:r></w:p></w:{root}>""";

    private static string Rels(params (string Type, string Target)[] relationships) =>
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + string.Concat(relationships.Select((r, i) => $"""<Relationship Id="rId{i + 1}" Type="{r.Type}" Target="{r.Target}"/>"""))
        + "</Relationships>";

    private static string ContentTypes(string mainPart) =>
        $"""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="{mainPart}" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""";

    private static byte[] Package(params (string Name, string Xml)[] parts)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, xml) in parts)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = Epoch;
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(xml);
                s.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }
}
