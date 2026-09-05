using System.Text.RegularExpressions;
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
        => new SnaplinkValidator(productRoot, fileRoots).Run(state, manifest: null);

    /// <summary>
    /// Validates as above and additionally gates the test-coverage declarations in <paramref name="manifest"/>
    /// against the tree. Split from the parameterless overload because the manifest is <em>derived</em> state
    /// (<c>scan-tests</c> writes it, it is gitignored): a caller without one — a clean CI checkout, the
    /// single-link recheck — gets the snaplink checks alone rather than a false all-clear on coverage.
    /// </summary>
    public static IntegrityReport Validate(ProductState state, string productRoot, IEnumerable<string>? fileRoots, TestCoverageManifest? manifest)
        => new SnaplinkValidator(productRoot, fileRoots).Run(state, manifest);

    /// <summary>
    /// Re-checks a <em>single</em> link. The Integrity page uses this after an inline edit so a fix is
    /// confirmed in milliseconds rather than forcing another full-tree scan (which parses every referenced
    /// source file and takes seconds). The verdict distinguishes a link proven good from one nothing could be
    /// proven about — the caller is telling a user their edit worked, and only the first earns that.
    /// </summary>
    public static LinkCheck CheckLink(Snaplink link, string productRoot)
        => new SnaplinkValidator(productRoot).Check(link);

    /// <summary>
    /// Single-link recheck that can also verify a <c>node</c> link, given the set of live node ids. The
    /// Integrity page uses this after editing a node snaplink so the fix confirms without a full rescan.
    /// </summary>
    public static LinkCheck CheckLink(Snaplink link, string productRoot, IReadOnlySet<string> nodeIds)
    {
        var validator = new SnaplinkValidator(productRoot) { _nodeIds = nodeIds };
        return validator.Check(link);
    }

    /// <summary>
    /// Single-link recheck resolved against <paramref name="fileRoots"/> — the caller's working tree — rather
    /// than the product root alone. What a write-time gate needs: a branch that has moved a file away must be
    /// told its link is broken, and resolving through the main checkout is exactly what would hide that.
    /// </summary>
    public static LinkCheck CheckLink(Snaplink link, string productRoot, IEnumerable<string>? fileRoots,
                                      IReadOnlySet<string>? nodeIds)
    {
        var validator = new SnaplinkValidator(productRoot, fileRoots) { _nodeIds = nodeIds };
        return validator.Check(link);
    }

    private IntegrityReport Run(ProductState state, TestCoverageManifest? manifest)
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
        FlagStaleCoverageNodes(state, manifest, report);
        FlagUnlinkedProjects(state, report);

        report.Issues = [.. report.Issues
            .OrderBy(i => i.NodeId, StringComparer.Ordinal)
            .ThenBy(i => i.Concern ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(i => i.Index)];
        report.Advisories = [.. report.Advisories
            .OrderBy(a => a.NodeId, StringComparer.Ordinal)
            .ThenBy(a => a.Concern ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(a => a.Index)];
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

    /// <summary>
    /// Flags <c>[CoversNode("id")]</c> declarations naming an id the tree no longer has. The tree is the
    /// authority on what exists, so a declaration pointing outside it is provably rotten — a rename or a
    /// delete left the attribute behind, and the test now claims to back nothing.
    /// <para>
    /// Deliberately only the *unknown id* half of the reconciliation. Its sibling —
    /// <see cref="CoverageAdvisoryKind.DeclaredButUnlinked"/>, a live node with no <c>tests</c> snaplink back —
    /// stays a non-gating advisory: that is a gap in the tree's bookkeeping, not proof of breakage, and it is
    /// resolved by the Integrity page's *Add link* rather than by failing a release.
    /// </para>
    /// <para>
    /// No manifest (a clean CI checkout, where <c>.product/test-coverage.json</c> is gitignored and absent)
    /// means nothing is claimed, so nothing can be disproved — skip, keeping the "only report what we can
    /// prove broken" bar. The Roslyn analyzer NXCOV002 catches the same mistake at author time; this is the
    /// build-time backstop for anyone who never compiled with the analyzer.
    /// </para>
    /// </summary>
    private static void FlagStaleCoverageNodes(ProductState state, TestCoverageManifest? manifest, IntegrityReport report)
    {
        if (manifest is null) return;

        foreach (var advisory in TestCoverageReconciler.Reconcile(state, manifest).Advisories)
        {
            if (advisory.Kind != CoverageAdvisoryKind.UnknownNode) continue;

            var where = advisory.Method is { Length: > 0 } m ? $"{advisory.Class}.{m}" : advisory.Class;
            report.Issues.Add(new IntegrityIssue
            {
                NodeId = advisory.NodeId,
                NodeTitle = string.Empty,   // there is no node — that is the finding
                Concern = TestCoverageReconciler.TestsConcern,
                Index = -1,
                Kind = IntegrityKind.StaleCoverageNode,
                Detail = $"test '{where}' declares [CoversNode(\"{advisory.NodeId}\")] but no such node is in the tree"
                       + (advisory.File is { Length: > 0 } f ? $" ({f})" : string.Empty),
                Link = new Snaplink()
            });
        }
    }

    /// <summary>Family node id → the <c>src</c> subdirectory whose assemblies it is the inventory for.</summary>
    private static readonly (string Node, string SubDir)[] ProjectFamilies =
        [("features", "Nexaflow.Features"), ("providers", "Nexaflow.Providers")];

    /// <summary>
    /// Flags a feature/provider assembly on disk that no node under its family links by <c>.csproj</c> — the
    /// reverse direction of the usual check: not "does this link still resolve?" but "is this shipping thing
    /// in the tree at all?". That is the failure that let GraphViewer ship with no node.
    /// <para>
    /// Two assembly shapes are deliberately exempt, matching the <c>ProductTreeCoverageTests</c> guard this
    /// mirrors: <c>*.Common</c> (shared contracts — not a feature) and any name carrying a further dot, e.g.
    /// <c>Compressed.Modern</c> (a codec backend behind a feature rather than a feature itself). Both are
    /// implementation detail of a node that already exists, so demanding their own node would force noise
    /// nodes into the tree.
    /// </para>
    /// <para>
    /// Only direct children of the family node count as linkers, so an assembly cannot be "covered" by a
    /// snaplink buried on some unrelated leaf. Skipped entirely when the family node or its source directory
    /// is absent — nothing to compare, so nothing is provable.
    /// </para>
    /// </summary>
    private void FlagUnlinkedProjects(ProductState state, IntegrityReport report)
    {
        var root = _fileRoots.FirstOrDefault(r => Directory.Exists(Path.Combine(r, "src")));
        if (root is null) return;

        foreach (var (familyId, subDir) in ProjectFamilies)
        {
            var srcDir = Path.Combine(root, "src", subDir);
            if (!Directory.Exists(srcDir)) continue;
            if (!state.Nodes.TryGetValue(familyId, out var family)) continue;

            var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var childId in family.Children ?? [])
                if (state.Nodes.TryGetValue(childId, out var child))
                    foreach (var link in child.Snaplinks ?? [])
                        if (link.Doc is { } doc && doc.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                            linked.Add(doc.Replace('\\', '/').Trim('/'));

            foreach (var dir in Directory.GetDirectories(srcDir, subDir + ".*"))
            {
                var asm = Path.GetFileName(dir);
                // A removed assembly leaves its gitignored bin/obj behind; only a real .csproj ships.
                if (!File.Exists(Path.Combine(dir, asm + ".csproj"))) continue;

                var shortName = asm[(subDir.Length + 1)..];
                if (shortName is "Common" || shortName.Contains('.')) continue;

                if (linked.Contains($"src/{subDir}/{asm}/{asm}.csproj")) continue;

                report.Issues.Add(new IntegrityIssue
                {
                    NodeId = familyId, NodeTitle = family.Title, Concern = null, Index = -1,
                    Kind = IntegrityKind.UnlinkedProject,
                    Detail = $"assembly '{asm}' ships but no node under '{familyId}' links "
                           + $"src/{subDir}/{asm}/{asm}.csproj — it is untracked in the product tree",
                    Link = new Snaplink()
                });
            }
        }
    }

    private void Scan(List<Snaplink>? links, string nodeId, string title, string? concern, IntegrityReport report)
    {
        if (links is null) return;
        for (var i = 0; i < links.Count; i++)
        {
            report.ScannedSnaplinks++;
            var check = Check(links[i]);
            if (check.Detail is null)
            {
                // Sound at the level that gates a build. Its finer ast target may still be wrong, which is a
                // suggestion rather than breakage — the field has never been checked, so it holds free text.
                if (CheckAst(links[i]) is { } advisory)
                {
                    advisory.NodeId = nodeId;
                    advisory.NodeTitle = title;
                    advisory.Concern = concern;
                    advisory.Index = i;
                    report.Advisories.Add(advisory);
                }
                continue;
            }
            report.Issues.Add(new IntegrityIssue
            {
                NodeId = nodeId, NodeTitle = title, Concern = concern, Index = i,
                Kind = check.Kind, Detail = check.Detail, Link = links[i]
            });
        }
    }

    /// <summary>
    /// The advisory for a link whose <see cref="Snaplink.Ast"/> does not resolve, or null when it resolves —
    /// or when nothing can be proven (no <c>ast</c>, no grammar for the file, an unreadable file).
    /// <para>
    /// Never an <see cref="IntegrityIssue"/>. Nothing has ever validated this field, so it holds prose as
    /// often as a path; failing a release build on that would be punishing links whose real target is sound.
    /// </para></summary>
    private SnaplinkAdvisory? CheckAst(Snaplink link)
    {
        if (string.IsNullOrWhiteSpace(link.Ast) || string.IsNullOrWhiteSpace(link.Doc)) return null;

        var facts = Facts(link.Doc!);
        if (!facts.Exists || facts.Text is null) return null;
        if (facts.Outline() is not { HasContent: true } outline) return null;   // unverifiable, as ever

        var paths = outline.Types.Select(t => t.AstPath)
                           .Concat(outline.Types.SelectMany(t => t.Members).Select(m => m.AstPath))
                           .ToList();
        if (paths.Contains(link.Ast!, StringComparer.Ordinal)) return null;

        return new SnaplinkAdvisory
        {
            Kind = SnaplinkAdvisoryKind.UnresolvedAst,
            Doc = link.Doc!,
            Current = link.Ast!,
            Suggestion = NearestPath(link.Ast!, outline),
        };
    }

    /// <summary>
    /// The structure path a stale <c>ast</c> most likely meant. Matches on the names inside the value rather
    /// than the value as a whole, because these were written as descriptions — <c>"ListView[x:Name=AppListView]"</c>,
    /// <c>"Grid[Carousel]"</c>, <c>"SuccessBrush"</c> — and the name buried in one is the part that is real.
    /// Returns null when nothing in the file is a close enough match to be worth offering.
    /// </summary>
    private static string? NearestPath(string ast, CodeOutline outline)
    {
        var words = Words.Matches(ast).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        if (words.Count == 0) return null;

        var candidates = outline.Types
            .Select(t => (t.AstPath, t.Name))
            .Concat(outline.Types.SelectMany(t => t.Members).Select(m => (m.AstPath, m.Name)));

        // An exact name match is the only thing confident enough to suggest; anything looser would be a guess
        // dressed up as a fix, and the point of the advisory is to be actioned without re-checking it.
        var list = candidates.ToList();
        foreach (var (path, name) in list)
            if (words.Contains(name)) return path;

        // Then the same test with the punctuation and casing taken out, which is what separates a label like
        // "Mic button" from the MicButton it was written for. Still an equality, not a similarity.
        var flattened = Flatten(ast);
        foreach (var (path, name) in list)
            if (flattened == Flatten(name)) return path;
        return null;

        static string Flatten(string s) => string.Concat(s.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private static readonly Regex Words = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    /// <summary>What can be established about this link — sound, broken, or neither. The three are kept
    /// apart because only <see cref="LinkVerdict.Broken"/> gates a build, while only
    /// <see cref="LinkVerdict.Sound"/> earns telling someone their link is good.</summary>
    private LinkCheck Check(Snaplink link)
    {
        if (link.Type == "url")
        {
            if (string.IsNullOrWhiteSpace(link.Target))
                return LinkCheck.Broken(IntegrityKind.EmptyTarget, "url snaplink has no target");
            return Uri.TryCreate(link.Target, UriKind.Absolute, out _)
                ? LinkCheck.Sound()
                : LinkCheck.Broken(IntegrityKind.InvalidUrl, $"'{link.Target}' is not a well-formed absolute URL");
        }

        if (link.Type == "node")
        {
            if (string.IsNullOrWhiteSpace(link.Target))
                return LinkCheck.Broken(IntegrityKind.EmptyTarget, "node snaplink has no target node id");
            if (_nodeIds is null) return LinkCheck.Unverifiable();   // no tree in scope
            return _nodeIds.Contains(link.Target)
                ? LinkCheck.Sound()
                : LinkCheck.Broken(IntegrityKind.MissingNode, $"node '{link.Target}' is not in the tree");
        }

        if (string.IsNullOrWhiteSpace(link.Doc))
            return LinkCheck.Broken(IntegrityKind.MissingDoc, $"{link.Type} snaplink has no doc path");

        // A doc inside a linked worktree resolves today and dies with that branch — broken on arrival, and
        // checked before existence so the message names the real fix rather than "file not found".
        if (GitWorktrees.TryReRoot(link.Doc!, productRoot, Worktrees, out var reRooted))
            return LinkCheck.Broken(IntegrityKind.WorktreePath,
                    $"'{link.Doc}' points into a linked git worktree — use the repo path: {reRooted}");

        var facts = Facts(link.Doc!);
        if (!facts.Exists) return LinkCheck.Broken(IntegrityKind.MissingFile, $"file not found: {link.Doc}");
        if (facts.Text is null) return LinkCheck.Unverifiable();   // exists but unreadable/huge

        if (link.Type == "markdown")
            return link.TitlePath is { Count: > 0 } path && !SnaplinkTargets.HasHeadingPath(facts.Text, path)
                ? LinkCheck.Broken(IntegrityKind.MissingHeading, $"heading '{string.Join(" › ", path)}' not found in {link.Doc}")
                : LinkCheck.Sound();

        // code (and any other file-backed type): only structure we can actually resolve is checked.
        var wantsStructure = !string.IsNullOrWhiteSpace(link.Class) || !string.IsNullOrWhiteSpace(link.Method);
        if (!wantsStructure) return LinkCheck.Sound();          // whole-file link — existence was the whole claim
        if (facts.Outline() is not { HasContent: true } outline) return LinkCheck.Unverifiable();

        if (!string.IsNullOrWhiteSpace(link.Class))
        {
            var type = outline.Types.FirstOrDefault(t => string.Equals(t.Name, link.Class, StringComparison.Ordinal));
            if (type is null)
                return LinkCheck.Broken(IntegrityKind.MissingClass, $"class '{link.Class}' is not declared in {link.Doc}");

            if (!string.IsNullOrWhiteSpace(link.Method)
                && !type.Members.Any(m => string.Equals(m.Name, link.Method, StringComparison.Ordinal)))
                return LinkCheck.Broken(IntegrityKind.MissingMethod, $"method '{link.Class}.{link.Method}' is not declared in {link.Doc}");

            return LinkCheck.Sound();
        }

        // Method without a class → a top-level function.
        return outline.TopLevel.Any(m => string.Equals(m.Name, link.Method, StringComparison.Ordinal))
            ? LinkCheck.Sound()
            : LinkCheck.Broken(IntegrityKind.MissingMethod, $"top-level '{link.Method}' is not declared in {link.Doc}");
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
