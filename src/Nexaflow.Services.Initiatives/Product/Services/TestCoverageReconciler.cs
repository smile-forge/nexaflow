using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Cross-checks the test-coverage manifest (what tests <em>declare</em> they cover, via <c>[CoversNode]</c>)
/// against the tree (the authoritative intended state) and reports the gaps as non-gating
/// <see cref="CoverageAdvisory"/> items: a declared node that no longer exists, or a declaration with no
/// matching <c>tests</c> snaplink yet. The tree is never mutated here — the Integrity page decides whether to
/// act on an advisory. Pure and file-free, so it runs instantly on the UI thread.
/// </summary>
public static class TestCoverageReconciler
{
    public const string TestsConcern = "tests";

    public static CoverageReport Reconcile(ProductState state, TestCoverageManifest? manifest)
    {
        var report = new CoverageReport { Generated = manifest?.Generated ?? string.Empty };
        if (manifest is null) return report;

        foreach (var (nodeId, refs) in manifest.Coverage)
        {
            if (!state.Nodes.TryGetValue(nodeId, out var node))
            {
                foreach (var r in refs)
                    report.Advisories.Add(Advisory(CoverageAdvisoryKind.UnknownNode, nodeId, string.Empty, r));
                continue;
            }

            var existing = node.Concerns?.FirstOrDefault(c => c.Tag == TestsConcern)?.Snaplinks ?? [];
            foreach (var r in refs)
                if (!AlreadyLinked(existing, r))
                    report.Advisories.Add(Advisory(CoverageAdvisoryKind.DeclaredButUnlinked, nodeId, node.Title, r));
        }

        report.Advisories = [.. report.Advisories
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.NodeId, StringComparer.Ordinal)
            .ThenBy(a => a.Class, StringComparer.Ordinal)
            .ThenBy(a => a.Method ?? string.Empty, StringComparer.Ordinal)];
        return report;
    }

    private static bool AlreadyLinked(IReadOnlyList<Snaplink> existing, TestRef r)
    {
        if (r.File.Length == 0) return false;   // can't match without a file — surface it (not addable)
        var wantClass = CoverageAdvisory.SimpleName(r.Class);
        var wantMethod = r.Method ?? string.Empty;
        return existing.Any(l =>
            l.Type == "code"
            && SamePath(l.Doc, r.File)
            && string.Equals(l.Class, wantClass, StringComparison.Ordinal)
            && string.Equals(l.Method ?? string.Empty, wantMethod, StringComparison.Ordinal));
    }

    private static bool SamePath(string? a, string? b) =>
        string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    private static string Norm(string? p) => (p ?? string.Empty).Replace('\\', '/').Trim('/');

    private static CoverageAdvisory Advisory(CoverageAdvisoryKind kind, string nodeId, string title, TestRef r) => new()
    {
        Kind = kind, NodeId = nodeId, NodeTitle = title,
        Assembly = r.Assembly, Class = r.Class, Method = r.Method, File = r.File
    };
}
