using Nexaflow.Core.Services;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;
using Nexaflow.Tests.Fixtures;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The subfeature framework: discovery from the index, deterministic ordering, and laziness.
///
/// <para>
/// This generalises what the archive backends did ad-hoc — push-registered at assembly activation via a
/// parameterless <c>Activator</c>, with no metadata, no enable/disable, and an order that fell out of
/// <c>Directory.GetFiles</c>. The properties asserted here are the ones that were previously accidental.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("subfeature discovery/DI plumbing — no single product node")]
public class SubfeatureCatalogTests
{
    // ── Argument recognition ──────────────────────────────────────────────────

    [TestMethod]
    public void Recognises_a_handle_list_parameter_and_extracts_its_contract()
    {
        Assert.IsTrue(SubfeatureArg.IsHandleList(
            typeof(IReadOnlyList<ISubfeatureHandle<INetworkProbe>>), out var contract));
        Assert.AreEqual(typeof(INetworkProbe), contract);
    }

    [TestMethod]
    public void Recognises_an_eager_instance_list_parameter()
    {
        Assert.IsTrue(SubfeatureArg.IsInstanceList(typeof(IReadOnlyList<INetworkProbe>), out var contract));
        Assert.AreEqual(typeof(INetworkProbe), contract);
    }

    [TestMethod]
    public void A_handle_list_is_not_also_treated_as_an_instance_list()
    {
        // Both cases sit in the same resolver; if the eager one also matched, every lazy request would
        // silently load every plugin — the exact regression this framework exists to avoid.
        Assert.IsFalse(SubfeatureArg.IsInstanceList(
            typeof(IReadOnlyList<ISubfeatureHandle<INetworkProbe>>), out _));
    }

    [TestMethod]
    public void An_ordinary_list_parameter_is_left_alone_so_it_still_reports_a_real_defect()
    {
        // IReadOnlyList<string> must keep producing "the feature DI does not supply this" rather than
        // being quietly handed an empty list, which would turn a typo into a feature that does nothing.
        Assert.IsFalse(SubfeatureArg.IsInstanceList(typeof(IReadOnlyList<string>), out _));
        Assert.IsFalse(SubfeatureArg.IsInstanceList(typeof(IReadOnlyList<int>), out _));
        Assert.IsFalse(SubfeatureArg.IsHandleList(typeof(string), out _));
    }

    [TestMethod]
    public void Accepts_the_read_only_sequence_shapes_a_feature_might_reasonably_declare()
    {
        foreach (var t in new[]
        {
            typeof(IReadOnlyList<INetworkProbe>),
            typeof(IReadOnlyCollection<INetworkProbe>),
            typeof(IEnumerable<INetworkProbe>),
        })
            Assert.IsTrue(SubfeatureArg.IsInstanceList(t, out _), t.Name);

        // A mutable list is deliberately NOT accepted: handing a feature something it can mutate implies
        // an ownership it does not have.
        Assert.IsFalse(SubfeatureArg.IsInstanceList(typeof(List<INetworkProbe>), out _));
    }

    // ── Discovery from the index ──────────────────────────────────────────────

    [TestMethod]
    public void Discovers_the_shipped_probe_plugin_from_the_index()
    {
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });   // no-op activation: isolate discovery from side-effects

        var found = catalog.Subfeatures(typeof(INetworkProbe));

        Assert.IsTrue(found.Count > 0, "the ARP probe ships as a plugin and must be discoverable");
        Assert.IsTrue(found.Any(f => f.Meta.Owner == "network" && f.Meta.Id == "arp"),
            $"expected network/arp; found: {string.Join(", ", found.Select(f => $"{f.Meta.Owner}/{f.Meta.Id}"))}");
    }

    [TestMethod]
    public void Carries_the_metadata_needed_to_render_a_plugin_list_without_loading_anything()
    {
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });

        var arp = catalog.Subfeatures(typeof(INetworkProbe)).Single(f => f.Meta.Id == "arp");

        Assert.AreEqual("network", arp.Meta.Owner);
        Assert.IsFalse(string.IsNullOrWhiteSpace(arp.Meta.DisplayName));
        Assert.IsFalse(string.IsNullOrWhiteSpace(arp.Meta.Description),
            "the description is shown to the user and handed to the model — both need it");
        Assert.AreEqual(0, arp.Meta.Order, "ARP is discovery layer 0");
    }

    [TestMethod]
    public void Ordering_is_total_and_stable_rather_than_falling_out_of_directory_enumeration()
    {
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });

        var first = catalog.Subfeatures(typeof(INetworkProbe)).Select(f => $"{f.Meta.Order}:{f.Meta.Id}").ToList();
        var again = catalog.Subfeatures(typeof(INetworkProbe)).Select(f => $"{f.Meta.Order}:{f.Meta.Id}").ToList();

        CollectionAssert.AreEqual(first, again);
        CollectionAssert.AreEqual(
            first.OrderBy(s => s, System.StringComparer.Ordinal).ToList(), first,
            "order then id — so a cheap link-layer probe always runs before an expensive management one");
    }

    [TestMethod]
    public void A_contract_no_plugin_implements_yields_an_empty_set_rather_than_an_error()
    {
        // "No plugins installed" is a legitimate state, not a defect.
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });

        Assert.AreEqual(0, catalog.Subfeatures(typeof(IDisposable)).Count);
    }

    // ── Laziness ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void A_handle_reports_itself_unloaded_until_its_value_is_touched()
    {
        // The contract a host feature actually depends on: it can list, order and filter its plugins by
        // metadata, and pays for an instance only when it reaches for one.
        //
        // Note this asserts handle-level laziness, not assembly-level: a Debug build's FeatureCatalog scan
        // deliberately loads every assembly so a broken feature fails at launch rather than on first use,
        // which would make an assembly-load assertion pass vacuously in Release and fail in Debug.
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });

        var handles = new SubfeatureCatalog(catalog).Handles(typeof(INetworkProbe), workspace: null!);

        Assert.IsTrue(handles.Count > 0);
        foreach (ISubfeatureHandle<INetworkProbe> h in handles)
        {
            Assert.IsFalse(h.IsLoaded, $"{h.Id} must not be instantiated merely by being listed");
            Assert.IsFalse(string.IsNullOrWhiteSpace(h.DisplayName), "…yet its metadata is already available");
        }
    }

    [TestMethod]
    public void The_handle_list_is_typed_so_it_can_be_injected_directly()
    {
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });

        IList handles = new SubfeatureCatalog(catalog).Handles(typeof(INetworkProbe), workspace: null!);

        Assert.IsInstanceOfType<IReadOnlyList<ISubfeatureHandle<INetworkProbe>>>(handles,
            "the DI assigns this straight into an IReadOnlyList<ISubfeatureHandle<T>> constructor parameter");
    }
}
