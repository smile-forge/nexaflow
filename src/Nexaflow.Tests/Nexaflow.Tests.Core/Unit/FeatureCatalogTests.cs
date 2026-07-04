using System.Collections.Generic;
using System.Linq;
using Nexaflow.Core;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
public class FeatureCatalogTests
{
    // ── The regression that broke the action bar + git viewlet ─────────────────
    // Feature configs register only when their assembly is lazily activated, which can happen AFTER the
    // config view was handed to a registry (e.g. while it discovers actions across features). A snapshot
    // would miss those; the live view must see them.
    [TestMethod]
    public void LiveFeatureConfigView_SeesConfigsRegisteredAfterCreation()
    {
        var global = new Dictionary<Type, IFeatureConfig>();
        var view   = new LiveFeatureConfigView(global, workspace: null);

        Assert.IsFalse(view.ContainsKey(typeof(SampleConfig)), "config not registered yet");

        // Simulate an assembly activating and registering its global config after the view was built.
        global[typeof(SampleConfig)] = new SampleConfig();

        Assert.IsTrue(view.ContainsKey(typeof(SampleConfig)), "live view must reflect later registrations");
        Assert.IsTrue(view.TryGetValue(typeof(SampleConfig), out var cfg) && cfg is SampleConfig);
    }

    // ── Discovery resolves types across feature assemblies (loading them on demand) ──
    [TestMethod]
    public void FeatureCatalog_ResolvesFolderViewletsIncludingGit()
    {
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });   // no-op activation: isolate discovery/resolution

        var viewlets = catalog.TypesImplementing<IFolderViewlet>();

        Assert.IsTrue(viewlets.Count > 0, "expected folder viewlets to be discovered");
        Assert.IsTrue(
            viewlets.Any(t => t.FullName == "Nexaflow.Features.Git.Viewlets.GitViewlet"),
            "the Git folder viewlet must be resolvable from a different (lazily loaded) assembly");
    }

    [TestMethod]
    public void FeatureCatalog_ResolvesFileActions()
    {
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });

        Assert.IsTrue(catalog.TypesImplementing<IFileAction>().Count > 0,
            "file actions must be discoverable so the action bar can populate");
    }

    private sealed class SampleConfig : IFeatureConfig
    {
        public string ConfigName  => "sample-test";
        public string FriendlyName => "Sample";
    }
}
