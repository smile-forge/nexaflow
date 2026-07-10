using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Re-attaches the issues in a saved <see cref="IntegrityReport"/> to the actual <see cref="Snaplink"/>
/// instances of a freshly-loaded tree, so a repair UI can edit the real thing rather than a detached copy.
/// </summary>
/// <remarks>
/// The recorded <see cref="IntegrityIssue.Index"/> is a <em>hint</em>, not a guarantee. Removing one broken
/// link shifts every later index in the same list, so a purely index-based lookup would strand that node's
/// other issues as unbindable. Binding therefore falls back to matching on the link's target, claiming each
/// live instance at most once so two identical links cannot both bind to the same object.
/// </remarks>
public static class IntegrityBinder
{
    /// <summary>The snaplink list an issue refers to: the node's own, or one of its concern links'.</summary>
    public static List<Snaplink>? LinkList(ProductNode node, string? concern) =>
        concern is null
            ? node.Snaplinks
            : node.Concerns?.FirstOrDefault(c => c.Tag == concern)?.Snaplinks;

    /// <summary>Two links point at the same thing (status is deliberately ignored).</summary>
    public static bool SameTarget(Snaplink a, Snaplink b) =>
        a.Type == b.Type && a.Doc == b.Doc && a.Class == b.Class && a.Method == b.Method && a.Target == b.Target
        && SamePath(a.TitlePath, b.TitlePath);

    private static bool SamePath(List<string>? a, List<string>? b) =>
        (a is null or { Count: 0 } && b is null or { Count: 0 }) || (a is not null && b is not null && a.SequenceEqual(b));

    /// <summary>
    /// Binds each issue to its live link, positionally aligned with <paramref name="issues"/>. A null entry
    /// means the link genuinely no longer exists in the tree (the caller should treat that row as read-only).
    /// </summary>
    public static IReadOnlyList<Snaplink?> Bind(ProductState state, IReadOnlyList<IntegrityIssue> issues)
    {
        var claimed = new HashSet<Snaplink>(ReferenceEqualityComparer.Instance);
        var bound = new Snaplink?[issues.Count];

        for (var i = 0; i < issues.Count; i++)
            bound[i] = BindOne(state, issues[i], claimed);

        return bound;
    }

    private static Snaplink? BindOne(ProductState state, IntegrityIssue issue, HashSet<Snaplink> claimed)
    {
        if (!state.Nodes.TryGetValue(issue.NodeId, out var node)) return null;

        var list = LinkList(node, issue.Concern);
        if (list is null) return null;

        // Fast path: the recorded slot still holds the reported link.
        if (issue.Index >= 0 && issue.Index < list.Count)
        {
            var at = list[issue.Index];
            if (SameTarget(at, issue.Link) && claimed.Add(at)) return at;
        }

        // Slow path: an earlier removal shifted the indices — take the first unclaimed match.
        foreach (var candidate in list)
            if (SameTarget(candidate, issue.Link) && claimed.Add(candidate)) return candidate;

        return null;
    }

    /// <summary>
    /// After a link is removed from a node's list, slides the recorded index of every later issue on that
    /// same list down one, so the saved report keeps describing the tree it actually refers to.
    /// </summary>
    public static void ReindexAfterRemoval(IntegrityReport report, IntegrityIssue removed, int removedIndex)
    {
        foreach (var other in report.Issues)
            if (!ReferenceEquals(other, removed)
                && other.NodeId == removed.NodeId
                && other.Concern == removed.Concern
                && other.Index > removedIndex)
                other.Index--;
    }
}
