using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The merge behind Options → About → System components.
/// <para>
/// The interesting behaviour is not "can it call Probe" — it is what happens when two features want the same
/// component, when a declaration is broken, and when a probe throws. All three decide whether the About page
/// tells the truth, and the last two decide whether one bad feature can blank the whole list.
/// </para>
/// </summary>
[TestClass]
[CoversNode("system-components")]
public class ExternalDependencyRegistryTests
{
    // ── Declarations under test ───────────────────────────────────────────

    private class Fake(string id, ExternalDependencyKind kind, ExternalDependencyStatus status)
        : IExternalDependency
    {
        public string Id          => id;
        public string DisplayName => $"{id} display";
        public string Description => $"{id} description";
        public ExternalDependencyKind Kind => kind;
        public string? InstallUrl => null;
        public ExternalDependencyStatus Probe() => status;
    }

    private static ExternalDependencyStatus Present(string? v = null)
        => new(ExternalDependencyState.Present, v);
    private static ExternalDependencyStatus Missing()
        => new(ExternalDependencyState.Missing);

    private sealed class AlphaRequired()
        : Fake("shared-thing", ExternalDependencyKind.Required, Missing()), IExternalDependency;

    private sealed class BetaOptional()
        : Fake("shared-thing", ExternalDependencyKind.Optional, Missing()), IExternalDependency;

    private sealed class Standalone()
        : Fake("standalone", ExternalDependencyKind.Optional, Present("1.2.3")), IExternalDependency;

    /// <summary>A declaration whose probe throws — a bug in the check, not evidence about the machine.</summary>
    private sealed class ThrowingProbe : IExternalDependency
    {
        public string Id          => "throwing";
        public string DisplayName => "Throwing";
        public string Description => "";
        public ExternalDependencyKind Kind => ExternalDependencyKind.Required;
        public string? InstallUrl => null;
        public ExternalDependencyStatus Probe() => throw new InvalidOperationException("probe blew up");
    }

    /// <summary>No parameterless constructor, so it cannot be built at all.</summary>
#pragma warning disable CS9113 // the unused parameter IS the point: it removes the parameterless ctor
    private sealed class Unconstructable(string requiredArgument) : IExternalDependency
#pragma warning restore CS9113
    {
        public string Id          => "unconstructable";
        public string DisplayName => "Unconstructable";
        public string Description => "";
        public ExternalDependencyKind Kind => ExternalDependencyKind.Required;
        public string? InstallUrl => null;
        public ExternalDependencyStatus Probe() => Present();
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void TwoFeaturesDeclaringTheSameComponent_MergeIntoOneRow()
    {
        var reports = ExternalDependencyRegistry.BuildReports([typeof(AlphaRequired), typeof(BetaOptional)]);

        // One runtime, listed once. Listing it twice would read as two separate things to go and install —
        // which is exactly what the PDF reader and the Web tab would produce for WebView2 without this.
        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual("shared-thing", reports.Single().Id);
    }

    [TestMethod]
    public void RequiredWins_WhenOneFeatureNeedsItAndAnotherMerelyPrefersIt()
    {
        // Order must not matter: whichever declaration is met first, a component something is broken without
        // has to render as Required — the optional declarer downgrading it would hide a real fault.
        foreach (var order in new[]
                 {
                     new[] { typeof(AlphaRequired), typeof(BetaOptional) },
                     new[] { typeof(BetaOptional), typeof(AlphaRequired) },
                 })
        {
            var shared = ExternalDependencyRegistry.BuildReports(order).Single(r => r.Id == "shared-thing");
            Assert.AreEqual(ExternalDependencyKind.Required, shared.Kind);
            Assert.IsTrue(shared.IsBlocking, "required + missing is what the About page flags");
        }
    }

    [TestMethod]
    public void AThrowingProbe_ReportsUnknown_NotMissing()
    {
        var report = ExternalDependencyRegistry.BuildReports([typeof(ThrowingProbe)]).Single();

        // "Missing" would be a lie: the check failed, so nothing was learned about the machine. It would also
        // put a red row on About — and, via the page-load error panel, blame this component for unrelated
        // crashes.
        Assert.AreEqual(ExternalDependencyState.Unknown, report.Status.State);
        Assert.IsFalse(report.IsBlocking);
        StringAssert.Contains(report.Status.Detail ?? "", "probe blew up");
    }

    [TestMethod]
    public void ABrokenDeclaration_IsSkipped_WithoutLosingTheRest()
    {
        var reports = ExternalDependencyRegistry.BuildReports(
            [typeof(Unconstructable), typeof(Standalone)]);

        // One feature shipping an un-instantiable declaration must not blank the whole About list.
        Assert.IsFalse(reports.Any(r => r.Id == "unconstructable"));
        Assert.AreEqual("1.2.3", reports.Single(r => r.Id == "standalone").Status.DetectedVersion);
    }

    [TestMethod]
    public void BlockingComponentsSortFirst()
    {
        var reports = ExternalDependencyRegistry.BuildReports(
            [typeof(Standalone), typeof(AlphaRequired)]);

        // The page exists to answer "what is wrong with this PC", so the answer goes at the top.
        Assert.AreEqual("shared-thing", reports[0].Id);
    }

    [TestMethod]
    public void StatusOf_AnUndeclaredId_IsUnknown()
    {
        // The pre-flight contract: features must be able to ask about anything and get a safe "carry on"
        // rather than a false "missing" that would suppress a viewer that would have worked.
        var status = ExternalDependencyRegistry.Instance.StatusOf("nothing-declares-this");
        Assert.AreEqual(ExternalDependencyState.Unknown, status.State);
    }

    [TestMethod]
    public void RequiredBy_NamesTheDeclaringFeature()
    {
        var label = ExternalDependencyRegistry.BuildReports([typeof(Standalone)]).Single().RequiredByLabel;

        // Derived from the declaring assembly, so a user can map an unfamiliar component name onto the part
        // of the app that wants it. (Here that assembly is the test suite itself.)
        Assert.IsFalse(string.IsNullOrWhiteSpace(label));
        StringAssert.Contains(label, "Tests");
    }
}
