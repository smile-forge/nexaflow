using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Core;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// <see cref="FeatureManager.Instantiate"/> never returns null: every way it can fail is a wiring defect, and
/// each one throws with enough detail to act on.
/// <para>
/// Returning null instead is how a feature disappears. The caller enumerating implementations skips it,
/// nothing throws, nothing logs, and the symptom arrives months later as "that never worked". A missing
/// <see cref="WorkspaceRuntime.AiService"/> is no exception to this: <c>WorkspaceManager.BootstrapServices</c>
/// builds one unconditionally, so a workspace with no AI provider configured still has a service that answers
/// as much. Null there means the runtime isn't live, not that the user skipped setup.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("feature DI failure semantics — no single product node")]
public class FeatureDiFailureTests
{
    [ClassInitialize]
    public static void Init(TestContext _) => FeatureManager.Instance.RegisterFeatures();

    // ── Defects are loud ──────────────────────────────────────────────────────

    [TestMethod]
    public void UnknownConstructorParameter_Throws_NamingTheTypeAndParameter()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => FeatureManager.Instance.Instantiate(typeof(NeedsSomethingUninjectable), new WorkspaceRuntime()));

        // The message has to carry enough to act on: whoever hits this is looking at a feature that isn't
        // there, with no other clue as to why.
        StringAssert.Contains(ex.Message, nameof(NeedsSomethingUninjectable));
        StringAssert.Contains(ex.Message, "connectionString");
    }

    [TestMethod]
    public void UnregisteredGlobalConfig_Throws()
    {
        // The exact shape that let PdfTextExtractor look healthy while resolving to nothing: a config type
        // that is never registered because no assembly activation ever produced it.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => FeatureManager.Instance.Instantiate(typeof(NeedsAnUnregisteredConfig), new WorkspaceRuntime()));

        StringAssert.Contains(ex.Message, nameof(UnregisteredConfig));
        StringAssert.Contains(ex.Message, "not registered");
    }

    [TestMethod]
    public void NoPublicConstructor_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => FeatureManager.Instance.Instantiate(typeof(NoPublicCtor), new WorkspaceRuntime()));

        StringAssert.Contains(ex.Message, "no public constructor");
    }

    [TestMethod]
    public void ThrowingConstructor_PropagatesTheOriginalException()
    {
        // Not wrapped in TargetInvocationException, and not swallowed: the feature author needs their own
        // exception and their own stack, not "your feature doesn't exist".
        var ex = Assert.ThrowsExactly<NotSupportedException>(
            () => FeatureManager.Instance.Instantiate(typeof(ExplodingCtor), new WorkspaceRuntime()));

        Assert.AreEqual("deliberate", ex.Message);
    }

    [TestMethod]
    public void HalfWiredRuntime_Throws_RatherThanBuildingAgainstIt()
    {
        // A runtime whose services aren't set is pre-bootstrap or post-teardown. A feature built against it
        // would capture nulls and misbehave somewhere far away from the cause, so it's caught here instead.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => FeatureManager.Instance.Instantiate(typeof(NeedsAiService), new WorkspaceRuntime()));

        StringAssert.Contains(ex.Message, "BootstrapServices");
    }

    [TestMethod]
    public void ViableConstructor_IsPreferredOverAnUnsatisfiableOne()
    {
        // One broken overload must not sentence a type that is perfectly buildable another way.
        var built = FeatureManager.Instance.Instantiate(typeof(TwoCtorsOneViable), WiredRuntime());

        Assert.IsInstanceOfType<TwoCtorsOneViable>(built);
    }

    [TestMethod]
    public void SuccessfulBuild_NeverReturnsNull()
    {
        Assert.IsNotNull(FeatureManager.Instance.Instantiate(typeof(NeedsAiService), WiredRuntime()));
    }

    // ── The change is safe against the real feature set ───────────────────────

    [TestMethod]
    public void EveryShippedImplementationOfAnEnumeratedContract_Builds()
    {
        // The regression guard for making Instantiate throw at all. Both contracts below are resolved by
        // enumerating every implementation across the shipped feature assemblies, so a single unbuildable type
        // would now take out file search or the object-handoff chain for everyone.
        var workspace = WiredRuntime();
        var defects = new List<string>();

        foreach (var type in Contracts().SelectMany(FeatureCatalog.Instance.TypesImplementing))
        {
            try { FeatureManager.Instance.Instantiate(type, workspace); }
            catch (Exception ex) { defects.Add($"{type.FullName}: {ex.Message}"); }
        }

        Assert.AreEqual(0, defects.Count, string.Join("\n", defects));
    }

    private static IEnumerable<Type> Contracts() =>
        [typeof(IFileTextExtractor), typeof(IGenericObjectHandler)];

    /// <summary>
    /// A runtime in the state the app hands to features. <c>WorkspaceManager.BootstrapServices</c> is private,
    /// so this sets the same two services it sets — unconditionally, exactly as the real one does for a
    /// workspace with no AI provider configured.
    /// </summary>
    private static WorkspaceRuntime WiredRuntime()
    {
        var ws = new WorkspaceRuntime();
        Set(nameof(WorkspaceRuntime.ShellServices), new ShellServices(ws));
        Set(nameof(WorkspaceRuntime.AiService),
            new AIService(ws, Path.Combine(Path.GetTempPath(), "nexa-di-conv")));
        return ws;

        void Set(string property, object value) =>
            typeof(WorkspaceRuntime).GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(ws, [value]);
    }

    // ── Doubles ──────────────────────────────────────────────────────────────

    private sealed class NeedsSomethingUninjectable
    {
        public NeedsSomethingUninjectable(string connectionString) => _ = connectionString;
    }

    private sealed class UnregisteredConfig : IFeatureConfig
    {
        // Never activated by the catalog, so FeatureManager never registers it.
        public string ConfigName   => "never-registered-test-config";
        public string FriendlyName => "Never Registered";
    }

    private sealed class NeedsAnUnregisteredConfig
    {
        public NeedsAnUnregisteredConfig(UnregisteredConfig config) => _ = config;
    }

    private sealed class NoPublicCtor
    {
        private NoPublicCtor() { }
    }

    private sealed class ExplodingCtor
    {
        public ExplodingCtor() => throw new NotSupportedException("deliberate");
    }

    private sealed class NeedsAiService
    {
        public NeedsAiService(IAIService ai) => _ = ai;
    }

    private sealed class TwoCtorsOneViable
    {
        public TwoCtorsOneViable(IShellServices shell, string nonsense) { _ = shell; _ = nonsense; }
        public TwoCtorsOneViable(IShellServices shell) => _ = shell;
    }
}
