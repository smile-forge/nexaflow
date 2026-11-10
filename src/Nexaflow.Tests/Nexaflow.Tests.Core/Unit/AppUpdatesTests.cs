using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The state behind Options → About → Updates and the startup check's toast. What matters is that an offer, once
/// found, outlives what announced it — a failed re-check or a failed install never withdraws it — and that the page
/// is told to offer exactly one thing: Check for updates, or Update.
/// </summary>
[TestClass]
[CoversNode("opt-about-updates")]
public class AppUpdatesTests
{
    private static readonly AppUpdateOffer Newer = new("v9.9.9", "- everything is better");

    private sealed class FakeSource : IAppReleaseSource
    {
        private int _finds;
        private int _installs;

        public Func<Task<AppUpdateOffer?>> Find { get; set; } = () => Task.FromResult<AppUpdateOffer?>(null);
        public Func<Task> Install { get; set; } = () => Task.CompletedTask;

        public int Finds    => Volatile.Read(ref _finds);
        public int Installs => Volatile.Read(ref _installs);

        public Task<AppUpdateOffer?> FindNewerAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _finds);
            return Find();
        }

        public Task InstallAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _installs);
            return Install();
        }
    }

    private static AppUpdates Create(FakeSource source) => new(source, action => action(), (_, _) => { });

    private static async Task<AppUpdates> Offering(FakeSource source)
    {
        source.Find = () => Task.FromResult<AppUpdateOffer?>(Newer);
        var updates = Create(source);
        await updates.CheckAsync();
        return updates;
    }

    [TestMethod]
    public void BeforeAnyCheck_ItOffersACheck()
    {
        var status = Create(new FakeSource()).Status;

        Assert.AreEqual(AppUpdateStage.Unchecked, status.Stage);
        Assert.IsTrue(status.CanCheck);
        Assert.IsFalse(status.IsOffered);
        Assert.IsFalse(status.CanInstall);
    }

    [TestMethod]
    public async Task ACheckThatFindsNothing_SaysThisBuildIsCurrent()
    {
        var updates = Create(new FakeSource());

        var offer = await updates.CheckAsync();

        Assert.IsNull(offer);
        Assert.AreEqual(AppUpdateStage.Current, updates.Status.Stage);
        Assert.IsTrue(updates.Status.CanCheck);
        Assert.IsFalse(updates.Status.IsOffered);
    }

    [TestMethod]
    public async Task ACheckThatFindsARelease_OffersItForInstall()
    {
        var source  = new FakeSource();
        var updates = await Offering(source);

        var status = updates.Status;
        Assert.AreEqual(AppUpdateStage.Available, status.Stage);
        Assert.AreEqual(Newer, status.Offer);
        Assert.IsTrue(status.IsOffered);
        Assert.IsTrue(status.CanInstall);
        Assert.IsFalse(status.CanCheck, "while a release is offered, Update replaces Check for updates");
    }

    [TestMethod]
    public async Task ACheckThatCannotReachTheReleases_SaysSo_AndCanBeTriedAgain()
    {
        var updates = Create(new FakeSource { Find = () => throw new HttpRequestFailed() });

        var offer = await updates.CheckAsync();

        Assert.IsNull(offer);
        Assert.AreEqual(AppUpdateStage.CheckFailed, updates.Status.Stage);
        Assert.IsTrue(updates.Status.CanCheck);
    }

    [TestMethod]
    public async Task AFailedReCheck_KeepsTheOfferAnEarlierCheckFound()
    {
        var source  = new FakeSource();
        var updates = await Offering(source);

        source.Find = () => throw new HttpRequestFailed();
        var offer = await updates.CheckAsync();

        Assert.AreEqual(Newer, offer);
        Assert.AreEqual(AppUpdateStage.Available, updates.Status.Stage);
        Assert.AreEqual(Newer, updates.Status.Offer);
    }

    [TestMethod]
    public async Task ChecksAskedWhileOneRuns_ShareIt()
    {
        var gate    = new TaskCompletionSource<AppUpdateOffer?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source  = new FakeSource { Find = () => gate.Task };
        var updates = Create(source);

        var first  = updates.CheckAsync();
        var second = updates.CheckAsync();
        Assert.AreSame(first, second);
        Assert.AreEqual(AppUpdateStage.Checking, updates.Status.Stage);
        Assert.IsFalse(updates.Status.CanCheck, "a check under way is not offered again");

        gate.SetResult(Newer);
        await first;

        Assert.AreEqual(1, source.Finds);
    }

    [TestMethod]
    public async Task InstallWithNothingOffered_DoesNothing()
    {
        var source  = new FakeSource();
        var updates = Create(source);
        await updates.CheckAsync();

        await updates.InstallAsync();

        Assert.AreEqual(0, source.Installs);
        Assert.AreEqual(AppUpdateStage.Current, updates.Status.Stage);
    }

    [TestMethod]
    public async Task AnInstallUnderWay_IsShownAndNotStartedTwice_AndNoCheckInterrupts()
    {
        var source  = new FakeSource();
        var updates = await Offering(source);

        var handedOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Install = () => handedOver.Task;

        var installing = updates.InstallAsync();
        Assert.AreEqual(AppUpdateStage.Installing, updates.Status.Stage);
        Assert.IsTrue(updates.Status.IsOffered);
        Assert.IsFalse(updates.Status.CanInstall);

        await updates.InstallAsync();
        Assert.AreEqual(Newer, await updates.CheckAsync());
        Assert.AreEqual(1, source.Installs);
        Assert.AreEqual(1, source.Finds, "a check asked during an install reads nothing");

        handedOver.SetResult();
        await installing;
    }

    [TestMethod]
    public async Task AFailedInstall_KeepsTheOffer_AndCanBeTriedAgain()
    {
        var source  = new FakeSource();
        var updates = await Offering(source);
        source.Install = () => throw new HttpRequestFailed();

        await updates.InstallAsync();

        var status = updates.Status;
        Assert.AreEqual(AppUpdateStage.InstallFailed, status.Stage);
        Assert.AreEqual(Newer, status.Offer);
        Assert.IsTrue(status.IsOffered);
        Assert.IsTrue(status.CanInstall);

        source.Install = () => Task.CompletedTask;
        await updates.InstallAsync();
        Assert.AreEqual(2, source.Installs);
    }

    [TestMethod]
    public async Task AFailure_KeepsItsReason_IsReported_AndAWorkingCheckClearsIt()
    {
        var reported = new List<string>();
        var source   = new FakeSource { Find = () => throw new HttpRequestFailed() };
        var updates  = new AppUpdates(source, action => action(), (what, ex) => reported.Add($"{what}: {ex.Message}"));

        await updates.CheckAsync();

        Assert.AreEqual("the releases could not be reached", updates.Status.Failure);
        CollectionAssert.AreEqual(new[] { "checking for updates: the releases could not be reached" }, reported);

        source.Find = () => Task.FromResult<AppUpdateOffer?>(Newer);
        await updates.CheckAsync();
        Assert.IsNull(updates.Status.Failure);

        source.Install = () => throw new HttpRequestFailed();
        await updates.InstallAsync();
        Assert.AreEqual("the releases could not be reached", updates.Status.Failure);
        Assert.AreEqual("installing update v9.9.9: the releases could not be reached", reported[^1]);
    }

    [TestMethod]
    public async Task EveryChange_IsRaisedThroughTheUiHop()
    {
        var hops    = 0;
        var raised  = 0;
        var updates = new AppUpdates(new FakeSource { Find = () => Task.FromResult<AppUpdateOffer?>(Newer) },
                                     action => { Interlocked.Increment(ref hops); action(); },
                                     (_, _) => { });
        updates.Changed += (_, _) => Interlocked.Increment(ref raised);

        await updates.CheckAsync();     // Checking, then Available
        await updates.InstallAsync();   // Installing

        Assert.AreEqual(3, raised);
        Assert.AreEqual(raised, hops);
    }

    /// <summary>What a source throws when the releases cannot be reached.</summary>
    private sealed class HttpRequestFailed() : Exception("the releases could not be reached");
}
