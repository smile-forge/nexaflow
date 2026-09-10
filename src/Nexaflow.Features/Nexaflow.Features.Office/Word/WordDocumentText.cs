using System.Xml;
using Nexaflow.Features.Office.Packaging;
using Nexaflow.Features.Office.Search;

namespace Nexaflow.Features.Office.Word;

/// <summary>
/// The searchable text of a WordprocessingML document (<c>.docx</c>, <c>.docm</c>, <c>.dotx</c>,
/// <c>.dotm</c>): its core properties, the body, then footnotes, endnotes, comments, headers and footers.
/// <para>
/// Search matches whole words, and WordprocessingML fights that in two opposite ways, so the whole job is
/// getting word boundaries right:
/// <list type="bullet">
/// <item>Word splits a single word across runs whenever formatting, spell-check state or a revision id
/// changes mid-word — "George" is routinely <c>&lt;w:t&gt;Geo&lt;/w:t&gt;…&lt;w:t&gt;rge&lt;/w:t&gt;</c>. Runs are
/// therefore joined with <b>no</b> separator.</item>
/// <item>Paragraphs, table cells and list items carry no whitespace between them at all. Each paragraph end
/// (and each line break) therefore becomes a newline, or the last word of one cell would fuse with the first
/// word of the next.</item>
/// </list>
/// </para>
/// <para>
/// Text is what the document says once its tracked changes are accepted: deleted text (<c>w:delText</c>) and
/// the old side of a move (<c>w:moveFrom</c>) are left out, as are field codes (<c>w:instrText</c> — the
/// <c>HYPERLINK "…"</c> behind a link, not the words shown). Both Transitional and Strict (ISO 29500)
/// namespaces are understood.
/// </para>
/// </summary>
internal static class WordDocumentText
{
    private const string Wordprocessing       = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordprocessingStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string OfficeMath           = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string OfficeMathStrict     = "http://purl.oclc.org/ooxml/officeDocument/math";
    private const string MarkupCompatibility  = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>Parts related to the main document that hold the document's own words, in reading order.</summary>
    private static readonly string[] SideParts = ["/footnotes", "/endnotes", "/comments", "/header", "/footer"];

    /// <summary>
    /// Subtrees that never hold the document's text. The property containers are formatting only — and must be
    /// skipped rather than merely ignored, because <c>w:tabs</c> inside <c>w:pPr</c> lists <c>w:tab</c> elements
    /// that are tab <em>stops</em>, not tab characters. <c>w:moveFrom</c> is the old side of a tracked move,
    /// whose words also appear, as they now stand, under <c>w:moveTo</c>.
    /// </summary>
    private static readonly HashSet<string> Skipped =
        ["pPr", "rPr", "sectPr", "tblPr", "tblPrEx", "trPr", "tcPr", "tblGrid", "sdtPr", "sdtEndPr", "moveFrom"];

    /// <summary>
    /// Reads <paramref name="package"/>'s text into <paramref name="budget"/>, stopping early once the budget
    /// is spent. False when the package is not a Word document at all — no main part, or a main part that
    /// isn't <c>w:document</c> (a spreadsheet renamed <c>.docx</c>) — which the caller must report as
    /// "couldn't tell", never as "no text".
    /// </summary>
    public static bool Read(OpcPackage package, TextBudget budget, CancellationToken ct)
    {
        if (package.MainPart() is not { } main || !IsWordprocessingDocument(package, main)) return false;

        if (!CoreProperties.Append(package, budget, ct)) return true;
        if (!AppendPart(package, main, budget, ct)) return true;

        foreach (var type in SideParts)
            foreach (var part in package.Related(main, type))
                if (!AppendPart(package, part, budget, ct)) return true;

        return true;
    }

    // Checked before anything is read, so a package that isn't Word never produces text that would then be
    // trusted as a Word document's — an xlsx's main part holds no w:t, and reading it would confidently
    // report "no text".
    private static bool IsWordprocessingDocument(OpcPackage package, string main)
    {
        using var stream = package.OpenPart(main);
        if (stream is null) return false;

        using var reader = OpcPackage.CreateReader(stream);
        return reader.MoveToContent() == XmlNodeType.Element
               && reader.LocalName == "document"
               && IsWordprocessing(reader.NamespaceURI);
    }

    /// <summary>Appends one part's text. False once the budget is spent.</summary>
    private static bool AppendPart(OpcPackage package, string part, TextBudget budget, CancellationToken ct)
    {
        using var stream = package.OpenPart(part);
        if (stream is null) return !budget.IsFull;          // a dangling relationship: nothing there to read

        using var reader = OpcPackage.CreateReader(stream);

        // One entry per open mc:AlternateContent: whether one of its Choices has been read yet.
        var alternates = new Stack<bool>();

        var more = reader.Read();
        while (more)
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element && ShouldSkip(reader, alternates))
            {
                // Skip lands on the node after the subtree, which the next pass must handle, not read past.
                reader.Skip();
                more = !reader.EOF;
                continue;
            }

            if (!Visit(reader, budget, alternates)) return false;
            more = reader.Read();
        }
        return !budget.IsFull;
    }

    private static bool ShouldSkip(XmlReader reader, Stack<bool> alternates)
    {
        if (IsWordprocessing(reader.NamespaceURI)) return Skipped.Contains(reader.LocalName);

        // A DrawingML text box is written twice: as an mc:Choice for current Word and again, in VML, as the
        // mc:Fallback for Word 2007. Reading both would double every word in it. The Fallback is dropped only
        // when a Choice came first, so a producer that wrote nothing but a Fallback is still read.
        return reader.NamespaceURI == MarkupCompatibility
               && reader.LocalName == "Fallback"
               && alternates.Count > 0 && alternates.Peek();
    }

    /// <summary>Applies one node's contribution to the text. False once the budget is spent.</summary>
    private static bool Visit(XmlReader reader, TextBudget budget, Stack<bool> alternates)
    {
        var ns = reader.NamespaceURI;
        switch (reader.NodeType)
        {
            case XmlNodeType.Element when ns == MarkupCompatibility:
                if (reader.LocalName == "AlternateContent" && !reader.IsEmptyElement)
                    alternates.Push(false);
                else if (reader.LocalName == "Choice" && alternates.Count > 0)
                {
                    alternates.Pop();
                    alternates.Push(true);
                }
                return true;

            case XmlNodeType.EndElement when ns == MarkupCompatibility:
                if (reader.LocalName == "AlternateContent" && alternates.Count > 0) alternates.Pop();
                return true;

            case XmlNodeType.Element when reader.LocalName == "t" && (IsWordprocessing(ns) || IsOfficeMath(ns)):
                return budget.AppendElementText(reader);

            case XmlNodeType.Element when IsWordprocessing(ns):
                return reader.LocalName switch
                {
                    "tab" or "ptab"                => budget.Append('\t'),
                    "br" or "cr"                   => budget.EndLine(),
                    "noBreakHyphen"                => budget.Append('-'),
                    "p" when reader.IsEmptyElement => budget.EndLine(),
                    _                              => true,  // softHyphen, delText, instrText, … add nothing
                };

            case XmlNodeType.EndElement when IsWordprocessing(ns) && reader.LocalName == "p":
                return budget.EndLine();

            default:
                return true;
        }
    }

    private static bool IsWordprocessing(string ns) => ns is Wordprocessing or WordprocessingStrict;

    private static bool IsOfficeMath(string ns) => ns is OfficeMath or OfficeMathStrict;
}
