using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Checks that every snaplink in a tree still points at something real: the file exists, the markdown
/// heading path is still there, the class/method is still declared, the URL is well formed.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately conservative — it reports only what it can <em>prove</em> is broken. A file whose extension
/// has no tree-sitter grammar (<c>.xaml</c>, <c>.txt</c>) has no verifiable class/method structure, and a
/// file the parser yields nothing for may simply use a construct the grammar misses; both count as
/// <em>unverifiable</em>, not broken. A false positive here fails a release build, so the bar is proof.
/// </para>
/// <para>
/// Snaplinks hang off two places — the node itself, and each of the node's concern links — and both are
/// walked. Results are sorted so the report diffs cleanly between runs.
/// </para>
/// </remarks>
public sealed class SnaplinkValidator(string productRoot)
{
    /// <summary>
    /// What we know about one referenced file, computed once per run. The tree-sitter parse is <b>lazy</b>:
    /// most snaplinks only need the file to exist (whole-file links, markdown), and parsing every referenced
    /// source file up front dominated the scan.
    /// </summary>
    private sealed class FileFacts(bool exists, string? text, string? fullPath)
    {
        private CodeOutline? _outline;
        private bool _parsed;

        public bool Exists => exists;
        public string? Text => text;

        /// <summary>The outline, parsed on first request. Null ⇒ unverifiable (no grammar / unreadable).</summary>
        public CodeOutline? Outline()
        {
            if (_parsed) return _outline;
            _parsed = true;
            if (text is not null && fullPath is not null && !SnaplinkTargets.IsMarkdown(fullPath))
                _outline = SnaplinkTargets.Outline(fullPath, text, Path.GetDirectoryName(fullPath));
            return _outline;
        }
    }

    private readonly Dictionary<string, FileFacts> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Validates every snaplink in <paramref name="state"/> against the files under <paramref name="productRoot"/>.</summary>
    public static IntegrityReport Validate(ProductState state, string productRoot)
        => new SnaplinkValidator(productRoot).Run(state);

    /// <summary>
    /// Re-checks a <em>single</em> link. The Integrity page uses this after an inline edit so a fix is
    /// confirmed in milliseconds rather than forcing another full-tree scan (which parses every referenced
    /// source file and takes seconds). Returns a null detail when the link is sound — or unverifiable.
    /// </summary>
    public static (IntegrityKind Kind, string? Detail) CheckLink(Snaplink link, string productRoot)
        => new SnaplinkValidator(productRoot).Check(link);

    private IntegrityReport Run(ProductState state)
    {
        var report = new IntegrityReport
        {
            Generated = DateTime.Now.ToString("o"),
            ScannedNodes = state.Nodes.Count
        };

        foreach (var (id, node) in state.Nodes)
        {
            Scan(node.Snaplinks, id, node.Title, concern: null, report);
            foreach (var link in node.Concerns ?? [])
                Scan(link.Snaplinks, id, node.Title, link.Tag, report);
        }

        FlagUnbackedConcerns(state, report);

        report.Issues = [.. report.Issues
            .OrderBy(i => i.NodeId, StringComparer.Ordinal)
            .ThenBy(i => i.Concern ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(i => i.Index)];
        return report;
    }

    /// <summary>
    /// Flags concern links that reached a terminal status (<c>done</c>/<c>faulted</c>) with no snaplink,
    /// where the concern def demands one (<see cref="ConcernDef.RequiresSnaplink"/>). Provable from the
    /// tree alone — no file I/O — so it stays within the "only report what we can prove broken" bar.
    /// </summary>
    private static void FlagUnbackedConcerns(ProductState state, IntegrityReport report)
    {
        var mustLink = state.Product.Concerns
            .Where(c => c.RequiresSnaplink)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (mustLink.Count == 0) return;

        foreach (var (id, node) in state.Nodes)
            foreach (var link in node.Concerns ?? [])
                if (mustLink.Contains(link.Tag)
                    && link.Status is Status.Done or Status.Faulted
                    && (link.Snaplinks is null || link.Snaplinks.Count == 0))
                    report.Issues.Add(new IntegrityIssue
                    {
                        NodeId = id, NodeTitle = node.Title, Concern = link.Tag, Index = -1,
                        Kind = IntegrityKind.MissingSnaplink,
                        Detail = $"concern '{link.Tag}' is {link.Status.ToString().ToLowerInvariant()} but has no snaplink",
                        Link = new Snaplink()
                    });
    }

    private void Scan(List<Snaplink>? links, string nodeId, string title, string? concern, IntegrityReport report)
    {
        if (links is null) return;
        for (var i = 0; i < links.Count; i++)
        {
            report.ScannedSnaplinks++;
            var (kind, detail) = Check(links[i]);
            if (detail is null) continue;   // sound, or unverifiable — never reported
            report.Issues.Add(new IntegrityIssue
            {
                NodeId = nodeId, NodeTitle = title, Concern = concern, Index = i,
                Kind = kind, Detail = detail, Link = links[i]
            });
        }
    }

    /// <summary>The failure for this link, or a null detail when it is sound (or unverifiable).</summary>
    private (IntegrityKind Kind, string? Detail) Check(Snaplink link)
    {
        if (link.Type == "url")
        {
            if (string.IsNullOrWhiteSpace(link.Target))
                return (IntegrityKind.EmptyTarget, "url snaplink has no target");
            return Uri.TryCreate(link.Target, UriKind.Absolute, out _)
                ? (default, null)
                : (IntegrityKind.InvalidUrl, $"'{link.Target}' is not a well-formed absolute URL");
        }

        if (string.IsNullOrWhiteSpace(link.Doc))
            return (IntegrityKind.MissingDoc, $"{link.Type} snaplink has no doc path");

        var facts = Facts(link.Doc!);
        if (!facts.Exists) return (IntegrityKind.MissingFile, $"file not found: {link.Doc}");
        if (facts.Text is null) return (default, null);   // exists but unreadable/huge → unverifiable

        if (link.Type == "markdown")
            return link.TitlePath is { Count: > 0 } path && !SnaplinkTargets.HasHeadingPath(facts.Text, path)
                ? (IntegrityKind.MissingHeading, $"heading '{string.Join(" › ", path)}' not found in {link.Doc}")
                : (default, null);

        // code (and any other file-backed type): only structure we can actually resolve is checked.
        var wantsStructure = !string.IsNullOrWhiteSpace(link.Class) || !string.IsNullOrWhiteSpace(link.Method);
        if (!wantsStructure) return (default, null);            // whole-file link → existence is enough; no parse
        if (facts.Outline() is not { HasContent: true } outline) return (default, null);   // unverifiable

        if (!string.IsNullOrWhiteSpace(link.Class))
        {
            var type = outline.Types.FirstOrDefault(t => string.Equals(t.Name, link.Class, StringComparison.Ordinal));
            if (type is null)
                return (IntegrityKind.MissingClass, $"class '{link.Class}' is not declared in {link.Doc}");

            if (!string.IsNullOrWhiteSpace(link.Method)
                && !type.Members.Any(m => string.Equals(m.Name, link.Method, StringComparison.Ordinal)))
                return (IntegrityKind.MissingMethod, $"method '{link.Class}.{link.Method}' is not declared in {link.Doc}");

            return (default, null);
        }

        // Method without a class → a top-level function.
        return outline.TopLevel.Any(m => string.Equals(m.Name, link.Method, StringComparison.Ordinal))
            ? (default, null)
            : (IntegrityKind.MissingMethod, $"top-level '{link.Method}' is not declared in {link.Doc}");
    }

    private FileFacts Facts(string doc)
    {
        if (_cache.TryGetValue(doc, out var cached)) return cached;

        var full = Path.IsPathRooted(doc) ? doc : Path.Combine(productRoot, doc);
        var facts = File.Exists(full)
            ? new FileFacts(true, SnaplinkTargets.ReadText(full), full)
            : new FileFacts(false, null, null);

        _cache[doc] = facts;
        return facts;
    }
}
