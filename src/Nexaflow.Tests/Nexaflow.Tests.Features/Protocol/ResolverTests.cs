using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The encode-side resolver.
///
/// <para>
/// Every case here is a shape that broke the previous design, reduced to synthetic nodes so it can be
/// tested with no pattern library, no document and no socket. The previous design failed all ten stress
/// protocols on encode — including the two simplest — for three distinct reasons, none of them a cycle,
/// and the worst of them reported <i>success</i>.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol resolver — tree nodes land with the engine")]
public class ResolverTests
{
    /// <summary>A node whose facets settle from a table of dependencies and a compute function.</summary>
    private static ResolutionNode Node(
        string id,
        Dictionary<Facet, FacetRef[]>? deps = null,
        Func<Facet, IReadOnlyDictionary<FacetRef, object?>, FacetResult>? settle = null,
        params Facet[] notApplicable)
        => new()
        {
            Id = id,
            DependenciesFor = f => deps is not null && deps.TryGetValue(f, out var d) ? d : [],
            Settle = settle ?? ((f, _) => FacetResult.Of($"{id}.{f}")),
            NotApplicable = new HashSet<Facet>(notApplicable),
        };

    private static FacetRef Ref(string id, Facet f) => new(id, f);

    // ── The shape that used to block on pass one ──────────────────────────────

    [TestMethod]
    public void Extent_may_depend_on_value_which_the_old_fixed_stage_order_forbade()
    {
        // A variable-width length field: its own width is a function of the number it carries. Under a
        // fixed Sized-before-Valued chain this is unschedulable, and it is the single commonest shape in
        // the corpus — one protocol had 57 of 57 segments blocked on it.
        var resolver = new Resolver().Add(
            Node("body", settle: (f, _) => FacetResult.Of(f == Facet.Extent ? 300 : "body")),
            Node("len",
                deps: new()
                {
                    [Facet.Value] = [Ref("body", Facet.Extent)],
                    [Facet.Extent] = [Ref("len", Facet.Value)],      // width FROM value
                },
                settle: (f, inputs) => f switch
                {
                    Facet.Value => FacetResult.Of((int)inputs[Ref("body", Facet.Extent)]!),
                    Facet.Extent => FacetResult.Of((int)inputs[Ref("len", Facet.Value)]! < 128 ? 1 : 3),
                    _ => FacetResult.Of(null),
                }));

        var settled = resolver.Resolve();

        Assert.AreEqual(300, settled[Ref("len", Facet.Value)]);
        Assert.AreEqual(3, settled[Ref("len", Facet.Extent)], "300 needs the long form");
    }

    [TestMethod]
    public void A_length_covering_a_later_region_resolves_without_a_placeholder()
    {
        // The length precedes the region it measures on the wire, but depends on it in the graph. No
        // placeholder, no back-patch pass — the dependency simply settles later than the thing that needs it.
        var resolver = new Resolver().Add(
            Node("a", settle: (f, _) => FacetResult.Of(f == Facet.Extent ? 4 : "a")),
            Node("b", settle: (f, _) => FacetResult.Of(f == Facet.Extent ? 9 : "b")),
            Node("total",
                deps: new() { [Facet.Value] = [Ref("a", Facet.Extent), Ref("b", Facet.Extent)] },
                settle: (f, inputs) => f == Facet.Value
                    ? FacetResult.Of((int)inputs[Ref("a", Facet.Extent)]! + (int)inputs[Ref("b", Facet.Extent)]!)
                    : FacetResult.Of(2)));

        Assert.AreEqual(13, resolver.Resolve()[Ref("total", Facet.Value)]);
    }

    [TestMethod]
    public void A_digest_over_a_span_settles_after_every_member_of_that_span()
    {
        var resolver = new Resolver().Add(
            Node("h1", settle: (f, _) => FacetResult.Of(f == Facet.Value ? 10 : 1)),
            Node("h2", settle: (f, _) => FacetResult.Of(f == Facet.Value ? 20 : 1)),
            Node("mac",
                // The digest's EXTENT is independent of its value — which is what makes this solvable at
                // all — while its VALUE waits on the whole span.
                deps: new() { [Facet.Value] = [Ref("h1", Facet.Value), Ref("h2", Facet.Value)] },
                settle: (f, inputs) => f == Facet.Value
                    ? FacetResult.Of((int)inputs[Ref("h1", Facet.Value)]! ^ (int)inputs[Ref("h2", Facet.Value)]!)
                    : FacetResult.Of(16)));

        Assert.AreEqual(30, resolver.Resolve()[Ref("mac", Facet.Value)]);
    }

    // ── The failure that used to report success ───────────────────────────────

    [TestMethod]
    public void A_repetition_expands_and_its_elements_are_resolved_too()
    {
        var resolver = new Resolver().Add(
            Node("list",
                settle: (f, _) => f == Facet.Realised
                    ? FacetResult.Expanding("expanded",
                        Node("item0", settle: (_, _) => FacetResult.Of(7)),
                        Node("item1", settle: (_, _) => FacetResult.Of(8)))
                    : FacetResult.Of(null)));

        var settled = resolver.Resolve();

        Assert.AreEqual(7, settled[Ref("item0", Facet.Value)], "a realised child must itself resolve");
        Assert.AreEqual(8, settled[Ref("item1", Facet.Value)]);
    }

    [TestMethod]
    public void An_unrealised_dependency_is_reported_as_under_expansion_not_as_success()
    {
        // THE regression this facet exists for. A node depends on an element that was never materialised.
        // Under the previous terminal condition — "every node emitted" — this set is trivially complete and
        // encoding reported success while emitting a short, structurally valid, wrong message.
        var resolver = new Resolver().Add(
            Node("count",
                deps: new() { [Facet.Value] = [Ref("neverMade", Facet.Value)] },
                settle: (_, _) => FacetResult.Of(0)));

        var ex = Assert.ThrowsExactly<ResolutionException>(() => resolver.Resolve());

        Assert.AreEqual(ResolutionFailure.Unrealised, ex.Diagnostic.Failure,
            "under-expansion must never be reported as success, and must not be called a cycle either");
        StringAssert.Contains(ex.Message, "neverMade");
        StringAssert.Contains(ex.Message, "report success",
            "the message must say what the old failure mode was, or the next author will not see the risk");
    }

    // ── Diagnostics: a stall is not automatically a cycle ─────────────────────

    [TestMethod]
    public void A_genuine_cycle_is_named_as_one_with_the_participating_facets()
    {
        var resolver = new Resolver().Add(
            Node("x", deps: new() { [Facet.Value] = [Ref("y", Facet.Value)] }),
            Node("y", deps: new() { [Facet.Value] = [Ref("x", Facet.Value)] }));

        var ex = Assert.ThrowsExactly<ResolutionException>(() => resolver.Resolve());

        Assert.AreEqual(ResolutionFailure.Cycle, ex.Diagnostic.Failure);
        StringAssert.Contains(ex.Message, "depend on each other");
        Assert.IsTrue(ex.Diagnostic.Blocked.Count >= 2);
    }

    [TestMethod]
    public void A_self_referential_facet_is_a_cycle_rather_than_a_hang()
    {
        var resolver = new Resolver().Add(
            Node("selfish", deps: new() { [Facet.Value] = [Ref("selfish", Facet.Value)] }));

        var ex = Assert.ThrowsExactly<ResolutionException>(() => resolver.Resolve());
        Assert.AreEqual(ResolutionFailure.Cycle, ex.Diagnostic.Failure);
    }

    // ── Scheduling behaviour ──────────────────────────────────────────────────

    [TestMethod]
    public void A_deep_chain_resolves_without_anything_resembling_a_pass_count()
    {
        // Demand-driven waiting is O(edges). The previous rescan-per-pass design measured 61 passes on the
        // worst corpus protocol; a 200-deep chain here is the degenerate case for that design and costs
        // nothing for this one.
        const int depth = 200;
        var resolver = new Resolver();

        resolver.Add(Node("n0", settle: (_, _) => FacetResult.Of(0)));
        for (int i = 1; i < depth; i++)
        {
            int prev = i - 1;
            resolver.Add(Node($"n{i}",
                deps: new() { [Facet.Value] = [Ref($"n{prev}", Facet.Value)] },
                settle: (f, inputs) => f == Facet.Value
                    ? FacetResult.Of((int)inputs[Ref($"n{prev}", Facet.Value)]! + 1)
                    : FacetResult.Of(null)));
        }

        var settled = resolver.Resolve();
        Assert.AreEqual(depth - 1, settled[Ref($"n{depth - 1}", Facet.Value)]);
    }

    [TestMethod]
    public void Declaration_order_does_not_affect_the_outcome()
    {
        // Nodes declared before their prerequisites must resolve identically — otherwise document authors
        // acquire a superstition about ordering that the model never promised.
        var forwards = new Resolver().Add(
            Node("first", settle: (_, _) => FacetResult.Of(5)),
            Node("second",
                deps: new() { [Facet.Value] = [Ref("first", Facet.Value)] },
                settle: (f, i) => f == Facet.Value
                    ? FacetResult.Of((int)i[Ref("first", Facet.Value)]! * 2) : FacetResult.Of(null)));

        var backwards = new Resolver().Add(
            Node("second",
                deps: new() { [Facet.Value] = [Ref("first", Facet.Value)] },
                settle: (f, i) => f == Facet.Value
                    ? FacetResult.Of((int)i[Ref("first", Facet.Value)]! * 2) : FacetResult.Of(null)),
            Node("first", settle: (_, _) => FacetResult.Of(5)));

        Assert.AreEqual(forwards.Resolve()[Ref("second", Facet.Value)],
                        backwards.Resolve()[Ref("second", Facet.Value)]);
    }

    [TestMethod]
    public void A_facet_that_does_not_apply_is_settled_rather_than_blocking()
    {
        // A node emitting no octets has no extent to compute, and must not stall anything waiting on it.
        var resolver = new Resolver().Add(
            Node("phantom", settle: (_, _) => FacetResult.Of(null), notApplicable: Facet.Extent),
            Node("observer",
                deps: new() { [Facet.Value] = [Ref("phantom", Facet.Extent)] },
                settle: (_, _) => FacetResult.Of("saw it")));

        Assert.AreEqual("saw it", resolver.Resolve()[Ref("observer", Facet.Value)]);
    }

    [TestMethod]
    public void The_trace_records_settlement_order_for_the_dry_run_breakdown()
    {
        var resolver = new Resolver().Add(
            Node("a", settle: (_, _) => FacetResult.Of(1)),
            Node("b", deps: new() { [Facet.Value] = [Ref("a", Facet.Value)] },
                settle: (_, _) => FacetResult.Of(2)));

        resolver.Resolve();

        int a = resolver.Trace.ToList().FindIndex(t => t.StartsWith("a.Value", StringComparison.Ordinal));
        int b = resolver.Trace.ToList().FindIndex(t => t.StartsWith("b.Value", StringComparison.Ordinal));

        Assert.IsTrue(a >= 0 && b >= 0 && a < b, "a prerequisite settles before its dependent");
        Assert.IsTrue(resolver.SettledCount > 0);
    }
}
