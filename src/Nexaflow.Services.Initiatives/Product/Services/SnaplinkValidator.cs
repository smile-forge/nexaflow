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
public sealed class SnaplinkValidator
{
    private readonly string[] _fileRoots;

    /// <summary>Validates against the files under <paramref name="productRoot"/>.</summary>
    public SnaplinkValidator(string productRoot) : this(productRoot, null) { }

    /// <summary>
    /// Validates against <paramref name="fileRoots"/> in order, falling back to <paramref name="productRoot"/>.
    /// This matters in a git worktree: the tree itself lives only in the main checkout, so the product root
    /// resolves there — but the <em>code</em> a snaplink names is the copy on the branch you're editing. Passing
    /// the caller's working tree first makes "does this link resolve?" answer about the code in front of you,
    /// which is what <c>describe --code</c> already does. The installer gate is unaffected: there the working
    /// tree and the product root are the same directory.
    /// </summary>
    public SnaplinkValidator(string productRoot, IEnumerable<string>? fileRoots)
    {
        this.productRoot = productRoot;
        _fileRoots = [.. (fileRoots ?? []).Append(productRoot)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private readonly string productRoot;

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

    /// <summary>The product root's linked worktrees, read once (file reads only) on first use.</summary>
    private IReadOnlyList<string>? _worktrees;
    private IReadOnlyList<string> Worktrees => _worktrees ??= GitWorktrees.Roots(productRoot);

    /// <summary>Every node id in the tree, for validating <c>node</c> snaplink targets. Null in the
    /// single-link <see cref="CheckLink(Snaplink, string)"/> path (no tree in scope) — a node link is then
    /// treated as unverifiable, deferred to the next full scan, which stays true to the "prove it broken" bar.</summary>
    private IReadOnlySet<string>? _nodeIds;

    /// <summary>Validates every snaplink in <paramref name="state"/> against the files under
    /// <paramref name="productRoot"/>, or — when given — <paramref name="fileRoots"/> first.</summary>
    public static IntegrityReport Validate(ProductState state, string productRoot, IEnumerable<string>? fileRoots = null)
        => new SnaplinkValidator(productRoot, fileRoots).Run(state);

    /// <summary>
    /// Re-checks a <em>single</em> link. The Integrity page uses this after an inline edit so a fix is
    /// confirmed in milliseconds rather than forcing another full-tree scan (which parses every referenced
    /// source file and takes seconds). Returns a null detail when the link is sound — or unverifiable.
    /// </summary>
    public static (IntegrityKind Kind, string? Detail) CheckLink(Snaplink link, string productRoot)
        => new SnaplinkValidator(productRoot).Check(link);

    /// <summary>
    /// Single-link recheck that can also verify a <c>node</c> link, given the set of live node ids. The
    /// Integrity page uses this after editing a node snaplink so the fix confirms without a full rescan.
    /// </summary>
    public static (IntegrityKind Kind, string? Detail) CheckLink(Snaplink link, string productRoot, IReadOnlySet<string> nodeIds)
    {
        var validator = new SnaplinkValidator(productRoot) { _nodeIds = nodeIds };
        return validator.Check(link);
    }

    private IntegrityReport Run(ProductState state)
    {
        var report = new IntegrityReport
        {
            Generated = DateTime.Now.ToString("o"),
            ScannedNodes = state.Nodes.Count
        };

        _nodeIds = state.Nodes.Keys.ToHashSet(StringComparer.Ordinal);

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

        if (link.Type == "node")
        {
            if (string.IsNullOrWhiteSpace(link.Target))
                return (IntegrityKind.EmptyTarget, "node snaplink has no target node id");
            if (_nodeIds is null) return (default, null);   // no tree in scope → unverifiable, not broken
            return _nodeIds.Contains(link.Target)
                ? (default, null)
                : (IntegrityKind.MissingNode, $"node '{link.Target}' is not in the tree");
        }

        if (string.IsNullOrWhiteSpace(link.Doc))
            return (IntegrityKind.MissingDoc, $"{link.Type} snaplink has no doc path");

        // A doc inside a linked worktree resolves today and dies with that branch — broken on arrival, and
        // checked before existence so the message names the real fix rather than "file not found".
        if (GitWorktrees.TryReRoot(link.Doc!, productRoot, Worktrees, out var reRooted))
            return (IntegrityKind.WorktreePath,
                    $"'{link.Doc}' points into a linked git worktree — use the repo path: {reRooted}");

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

        // First root that actually has the file wins; if none does, resolve against the product root so the
        // "not found" message names the canonical location.
        var full = Path.IsPathRooted(doc)
            ? doc
            : _fileRoots.Select(r => Path.Combine(r, doc)).FirstOrDefault(File.Exists)
              ?? Path.Combine(productRoot, doc);
        var facts = File.Exists(full)
            ? new FileFacts(true, SnaplinkTargets.ReadText(full), full)
            : new FileFacts(false, null, null);

        _cache[doc] = facts;
        return facts;
    }
}
