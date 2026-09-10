using System.IO;
using System.IO.Compression;
using System.Xml;

namespace Nexaflow.Features.Office.Packaging;

/// <summary>
/// An Office Open XML package (ECMA-376 Part 2, "OPC"): a zip whose parts find each other through relationship
/// files rather than through fixed paths. Word, Excel and PowerPoint all sit on this, so it is the one layer
/// every OOXML format — and, later, image/chart extraction — shares.
/// <para>
/// Parts are streamed straight out of the zip. Nothing is extracted to disk, and nothing is read that a
/// caller doesn't ask for: opening reads only the central directory.
/// </para>
/// <para>
/// Part names are held without the leading slash (<c>word/document.xml</c>) and compared case-insensitively,
/// as OPC requires — a producer that writes a relationship target in a different case from its zip entry
/// still resolves.
/// </para>
/// </summary>
internal sealed class OpcPackage : IDisposable
{
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _parts = new(StringComparer.OrdinalIgnoreCase);

    private OpcPackage(ZipArchive zip)
    {
        _zip = zip;
        foreach (var entry in zip.Entries)
            _parts.TryAdd(entry.FullName.Replace('\\', '/').TrimStart('/'), entry);
    }

    /// <summary>
    /// Opens <paramref name="stream"/> as a package, or returns null when it isn't a zip at all — which is
    /// what a password-protected Office file is (an OLE compound file wrapping the encrypted package), and
    /// what a renamed legacy <c>.doc</c> or a <c>~$</c> owner file is. The stream stays the caller's.
    /// <para>
    /// A non-seekable stream is declined rather than opened: <see cref="ZipArchive"/> would copy the whole of
    /// it into memory first, which is exactly what a search read must never do.
    /// </para>
    /// </summary>
    public static OpcPackage? TryOpen(Stream stream)
    {
        if (!stream.CanSeek) return null;

        try { return new OpcPackage(new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true)); }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>
    /// The package's main part (<c>word/document.xml</c>, <c>xl/workbook.xml</c>, …), found the way a
    /// consumer is meant to find it — through the package relationship — rather than assumed. Matching the
    /// relationship type by its last segment covers both the Transitional and the Strict spelling.
    /// </summary>
    public string? MainPart()
    {
        var main = Related(sourcePart: null, "/officeDocument");
        return main.Count > 0 && _parts.ContainsKey(main[0]) ? main[0] : null;
    }

    /// <summary>
    /// The parts <paramref name="sourcePart"/> (null = the package itself) relates to by a relationship whose
    /// type ends with <paramref name="typeSuffix"/> — <c>"/header"</c>, <c>"/footnotes"</c>,
    /// <c>"/core-properties"</c>. In relationship-file order, each once; external targets (hyperlinks, linked
    /// files) are not parts and are skipped.
    /// </summary>
    public IReadOnlyList<string> Related(string? sourcePart, string typeSuffix)
    {
        using var rels = OpenPart(RelationshipsPartOf(sourcePart));
        if (rels is null) return [];

        var baseDirectory = sourcePart is null ? string.Empty : DirectoryOf(sourcePart);
        var found = new List<string>();

        using var reader = CreateReader(rels);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship") continue;
            if (string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                continue;

            var type   = reader.GetAttribute("Type");
            var target = reader.GetAttribute("Target");
            if (type is null || target is null || !type.EndsWith(typeSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (Resolve(baseDirectory, target) is { } part && !found.Contains(part, StringComparer.OrdinalIgnoreCase))
                found.Add(part);
        }
        return found;
    }

    /// <summary>A forward-only stream over <paramref name="partName"/>, or null when the package has no such
    /// part. The caller owns the stream.</summary>
    public Stream? OpenPart(string partName)
        => _parts.TryGetValue(partName, out var entry) ? entry.Open() : null;

    /// <summary>
    /// An <see cref="XmlReader"/> over a part, configured the one way every part is read: no DTDs and no
    /// resolver (a document is untrusted input), comments and processing instructions dropped, and
    /// whitespace <b>kept</b> — a space-only text run is the gap between two words.
    /// </summary>
    public static XmlReader CreateReader(Stream part) => XmlReader.Create(part, new XmlReaderSettings
    {
        DtdProcessing                = DtdProcessing.Prohibit,
        XmlResolver                  = null,
        IgnoreComments               = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace             = false,
        CloseInput                   = false,
    });

    public void Dispose() => _zip.Dispose();

    // "_rels/.rels" for the package itself; "word/_rels/document.xml.rels" for word/document.xml.
    private static string RelationshipsPartOf(string? sourcePart)
    {
        if (sourcePart is null) return "_rels/.rels";

        var slash = sourcePart.LastIndexOf('/');
        return $"{sourcePart[..(slash + 1)]}_rels/{sourcePart[(slash + 1)..]}.rels";
    }

    private static string DirectoryOf(string part)
    {
        var slash = part.LastIndexOf('/');
        return slash < 0 ? string.Empty : part[..(slash + 1)];
    }

    // A target is a URI: relative to the source part's folder unless it starts with '/', free to climb with
    // "..", and percent-encoded. Resolved here by segment rather than through System.Uri, which wants a base
    // URI a zip entry doesn't have.
    private static string? Resolve(string baseDirectory, string target)
    {
        var path = Uri.UnescapeDataString(target);
        var hash = path.IndexOf('#');
        if (hash >= 0) path = path[..hash];

        var combined = path.StartsWith('/') ? path : baseDirectory + path;

        var segments = new List<string>();
        foreach (var segment in combined.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return segments.Count == 0 ? null : string.Join('/', segments);
    }
}
