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

    /// <summary>Title of <c>outline.pdf</c>'s first root bookmark, which points at page 1.</summary>
    public const string OutlineRootTitle = "Chapter One";

    /// <summary>Title of the bookmark nested under <see cref="OutlineRootTitle"/>; points at page 2.</summary>
    public const string OutlineChildTitle = "Nesting habits";

    /// <summary>Title of <c>outline.pdf</c>'s second root bookmark, which points at page 3.</summary>
    public const string OutlineSecondTitle = "Chapter Two";

    /// <summary><c>outline.pdf</c>'s <c>/Producer</c> — a field nothing else in the set carries.</summary>
    public const string OutlineProducer = "Nexaflow Fixture Press";

    /// <summary>
    /// Title of <c>outline.pdf</c>'s <em>grouping</em> bookmark: it has a child but no destination of its
    /// own. That is the single most common shape in a real table of contents, and the one PdfPig discards
    /// unless it is explicitly asked to keep it.
    /// </summary>
    public const string OutlineGroupTitle = "Appendices";

    /// <summary>Title of the bookmark nested under <see cref="OutlineGroupTitle"/>; points at page 3.</summary>
    public const string OutlineGroupChildTitle = "Ringing data";

    /// <summary>
    /// The raw <c>/XYZ</c> y coordinate of <see cref="OutlineChildTitle"/>'s destination, as written in the
    /// file: PDF user space, origin bottom-left. Its page is 200 points tall, so this sits 150 up from the
    /// bottom — near the top of the page.
    /// </summary>
    public const double OutlineChildDestinationY = 150;

    /// <summary>Height of every page in <c>outline.pdf</c> (<c>/MediaBox [0 0 300 200]</c>).</summary>
    public const double OutlinePageHeight = 200;

    /// <summary>
    /// The same destination expressed the way a viewer wants it — measured <em>down</em> from the top of the
    /// page. Near the top of the page therefore means a small number, the mirror image of the raw coordinate.
    /// </summary>
    public const double OutlineChildOffsetFromTop = OutlinePageHeight - OutlineChildDestinationY;

    /// <summary>Bookmark in <c>outline-named.pdf</c> reaching page 1 via a <c>/Dest (name)</c> lookup.</summary>
    public const string NamedDestTitle = "Named destination";

    /// <summary>Bookmark in <c>outline-named.pdf</c> reaching page 2 via a <c>/A /GoTo</c> action.</summary>
    public const string ActionDestTitle = "Action destination";

    /// <summary>Bookmark in <c>outline-named.pdf</c> reaching page 3 via an action naming a destination.</summary>
    public const string ActionNamedDestTitle = "Action to a named destination";

    /// <summary>First and last words of <c>two-column.pdf</c>'s left column.</summary>
    public const string LeftColumnFirst = "alpha";
    public const string LeftColumnLast  = "charlie";

    /// <summary>First and last words of <c>two-column.pdf</c>'s right column.</summary>
    public const string RightColumnFirst = "xray";
    public const string RightColumnLast  = "zulu";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Raw("text.pdf", BuildTextDocument()),
        SampleFile.Raw("image-only.pdf", BuildImageOnlyDocument()),
        SampleFile.Raw("repeated-image.pdf", BuildRepeatedImageDocument()),
        SampleFile.Raw("jpeg-image.pdf", BuildJpegImageDocument()),
        SampleFile.Raw("outline.pdf", BuildOutlineDocument()),
        SampleFile.Raw("outline-named.pdf", BuildNamedDestinationOutlineDocument()),
        SampleFile.Raw("two-column.pdf", BuildTwoColumnDocument()),
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
    /// Three pages with a real <c>/Outlines</c> tree — two roots, one of them with a child — and an
    /// <c>/Info</c> carrying the producer/creator/date fields the other fixtures leave empty.
    /// <para>
    /// The one fixture that can prove a table of contents is readable at all: every other document here has
    /// no outline, so without this the "no contents" branch would be the only one anything ever exercised.
    /// Each bookmark's destination is an explicit <c>[page /Fit]</c> array, because the page <em>number</em>
    /// is the part the panel and the AI both depend on.
    /// </para>
    /// </summary>
    private static byte[] BuildOutlineDocument()
    {
        var pdf = new PdfBuilder();

        pdf.Object(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 10 0 R >>");
        pdf.Object(2, "<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R] /Count 3 >>");

        pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] "
                    + "/Resources << /Font << /F1 9 0 R >> >> /Contents 4 0 R >>");
        pdf.Stream(4, null, Ascii("BT /F1 14 Tf 20 150 Td (first page) Tj ET"));
        pdf.Object(5, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] "
                    + "/Resources << /Font << /F1 9 0 R >> >> /Contents 6 0 R >>");
        pdf.Stream(6, null, Ascii("BT /F1 14 Tf 20 150 Td (second page) Tj ET"));
        pdf.Object(7, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] "
                    + "/Resources << /Font << /F1 9 0 R >> >> /Contents 8 0 R >>");
        pdf.Stream(8, null, Ascii("BT /F1 14 Tf 20 150 Td (third page) Tj ET"));

        pdf.Object(9, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        // /Outlines → 11 (Chapter One, p1) → 12 (Nesting habits, p2, child)
        //           ; 13 (Chapter Two, p3)
        //           ; 15 (Appendices — a GROUPING node: children but no /Dest) → 16 (Ringing data, p3)
        //
        // That last shape is the one worth having. PdfPig discards a destination-less parent unless it is
        // explicitly told to keep it, hoisting its children up in its place — so without this entry the
        // fixture could never tell a correct outline from one missing all its section headings.
        pdf.Object(10, "<< /Type /Outlines /First 11 0 R /Last 15 0 R /Count 5 >>");
        pdf.Object(11, $"<< /Title ({OutlineRootTitle}) /Parent 10 0 R /Next 13 0 R "
                     + "/First 12 0 R /Last 12 0 R /Count 1 /Dest [3 0 R /Fit] >>");
        pdf.Object(12, $"<< /Title ({OutlineChildTitle}) /Parent 11 0 R "
                     + $"/Dest [5 0 R /XYZ 0 {OutlineChildDestinationY} 0] >>");
        pdf.Object(13, $"<< /Title ({OutlineSecondTitle}) /Parent 10 0 R /Prev 11 0 R /Next 15 0 R "
                     + "/Dest [7 0 R /Fit] >>");
        pdf.Object(15, $"<< /Title ({OutlineGroupTitle}) /Parent 10 0 R /Prev 13 0 R "
                     + "/First 16 0 R /Last 16 0 R /Count 1 >>");
        pdf.Object(16, $"<< /Title ({OutlineGroupChildTitle}) /Parent 15 0 R /Dest [7 0 R /Fit] >>");

        pdf.Object(14, $"<< /Title (Field Notes) /Author (R. Hawking) /Creator (Fixture Generator) "
                     + $"/Producer ({OutlineProducer}) /CreationDate (D:20260101120000Z) "
                     + "/ModDate (D:20260202130000Z) >>");

        return pdf.Finish(rootObject: 1, infoObject: 14);
    }
    /// <summary>
    /// Three pages whose bookmarks reach their pages the way real tools write them, rather than the way the
    /// specification's simplest example does:
    /// <list type="bullet">
    /// <item>a <b>named</b> destination resolved through the catalogue's <c>/Names /Dests</c> tree — what
    /// LaTeX's hyperref emits for every section, so most academic PDFs look like this;</item>
    /// <item>a <b>GoTo action</b> (<c>/A</c>) instead of a <c>/Dest</c> — what most word processors emit;</item>
    /// <item>a named destination reached <em>through</em> an action, which is the combination of the two.</item>
    /// </list>
    /// <para>
    /// <c>outline.pdf</c> uses plain explicit destinations, which is the one form a hand-written fixture
    /// reaches for and very nearly the only form real documents don't use. A reader that resolves only those
    /// shows a table of contents with every page number missing.
    /// </para>
    /// </summary>
    private static byte[] BuildNamedDestinationOutlineDocument()
    {
        var pdf = new PdfBuilder();

        pdf.Object(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 10 0 R /Names << /Dests 20 0 R >> >>");
        pdf.Object(2, "<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R] /Count 3 >>");

        pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] "
                    + "/Resources << /Font << /F1 9 0 R >> >> /Contents 4 0 R >>");
        pdf.Stream(4, null, Ascii("BT /F1 14 Tf 20 150 Td (alpha page) Tj ET"));
        pdf.Object(5, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] "
                    + "/Resources << /Font << /F1 9 0 R >> >> /Contents 6 0 R >>");
        pdf.Stream(6, null, Ascii("BT /F1 14 Tf 20 150 Td (beta page) Tj ET"));
        pdf.Object(7, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] "
                    + "/Resources << /Font << /F1 9 0 R >> >> /Contents 8 0 R >>");
        pdf.Stream(8, null, Ascii("BT /F1 14 Tf 20 150 Td (gamma page) Tj ET"));

        pdf.Object(9, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        pdf.Object(10, "<< /Type /Outlines /First 11 0 R /Last 13 0 R /Count 3 >>");
        pdf.Object(11, $"<< /Title ({NamedDestTitle}) /Parent 10 0 R /Next 12 0 R /Dest (sec.one) >>");
        pdf.Object(12, $"<< /Title ({ActionDestTitle}) /Parent 10 0 R /Prev 11 0 R /Next 13 0 R "
                     + "/A << /S /GoTo /D [5 0 R /XYZ 0 120 0] >> >>");
        pdf.Object(13, $"<< /Title ({ActionNamedDestTitle}) /Parent 10 0 R /Prev 12 0 R "
                     + "/A << /S /GoTo /D (sec.three) >> >>");

        // The /Dests name tree: sorted /Names pairs of (name, destination array).
        pdf.Object(20, "<< /Names [(sec.one) [3 0 R /XYZ 0 180 0] (sec.three) [7 0 R /Fit]] >>");

        return pdf.Finish(rootObject: 1, infoObject: null);
    }

    /// <summary>
    /// One page laid out in two columns, whose content stream deliberately writes them <em>interleaved</em>:
    /// first line of the left column, first line of the right, second line of the left, and so on.
    /// <para>
    /// That is what a real two-column document does often enough to matter, and it is the case where reading
    /// the content stream in order produces confident nonsense rather than anything that looks broken. Only
    /// laying the words out spatially recovers the columns.
    /// </para>
    /// <para>
    /// Full lines of several words each, not one word per line: spatial segmentation works by estimating the
    /// usual within-line and between-line distances from the words themselves, so a page of six isolated
    /// words gives it nothing to estimate from and it clusters them arbitrarily. A fixture too sparse to
    /// segment would be testing the fixture, not the code.
    /// </para>
    /// </summary>
    private static byte[] BuildTwoColumnDocument()
    {
        var pdf = new PdfBuilder();

        pdf.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        pdf.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 520 260] "
                    + "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>");

        string[] left =
        [
            $"{LeftColumnFirst} opens the left column",
            "with several words per line",
            "so the usual spacing can be",
            "estimated the way the",
            $"algorithm expects {LeftColumnLast}",
        ];
        string[] right =
        [
            $"{RightColumnFirst} opens the right column",
            "and also runs to several",
            "words across each of its",
            "lines so the gutter between",
            $"them is the widest gap {RightColumnLast}",
        ];

        // Left column at x=40, right at x=290. At 10pt a left line reaches about x=175, so the gutter is far
        // wider than any gap between words — which is the signal the segmentation reads.
        var content = new StringBuilder();
        for (var i = 0; i < left.Length; i++)
        {
            var y = 220 - i * 20;
            content.Append($"BT /F1 10 Tf 40 {y} Td ({left[i]}) Tj ET\n");
            content.Append($"BT /F1 10 Tf 290 {y} Td ({right[i]}) Tj ET\n");
        }

        pdf.Stream(4, null, Ascii(content.ToString()));
        pdf.Object(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

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
