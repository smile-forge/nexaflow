using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// PDF fixtures for the search text extractor and the image extractor. Hand-encoded so this fixtures library
/// stays dependency-free (per CLAUDE.md) — anything needing a real writer is built in the feature test project
/// with PdfPig.
/// <para>
/// The set is chosen to pin the extractor's three-way answer, which is the whole subtlety of the contract:
/// text found, <b>read it and there is genuinely none</b> (an image-only scan), and <b>couldn't read it at
/// all</b> (corrupt). Those must not collapse into each other — the middle one is a confident non-match, the
/// last one is an admission of ignorance.
/// </para>
/// <para>
/// Public, unlike its sibling sample sets, only because the three values below have to agree between the
/// fixture and the tests asserting on them. A test hard-coding "peregrine" would drift into a mystifying
/// failure the first time this document changed.
/// </para>
/// </summary>
public sealed class PdfSamples : ISampleSet
{
    public string SubDirectory => "pdf";

    /// <summary>A word that appears in <c>text.pdf</c>'s page content and nowhere else.</summary>
    public const string BodyNeedle = "peregrine";

    /// <summary>Title of <c>text.pdf</c>. Deliberately absent from its page content.</summary>
    public const string MetadataTitle = "Quarterly Falconry Review";

    /// <summary>Value of <c>text.pdf</c>'s single form field. Also absent from the page content.</summary>
    public const string FormFieldValue = "Ada Lovelace";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Raw("text.pdf", BuildTextDocument()),
        SampleFile.Raw("image-only.pdf", BuildImageOnlyDocument()),
        SampleFile.Raw("repeated-image.pdf", BuildRepeatedImageDocument()),
        SampleFile.Raw("jpeg-image.pdf", BuildJpegImageDocument()),
        SampleFile.Raw("corrupt.pdf", BuildCorruptDocument()),
    ];

    // ── The documents ────────────────────────────────────────────────────────

    /// <summary>
    /// One page of Helvetica text, plus a title and a filled-in form field that appear nowhere in that text —
    /// so a test can tell "found the body" from "found the metadata".
    /// </summary>
    private static byte[] BuildTextDocument()
    {
        var pdf = new PdfBuilder();

        pdf.Object(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [7 0 R] >> >>");
        pdf.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] "
                    + "/Resources << /Font << /F1 5 0 R >> >> /Annots [7 0 R] /Contents 4 0 R >>");
        pdf.Stream(4, null, Ascii($"BT /F1 14 Tf 20 150 Td (the {BodyNeedle} stoops on prey) Tj ET"));
        pdf.Object(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        pdf.Object(6, $"<< /Title ({MetadataTitle}) /Author (Jane Smith) "
                    + "/Subject (Raptor performance) /Keywords (falcons, hunting) >>");
        pdf.Object(7, $"<< /Type /Annot /Subtype /Widget /Rect [20 20 200 40] /FT /Tx "
                    + $"/T (applicant) /V ({FormFieldValue}) /F 4 >>");

        return pdf.Finish(rootObject: 1, infoObject: 6);
    }

    /// <summary>A scanned page, in effect: one raw RGB image and not a single glyph.</summary>
    private static byte[] BuildImageOnlyDocument()
    {
        var pdf = new PdfBuilder();

        pdf.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        pdf.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] "
                    + "/Resources << /XObject << /Im1 5 0 R >> >> /Contents 4 0 R >>");
        pdf.Stream(4, null, Ascii("q 200 0 0 200 0 0 cm /Im1 Do Q"));
        pdf.Stream(5, ImageDictionary(), RawRgbPixels());

        return pdf.Finish(rootObject: 1, infoObject: null);
    }

    /// <summary>
    /// Two pages, both drawing the <em>same</em> image object — the shape of a real report with a logo in its
    /// header. Extraction must yield one file, not one per page.
    /// </summary>
    private static byte[] BuildRepeatedImageDocument()
    {
        var pdf = new PdfBuilder();

        pdf.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        pdf.Object(2, "<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 2 >>");
        pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] "
                    + "/Resources << /XObject << /Im1 5 0 R >> >> /Contents 4 0 R >>");
        pdf.Stream(4, null, Ascii("q 100 0 0 100 0 0 cm /Im1 Do Q"));
        pdf.Stream(5, ImageDictionary(), RawRgbPixels());
        pdf.Object(6, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] "
                    + "/Resources << /XObject << /Im1 5 0 R >> >> /Contents 7 0 R >>");
        pdf.Stream(7, null, Ascii("q 100 0 0 100 0 0 cm /Im1 Do Q"));

        return pdf.Finish(rootObject: 1, infoObject: null);
    }

    /// <summary>
    /// An image stored with <c>/DCTDecode</c>, i.e. a JPEG the PDF holds verbatim. The payload is a JPEG
    /// header and nothing more: the extractor is expected to recognise the signature and copy the bytes out
    /// untouched, so it never decodes this and a full image would only make the fixture bigger.
    /// </summary>
    private static byte[] BuildJpegImageDocument()
    {
        var pdf = new PdfBuilder();

        pdf.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        pdf.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] "
                    + "/Resources << /XObject << /Im1 5 0 R >> >> /Contents 4 0 R >>");
        pdf.Stream(4, null, Ascii("q 100 0 0 100 0 0 cm /Im1 Do Q"));
        pdf.Stream(5, "/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB "
                    + "/BitsPerComponent 8 /Filter /DCTDecode", JpegHeaderBytes());

        return pdf.Finish(rootObject: 1, infoObject: null);
    }

    /// <summary>
    /// Header, then wreckage. Truncated mid-object with no cross-reference table, so even lenient parsing
    /// can't recover a page — the case that must come back "couldn't read it", never "no text in it".
    /// </summary>
    private static byte[] BuildCorruptDocument()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Ascii("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type"));
        bytes.AddRange([0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x00, 0xFF]);
        return [.. bytes];
    }

    // ── Shared pieces ────────────────────────────────────────────────────────

    private static string ImageDictionary() =>
        "/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8";

    /// <summary>2×2 RGB, no <c>/Filter</c> — the decoder has to build a PNG from these bytes.</summary>
    private static byte[] RawRgbPixels() =>
    [
        0xFF, 0x00, 0x00,   0x00, 0xFF, 0x00,
        0x00, 0x00, 0xFF,   0xFF, 0xFF, 0x00,
    ];

    /// <summary>SOI + APP0/JFIF. Enough to be recognised as a JPEG by its signature.</summary>
    private static byte[] JpegHeaderBytes() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10,
        0x4A, 0x46, 0x49, 0x46, 0x00,       // "JFIF\0"
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
    ];

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>
    /// Just enough PDF writer for these fixtures: numbered objects, streams with a computed <c>/Length</c>,
    /// and the cross-reference table — whose entries are byte offsets, which is the only reason this needs to
    /// be a builder rather than a string constant.
    /// </summary>
    private sealed class PdfBuilder
    {
        private readonly List<byte> _bytes = [];
        private readonly SortedDictionary<int, int> _offsets = [];

        public PdfBuilder() => Append("%PDF-1.4\n");

        public void Object(int number, string body)
        {
            _offsets[number] = _bytes.Count;
            Append($"{number} 0 obj\n{body}\nendobj\n");
        }

        /// <param name="extraDictionaryEntries">Dictionary entries other than <c>/Length</c>, or null.</param>
        public void Stream(int number, string? extraDictionaryEntries, byte[] content)
        {
            _offsets[number] = _bytes.Count;
            var entries = extraDictionaryEntries is null
                ? $"/Length {content.Length}"
                : $"{extraDictionaryEntries} /Length {content.Length}";
            Append($"{number} 0 obj\n<< {entries} >>\nstream\n");
            _bytes.AddRange(content);
            Append("\nendstream\nendobj\n");
        }

        public byte[] Finish(int rootObject, int? infoObject)
        {
            var highest  = _offsets.Keys.Max();
            var xrefAt   = _bytes.Count;
            var entryCount = highest + 1;   // slot 0 is the mandatory free-list head

            Append($"xref\n0 {entryCount}\n");
            Append("0000000000 65535 f \n");             // each entry is exactly 20 bytes
            for (var n = 1; n <= highest; n++)
            {
                // A number with no object still needs a slot, marked free.
                Append(_offsets.TryGetValue(n, out var offset)
                    ? $"{offset:D10} 00000 n \n"
                    : "0000000000 65535 f \n");
            }

            var info = infoObject is null ? string.Empty : $" /Info {infoObject} 0 R";
            Append($"trailer\n<< /Size {entryCount} /Root {rootObject} 0 R{info} >>\n");
            Append($"startxref\n{xrefAt}\n%%EOF\n");

            return [.. _bytes];
        }

        private void Append(string s) => _bytes.AddRange(Encoding.ASCII.GetBytes(s));
    }
}
