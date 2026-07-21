using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Checks a feature subtree against the modelling rules in <c>docs/feature-tree-and-tests.md</c> §1–§4 — the
/// conventions that were previously only enforced by reading the tree carefully. It answers "is this feature
/// modelled the way the Text Viewer is?".
/// </summary>
/// <remarks>
/// <para>
/// <b>Advisory, not gating.</b> Unlike <see cref="SnaplinkValidator"/> (which proves a link dead and fails the
/// release build), this reports convention drift, and a role it infers can be wrong. Node roles are derived
/// from position — the doc's §6(b) proposal to store an explicit <c>kind</c> would make these robust; until
/// then a finding is a prompt to look, not a verdict.
/// </para>
/// <para>
/// Rules are scoped to features that have <em>started</em> adopting the backbone: a feature with no UI node
/// yet is reported once (<see cref="Rule.MissingBackbone"/>) rather than having every leaf flagged, so the
/// output stays a to-do list instead of a wall.
/// </para>
/// </remarks>
public static class StructureLinter
{
    /// <summary>Which convention a finding is about.</summary>
    public enum Rule
    {
        /// <summary>`AI Ready` is the human maturity verdict for a whole feature — it belongs on the feature
        /// root and nowhere below it (§2).</summary>
        AiReadyBelowFeature,
        /// <summary>A feature root should have the UI / Functionality / AI Integration backbone (§1).</summary>
        MissingBackbone,
        /// <summary>A panel or state node (anything under UI that has children) is covered by the feature's one
        /// UI journey, so it carries no `tests` concern of its own (§2, §3).</summary>
        ContainerHasTests,
        /// <summary>A panel is a distinct visual surface, so it carries `theming` (§2).</summary>
        PanelMissingTheming,
        /// <summary>Every leaf control / behaviour / AI-act leaf is unit-tested, or explicitly `shouldnt` with a
        /// note saying why (§2, §3).</summary>
        LeafMissingTests,
        /// <summary>A `tests` concern that reached done/faulted names the test backing it (§4). This is the
        /// doc's §6(a) proposal, previewed here before it becomes gating.</summary>
        TestsDoneWithoutSnaplink,
        /// <summary>A node declared untestable should say why and who covers it instead (§3).</summary>
        ShouldntWithoutNote,
        /// <summary>The UI node carries the feature's one journey test (§3, §4).</summary>
        UiMissingJourney,
    }

    /// <summary>One convention breach: the node, the rule, and what to do about it.</summary>
    public sealed record Finding(string FeatureId, string NodeId, string Title, Rule Rule, string Detail);

    private const string TestsConcern = "tests";
    private const string ThemingConcern = "theming";
    private const string AiReadyConcern = "AI Ready";

    /// <summary>
    /// Lints every feature, or — with <paramref name="underId"/> — just that node's subtree (a feature root,
    /// or the whole tree from any ancestor). Findings are ordered by feature then tree position.
    /// </summary>
    public static IReadOnlyList<Finding> Lint(ProductState state, string? underId = null)
    {
        var findings = new List<Finding>();
        foreach (var featureId in FeatureRoots(state, underId))
            LintFeature(state, featureId, findings);
        return findings;
    }

    /// <summary>
    /// The feature roots in scope: the children of the "Features" container, filtered to
    /// <paramref name="underId"/>'s subtree when one is given (so <c>--under git</c> lints just Git, and
    /// <c>--under features</c> lints them all).
    /// </summary>
    private static IEnumerable<string> FeatureRoots(ProductState state, string? underId)
    {
        var container = state.Nodes.FirstOrDefault(kv =>
            string.Equals(kv.Value.Title, "Features", StringComparison.OrdinalIgnoreCase));
        if (container.Key is null) yield break;

        foreach (var id in container.Value.Children.Where(state.Nodes.ContainsKey))
            if (underId is null || id == underId || IsAncestor(state, underId, id))
                yield return id;
    }

    private static bool IsAncestor(ProductState state, string ancestor, string node)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var cur = node; cur is not null && seen.Add(cur); cur = state.Nodes.GetValueOrDefault(cur)?.Parent)
            if (cur == ancestor) return true;
        return false;
    }

    private static void LintFeature(ProductState state, string featureId, List<Finding> findings)
    {
        var feature = state.Nodes[featureId];
        void Add(string nodeId, Rule rule, string detail) =>
            findings.Add(new Finding(featureId, nodeId, state.Nodes[nodeId].Title, rule, detail));

        var ui = ChildLike(state, feature, "UI", "-ui");
        var functionality = ChildLike(state, feature, "Functionality", "-functionality");
        var ai = ChildLike(state, feature, "AI Integration", "-ai");

        // `AI Ready` is the one concern with a hard placement rule, checked across the whole subtree.
        foreach (var id in Subtree(state, featureId).Where(id => id != featureId))
            if (HasConcern(state.Nodes[id], AiReadyConcern))
                Add(id, Rule.AiReadyBelowFeature,
                    $"'{AiReadyConcern}' is the feature-level maturity verdict — it belongs on '{featureId}' only");

        // Report an unconverted feature once rather than flagging each of its leaves.
        var missing = new[] { ("UI", ui), ("Functionality", functionality), ("AI Integration", ai) }
            .Where(x => x.Item2 is null).Select(x => x.Item1).ToList();
        if (missing.Count > 0)
            Add(featureId, Rule.MissingBackbone, $"no {string.Join(" / ", missing)} node — see docs §1");
        if (ui is null) return;   // without a UI node the position-based roles below can't be inferred

        if (!HasConcern(state.Nodes[ui], TestsConcern))
            Add(ui, Rule.UiMissingJourney, "the UI node carries the feature's one journey test (§3)");

        // Under UI: a node with children is a panel or a state group — journey-covered, so no `tests`.
        foreach (var id in Subtree(state, ui).Where(id => id != ui))
        {
            var node = state.Nodes[id];
            var isContainer = node.Children.Any(state.Nodes.ContainsKey);

            if (isContainer && HasConcern(node, TestsConcern))
                Add(id, Rule.ContainerHasTests,
                    "a panel / state node is covered by the UI journey — drop its 'tests' concern (§2)");

            // A state node is pure structure (no concerns at all); a panel owns a visual surface.
            if (isContainer && (node.Concerns?.Count ?? 0) > 0 && !HasConcern(node, ThemingConcern))
                Add(id, Rule.PanelMissingTheming, "a panel is a visual surface — it should carry 'theming' (§2)");

            if (!isContainer && !HasConcern(node, TestsConcern))
                Add(id, Rule.LeafMissingTests, "a leaf control is unit-tested — add a 'tests' concern (§2)");
        }

        // Functionality behaviours and AI act leaves are unit-tested too.
        foreach (var container in new[] { functionality, ai }.Where(c => c is not null))
            foreach (var id in Subtree(state, container!).Where(id => id != container))
            {
                var node = state.Nodes[id];
                if (!node.Children.Any(state.Nodes.ContainsKey) && !HasConcern(node, TestsConcern))
                    Add(id, Rule.LeafMissingTests, "a behaviour / AI-act leaf is unit-tested (§2)");
            }

        // Evidence rules, across the whole feature.
        foreach (var id in Subtree(state, featureId))
        {
            var node = state.Nodes[id];
            var tests = node.Concerns?.FirstOrDefault(c => c.Tag == TestsConcern);
            if (tests is null) continue;

            if (tests.Status is Status.Done or Status.Faulted && (tests.Snaplinks?.Count ?? 0) == 0)
                Add(id, Rule.TestsDoneWithoutSnaplink,
                    $"'tests' is {tests.Status.ToString().ToLowerInvariant()} but names no test (§4)");

            if (tests.Status is Status.Shouldnt && string.IsNullOrWhiteSpace(node.Note))
                Add(id, Rule.ShouldntWithoutNote,
                    "'tests' is shouldnt — add a note saying why and who covers it instead (§3)");
        }
    }

    /// <summary>A child of <paramref name="parent"/> whose title matches <paramref name="title"/> or whose id
    /// ends with <paramref name="idSuffix"/> — how the backbone nodes are recognised without a stored kind.</summary>
    private static string? ChildLike(ProductState state, ProductNode parent, string title, string idSuffix) =>
        parent.Children.FirstOrDefault(c =>
            state.Nodes.TryGetValue(c, out var n) &&
            (string.Equals(n.Title, title, StringComparison.OrdinalIgnoreCase) ||
             c.EndsWith(idSuffix, StringComparison.OrdinalIgnoreCase)));

    private static bool HasConcern(ProductNode node, string tag) =>
        node.Concerns?.Any(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase)) ?? false;

    /// <summary>The node and every descendant, in tree order; cycle-safe.</summary>
    private static IEnumerable<string> Subtree(ProductState state, string rootId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id) || !state.Nodes.TryGetValue(id, out var node)) continue;
            yield return id;
            foreach (var c in Enumerable.Reverse(node.Children)) stack.Push(c);
        }
    }
}
