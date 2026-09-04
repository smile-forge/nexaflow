using System.Text;
using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// One rendering of each product-tree query result, as text.
/// <para>
/// The CLI and the in-app AI tool surface answer the same questions over the same services, and the answer
/// has to read identically from both — otherwise a model told to "run lint and fix what it finds" learns one
/// output shape from the docs and meets another at runtime. Keeping the rendering here means the two cannot
/// drift: <c>Program.cs</c> writes these strings to the console, <c>ProductTools</c> returns them to the
/// model.
/// </para>
/// <para>
/// Everything is plain text with no ANSI or padding tricks beyond simple column alignment — it has to survive
/// being pasted into a prompt as readily as a terminal.
/// </para>
/// </summary>
public static class ProductReport
{
    private static string Lower(Status s) => s.ToString().ToLowerInvariant();

    // ── find ──────────────────────────────────────────────────────────────────

    /// <summary>Nodes matching a search term, with the path that locates each one.</summary>
    public static string Find(IReadOnlyList<ProductQuery.Hit> hits, string term)
    {
        if (hits.Count == 0) return $"No nodes match '{term}'.";

        var sb = new StringBuilder();
        foreach (var h in hits)
            sb.AppendLine($"  {h.Id,-28} [{Lower(h.Status)}]  {h.Title}"
                        + $"   ({string.Join(" > ", h.Path.Take(h.Path.Count - 1).Select(c => c.Title))})");
        sb.Append($"{hits.Count} match(es).");
        return sb.ToString();
    }

    // ── query ─────────────────────────────────────────────────────────────────

    /// <summary>Nodes filtered by subtree / concern / status / leafness. <paramref name="concern"/> names the
    /// concern column when one was filtered on.</summary>
    public static string Query(IReadOnlyList<ProductQuery.QueryHit> hits, string? concern)
    {
        if (hits.Count == 0) return "No matching nodes.";

        var sb = new StringBuilder();
        foreach (var h in hits)
        {
            var shape = h.IsLeaf ? "leaf" : "panel";
            var concernCol = h.ConcernStatus is { } cs
                ? $"{concern}={Lower(cs)}({h.ConcernSnaplinks} sl)"
                : $"[{Lower(h.NodeStatus)}]";
            sb.AppendLine($"  {h.Id,-26} {shape,-5} {concernCol,-22} "
                        + string.Join(" > ", h.Path.Select(c => c.Title)));
        }
        sb.Append($"{hits.Count} node(s).");
        return sb.ToString();
    }

    // ── describe ──────────────────────────────────────────────────────────────

    /// <summary>One node in full: path, about/note, concerns, snaplinks and children.</summary>
    public static string Describe(ProductQuery.Detail d)
    {
        var sb = new StringBuilder();

        // Status on a parent is rolled up from its children, so say that rather than let it read as stored.
        var derived = d.Children.Count > 0 ? "  (derived)" : "";
        sb.AppendLine($"{d.Id}  [{Lower(d.Status)}]{derived}  {d.Title}");
        sb.AppendLine($"  path:    {string.Join(" > ", d.Path.Select(c => c.Title))}");
        if (!string.IsNullOrWhiteSpace(d.Description)) sb.AppendLine($"  about:   {d.Description}");
        if (!string.IsNullOrWhiteSpace(d.Note)) sb.AppendLine($"  note:    {d.Note}");
        if (d.Concerns.Count > 0)
            sb.AppendLine("  concerns: " + string.Join("  ", d.Concerns.Select(c => $"{c.Tag}={Lower(c.Status)}")));
        foreach (var g in d.Snaplinks.GroupBy(l => l.Kind).OrderBy(g => g.Key))
            foreach (var l in g)
                sb.AppendLine($"  {g.Key,-6} {l.Display}");
        if (d.Children.Count > 0)
            sb.AppendLine("  children: " + string.Join(", ", d.Children.Select(c => c.Id)));

        return sb.ToString().TrimEnd();
    }

    // ── tree ──────────────────────────────────────────────────────────────────

    /// <summary>A subtree as an indented outline. Without <paramref name="full"/> the snaplinks stay a count,
    /// so the shape of a large subtree still fits in one screen (or one prompt).</summary>
    public static string Outline(IReadOnlyList<ProductQuery.OutlineRow> rows, bool full)
    {
        var sb = new StringBuilder();
        foreach (var (depth, n) in rows)
        {
            var pad = new string(' ', depth * 2);
            var concerns = n.Concerns.Count > 0
                ? "   " + string.Join(" ", n.Concerns.Select(c => $"{c.Tag}={Lower(c.Status)}"))
                : "";
            var links = !full && n.Snaplinks.Count > 0
                ? $"   ({n.Snaplinks.Count} link{(n.Snaplinks.Count == 1 ? "" : "s")})"
                : "";
            sb.AppendLine($"{pad}{n.Id}  [{Lower(n.Status)}]  {n.Title}{concerns}{links}");

            if (!full) continue;
            if (!string.IsNullOrWhiteSpace(n.Description)) sb.AppendLine($"{pad}    about: {n.Description}");
            if (!string.IsNullOrWhiteSpace(n.Note)) sb.AppendLine($"{pad}    note:  {n.Note}");
            foreach (var g in n.Snaplinks.GroupBy(l => l.Kind).OrderBy(g => g.Key))
                foreach (var l in g)
                    sb.AppendLine($"{pad}    {g.Key,-6} {l.Display}");
        }
        sb.Append($"{rows.Count} node(s).");
        return sb.ToString();
    }

    // ── lint ──────────────────────────────────────────────────────────────────

    /// <summary>Modelling-rule findings, grouped by feature. Advisory: nothing here fails a build, and the
    /// text says so, because a model reading "finding" as "error" will try to fix things that are fine.</summary>
    public static string Lint(IReadOnlyList<StructureLinter.Finding> findings, string? underId)
    {
        if (findings.Count == 0)
            return underId is null
                ? "Every feature follows the modelling rules."
                : $"'{underId}' follows the modelling rules.";

        var sb = new StringBuilder();
        foreach (var byFeature in findings.GroupBy(f => f.FeatureId))
        {
            sb.AppendLine($"\n{byFeature.Key}  ({byFeature.Count()} finding(s))");
            foreach (var f in byFeature)
                sb.AppendLine($"  [{f.Rule}] {f.NodeId} — {f.Title}\n      {f.Detail}");
        }

        var features = findings.Select(f => f.FeatureId).Distinct().Count();
        sb.Append($"\n{findings.Count} finding(s) across {features} feature(s) — advisory (nothing fails a build).");
        return sb.ToString();
    }

    // ── validate ──────────────────────────────────────────────────────────────

    /// <summary>Broken snaplinks. Unlike lint these <i>are</i> gating — the installer build fails on them —
    /// so the summary line says how much was scanned to reach the verdict.</summary>
    public static string Validate(IntegrityReport report)
    {
        var scanned = $"scanned {report.ScannedSnaplinks} snaplink(s) across {report.ScannedNodes} node(s)";
        if (report.IsClean) return $"Snaplinks OK — {scanned}.{Advisories(report)}";

        var sb = new StringBuilder();
        foreach (var i in report.Issues)
            sb.AppendLine($"  {i.NodeId} [{i.Scope}] #{i.Index}  {i.Detail}");

        // Not every gating issue is a broken *snaplink* any more — a stale [CoversNode] id is a rotten test
        // declaration, and MissingSnaplink is an absent one. Counting them all as "broken snaplinks" sent the
        // reader hunting the tree for a link that was never there, so the tally names each family it found.
        var byFamily = report.Issues
            .GroupBy(Family)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {g.Key}");

        sb.Append($"{string.Join(", ", byFamily)} across "
                + $"{report.Issues.Select(i => i.NodeId).Distinct().Count()} node(s) — {scanned}.");
        sb.Append(Advisories(report));
        return sb.ToString();
    }

    /// <summary>
    /// The non-gating tail of the report. Kept visually and textually apart from the issues above it, and
    /// never counted into them: an advisory must never read as something that failed the build, or the
    /// distinction that lets it exist at all stops meaning anything.
    /// </summary>
    private static string Advisories(IntegrityReport report)
    {
        if (report.Advisories.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine().AppendLine();
        sb.AppendLine($"{report.AdvisoryCount} advisory (non-gating) — an ast target that no longer resolves:");
        foreach (var a in report.Advisories)
        {
            sb.AppendLine($"  {a.NodeId} [{a.Concern ?? "node"}] #{a.Index}  ast \"{a.Current}\" does not resolve in {a.Doc}");
            sb.AppendLine($"      {(a.Suggestion is { Length: > 0 } s ? $"did you mean {s}?  " : "")}{a.Command}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>The noun for an issue kind, so the summary tally reads as what actually has to be fixed.</summary>
    private static string Family(IntegrityIssue issue) => issue.Kind switch
    {
        IntegrityKind.StaleCoverageNode  => "stale test coverage declaration(s)",
        IntegrityKind.StaleCoverageBuild => "test project(s) behind their source",
        IntegrityKind.UnlinkedProject    => "untracked assembly/assemblies",
        IntegrityKind.MissingSnaplink    => "unbacked concern(s)",
        _                                => "broken snaplink(s)"
    };
}
