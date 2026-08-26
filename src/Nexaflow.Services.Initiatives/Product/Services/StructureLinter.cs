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
        /// <summary>A leaf that many tests declare they cover is a leaf doing several jobs (§1, §3) — the
        /// tests found the sub-behaviours the tree has not named yet.</summary>
        LeafCoveredByTooManyTests,
        /// <summary>A node carrying many snaplinks is pointing at more code than one node can be about
        /// (§1, §4) — the same smell as <see cref="LeafCoveredByTooManyTests"/>, read from the links.</summary>
        TooManySnaplinks,
    }

    /// <summary>
    /// How many declared tests a leaf may attract before <see cref="Rule.LeafCoveredByTooManyTests"/> fires.
    /// <para>
    /// The model is one unit test per leaf behaviour, so a handful is normal and a dozen is not. The number
    /// is deliberately generous: this is a prompt to look at a node, and a rule that cries at seven would be
    /// turned off. For calibration, when this rule was written the whole repo had four leaves above it —
    /// <c>data-model</c> at 75 tests across five files and <c>integrity-validate</c> at 32 in one being the
    /// clearest cases of a leaf that had quietly become a subtree's worth of behaviour.
    /// </para>
    /// </summary>
    public const int MaxTestsPerLeaf = 12;

    /// <summary>
    /// How many snaplinks a node may carry — its own plus every concern's — before
    /// <see cref="Rule.TooManySnaplinks"/> fires.
    /// <para>
    /// Deliberately the same number as <see cref="MaxTestsPerLeaf"/>. The right ceiling really depends on how
    /// much code a feature involves and how user-facing it is, and no constant can know that; what a single
    /// catch-all buys instead is a reader who only has to hold one number. Where the two rules disagree about
    /// a node, that is information — a leaf over both is doing several jobs from two directions.
    /// </para>
    /// <para>
    /// For calibration, when this was written the median node carried 2 snaplinks and the mean 2.1 across
    /// 1,746 nodes; five were over this line and the largest was <c>sequence-diagram</c> at 29. Snaplinks do
    /// not aggregate the way tests do — a parent does not inherit its children's — so this applies to every
    /// node, though in practice it is leaves that trip it.
    /// </para>
    /// </summary>
    public const int MaxSnaplinksPerNode = 12;

    /// <summary>One convention breach: the node, the rule, and what to do about it.</summary>
    public sealed record Finding(string FeatureId, string NodeId, string Title, Rule Rule, string Detail);

    private const string TestsConcern = "tests";
    private const string ThemingConcern = "theming";
    private const string AiReadyConcern = "AI Ready";

    /// <summary>
    /// Lints every feature, or — with <paramref name="underId"/> — just that node's subtree (a feature root,
    /// or the whole tree from any ancestor). Findings are ordered by feature then tree position.
    /// </summary>
    /// <param name="coverage">The <c>scan-tests</c> manifest, when one has been generated. Only
    /// <see cref="Rule.LeafCoveredByTooManyTests"/> uses it; without it that rule simply doesn't run, which is
    /// why it is optional rather than a second entry point — a caller with no manifest still gets every other
    /// rule, and a caller that has one gets the extra check for free.</param>
    public static IReadOnlyList<Finding> Lint(
        ProductState state, string? underId = null, TestCoverageManifest? coverage = null)
    {
        var findings = new List<Finding>();
        foreach (var featureId in FeatureRoots(state, underId))
            LintFeature(state, featureId, coverage, findings);

        // The granularity rules run over EVERYTHING in scope, not just the feature subtree. The rules above
        // are feature-shaped - a backbone, a panel's theming - and mean nothing outside Features. "This node
        // is about too much" means the same wherever it sits, and scoping it to features hid the worst case
        // in the repo: `sequence-diagram`, under Common / Shared, at 29 snaplinks.
        LintGranularity(state, underId, coverage, findings);
        return findings;
    }

    /// <summary>
    /// The two rules that read a node's size rather than its shape. Attributed to the node's top-level
    /// section so the report still groups.
    /// </summary>
    private static void LintGranularity(
        ProductState state, string? underId, TestCoverageManifest? coverage, List<Finding> findings)
    {
        foreach (var id in InScope(state, underId))
        {
            var node = state.Nodes[id];
            void Add(Rule rule, string detail) => findings.Add(new Finding(Section(state, id), id, node.Title, rule, detail));

            // A node's own snaplinks plus its concerns'. Nothing aggregates from children, so a big number
            // is one node claiming to be about a lot of code rather than a parent summarising.
            var links = (node.Snaplinks?.Count ?? 0)
                      + (node.Concerns?.Sum(c => c.Snaplinks?.Count ?? 0) ?? 0);
            if (links > MaxSnaplinksPerNode)
                Add(Rule.TooManySnaplinks,
                    $"{links} snaplinks on one node (over {MaxSnaplinksPerNode}) - it is pointing at more code "
                    + "than one node can be about; split it and let each child carry its own links (§1, §4)");

            // The same smell read from the tests. Leaves only: a container legitimately accumulates its
            // children's tests, and flagging one would be flagging the tree for working.
            if (coverage is null || node.Children.Any(state.Nodes.ContainsKey)) continue;
            if (!coverage.Coverage.TryGetValue(id, out var refs) || refs.Count <= MaxTestsPerLeaf) continue;

            var files = refs.Select(r => r.File).Where(f => !string.IsNullOrEmpty(f))
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Add(Rule.LeafCoveredByTooManyTests,
                $"{refs.Count} tests declare this leaf"
                + (files > 1 ? $", across {files} files" : string.Empty)
                + $" (over {MaxTestsPerLeaf}) - they are probably covering behaviours that want their own "
                + "child nodes; `add-node` to name them (§1, §3)");
        }
    }

    /// <summary>Every node under <paramref name="underId"/>, or the whole tree when none is given.</summary>
    private static IEnumerable<string> InScope(ProductState state, string? underId) =>
        underId is null
            ? state.Nodes.Keys.Where(state.Nodes.ContainsKey)
            : Subtree(state, underId);

    /// <summary>The top-level ancestor a node sits under ("Features", "Common / Shared"), for grouping.</summary>
    private static string Section(ProductState state, string nodeId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cur = nodeId;
        while (seen.Add(cur) && state.Nodes.GetValueOrDefault(cur)?.Parent is { } parent
                             && state.Nodes.ContainsKey(parent))
            cur = parent;
        return cur;
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

    private static void LintFeature(
        ProductState state, string featureId, TestCoverageManifest? coverage, List<Finding> findings)
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
