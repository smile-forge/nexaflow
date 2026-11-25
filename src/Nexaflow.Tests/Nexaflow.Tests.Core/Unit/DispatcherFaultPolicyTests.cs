using System;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Whether a fault the UI thread's last-resort handler caught is still worth handling: always, until the same fault has recurred
/// so often in so short a window that handling it is what keeps it coming — a render-pass fault re-throws on every frame — and
/// the process is let die rather than spin.
/// </summary>
[TestClass]
[CoversNode("crash-log")]
public class DispatcherFaultPolicyTests
{
    private DateTimeOffset _now = new(2026, 8, 27, 11, 16, 9, TimeSpan.Zero);

    private DispatcherFaultPolicy NewPolicy() => new(() => _now);

    private static Exception Faulted(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (Exception ex) { return ex; }
    }

    [TestMethod]
    public void ARenderLoop_StopsBeingHandled()
    {
        var policy = NewPolicy();

        for (int i = 1; i < DispatcherFaultPolicy.LiveLockCount; i++)
            Assert.IsNull(policy.Observe(Faulted("render loop")), $"occurrence {i} should still be handled");

        var locked = policy.Observe(Faulted("render loop"));
        Assert.IsNotNull(locked, "past the live-lock threshold the process must be allowed to die rather than spin");
        Assert.AreEqual(DispatcherFaultPolicy.LiveLockCount, locked.Value.Repeats);
    }

    [TestMethod]
    public void ASlowDrip_IsNotALiveLock()
    {
        var policy = NewPolicy();
        for (int i = 0; i < DispatcherFaultPolicy.LiveLockCount + 10; i++)
        {
            Assert.IsNull(policy.Observe(Faulted("occasional")), "an occasional fault stays survivable");
            _now = _now.AddMinutes(1);   // well outside the burst window
        }
    }

    [TestMethod]
    public void DifferentFaults_AreNotOneLiveLock()
    {
        var policy = NewPolicy();
        for (int i = 0; i < DispatcherFaultPolicy.LiveLockCount; i++)
        {
            Assert.IsNull(policy.Observe(Faulted("alpha")));
            Assert.IsNull(policy.Observe(Faulted("beta")));
        }
    }

    [TestMethod]
    public void AfterALiveLock_TheNextFaultStartsAfresh()
    {
        var policy = NewPolicy();
        for (int i = 0; i < DispatcherFaultPolicy.LiveLockCount; i++) policy.Observe(Faulted("render loop"));

        Assert.IsNull(policy.Observe(Faulted("render loop")), "a restart, or a handler that carried on, starts counting again");
    }
}
