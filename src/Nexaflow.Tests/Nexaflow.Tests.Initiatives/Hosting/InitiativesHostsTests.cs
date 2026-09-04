using System;
using System.IO;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Hosting;

/// <summary>
/// Sharing one product's live state between the surfaces looking at it.
/// <para>
/// The whole value is that the second surface to open finds the tree already loaded and the graph already
/// warm, and that closing one page does not take the state out from under another. Both halves are easy to
/// get subtly wrong and invisible when you do — the cost of a duplicate host is a slow page rather than a
/// broken one, and the cost of an early release is a page that silently goes cold.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("Shared lifetime for the Product surfaces — infrastructure, not a product-tree node.")]
public class InitiativesHostsTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nfi-hosts-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void TwoSurfacesOnOneProduct_ShareOneHost()
    {
        using var first  = InitiativesHosts.Acquire(_root, out var createdFirst);
        using var second = InitiativesHosts.Acquire(_root, out var createdSecond);

        Assert.IsTrue(createdFirst,   "the first surface is what brings the state into being");
        Assert.IsFalse(createdSecond, "and the second must join it rather than build a second copy");
        Assert.AreSame(first.Host, second.Host);
    }

    /// <summary>The half that matters most: a page closing must not cool the state another page is using.</summary>
    [TestMethod]
    public void ClosingOneSurface_LeavesTheOtherWorking()
    {
        var first  = InitiativesHosts.Acquire(_root);
        var second = InitiativesHosts.Acquire(_root);

        first.Dispose();

        Assert.IsNotNull(InitiativesHosts.Warm(_root), "one holder is still open");
        Assert.AreSame(second.Host, InitiativesHosts.Warm(_root));

        second.Dispose();
        Assert.IsNull(InitiativesHosts.Warm(_root), "and now nobody is");
    }

    /// <summary>A view-model disposed twice must not hand back a share it does not have — that would close a
    /// host somebody else is still using, which is the failure that would look like the graph going cold at
    /// random.</summary>
    [TestMethod]
    public void ALeaseDisposedTwice_OnlyReleasesOnce()
    {
        var first  = InitiativesHosts.Acquire(_root);
        var second = InitiativesHosts.Acquire(_root);

        first.Dispose();
        first.Dispose();

        Assert.IsNotNull(InitiativesHosts.Warm(_root), "the second holder's share must survive the double release");
        second.Dispose();
    }

    [TestMethod]
    public void ReopeningAfterTheLastClose_BuildsAgain()
    {
        InitiativesHosts.Acquire(_root, out _).Dispose();

        using var again = InitiativesHosts.Acquire(_root, out var created);

        Assert.IsTrue(created, "nothing was holding it, so this surface pays for it again");
    }

    /// <summary>Two products are two hosts; a lookup for one must never answer with the other's.</summary>
    [TestMethod]
    public void DifferentProducts_DoNotShare()
    {
        var other = Path.Combine(_root, "nested");
        Directory.CreateDirectory(other);

        using var here  = InitiativesHosts.Acquire(_root);
        using var there = InitiativesHosts.Acquire(other);

        Assert.AreNotSame(here.Host, there.Host);
    }

    /// <summary>The same folder written two ways is the same product — a trailing separator or a relative
    /// step is not a reason to load a second copy of everything.</summary>
    [TestMethod]
    public void TheSamePathWrittenDifferently_IsTheSameProduct()
    {
        using var plain   = InitiativesHosts.Acquire(_root);
        using var trailed = InitiativesHosts.Acquire(_root + Path.DirectorySeparatorChar);

        Assert.AreSame(plain.Host, trailed.Host);
    }

    [TestMethod]
    public void NothingHeld_IsNotWarm()
    {
        Assert.IsNull(InitiativesHosts.Warm(_root));
    }
}
