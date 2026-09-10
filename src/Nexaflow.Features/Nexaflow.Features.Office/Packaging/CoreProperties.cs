using System.Xml;
using Nexaflow.Features.Office.Search;

namespace Nexaflow.Features.Office.Packaging;

/// <summary>
/// A package's core properties (<c>docProps/core.xml</c>): the title, subject, author, keywords and
/// description every Office format shares.
/// <para>
/// Searched alongside the body for the same two reasons the PDF extractor includes its metadata: a document
/// should be findable by its title even when the body never states its subject, and the Windows index matches
/// these properties too — leaving them out would have the verifier strike through a row the index rightly
/// returned.
/// </para>
/// </summary>
internal static class CoreProperties
{
    private const string DublinCore = "http://purl.org/dc/elements/1.1/";
    private const string Core       = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    /// <summary>Appends each searchable property on its own line. False once the budget is spent.</summary>
    public static bool Append(OpcPackage package, TextBudget budget, CancellationToken ct)
    {
        var parts = package.Related(sourcePart: null, "/core-properties");
        if (parts.Count == 0) return !budget.IsFull;

        using var stream = package.OpenPart(parts[0]);
        if (stream is null) return !budget.IsFull;

        using var reader = OpcPackage.CreateReader(stream);
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || !IsSearchable(reader)) continue;

            if (!budget.AppendElementText(reader) || !budget.EndLine()) return false;
        }
        return !budget.IsFull;
    }

    // Dates, revision counts and the last-modified-by name are not what anyone means by the document.
    private static bool IsSearchable(XmlReader reader) => reader.NamespaceURI switch
    {
        DublinCore => reader.LocalName is "title" or "subject" or "creator" or "description",
        Core       => reader.LocalName is "keywords",
        _          => false,
    };
}
