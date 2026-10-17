using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// Two pages on one USB tree share one inventory: built once, started once, told about every change, and let go with
/// the last page that held it.
/// </summary>
[TestClass]
[CoversNode("io-usb-inventory")]
public class UsbInventoryShareTests
{
    sealed class Counting : IUsbDeviceInventory
    {
        public int Starts, Disposals;
        public TaskCompletionSource FirstPass { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public UsbInventorySnapshot Snapshot { get; set; } = UsbInventorySnapshot.Empty;
        public event EventHandler<UsbInventoryChangedEventArgs>? Changed;

        public Task StartAsync(CancellationToken ct)
        {
            Starts++;
            return FirstPass.Task;
        }

        public Task<UsbInventorySnapshot> RefreshAsync(CancellationToken ct) => Task.FromResult(Snapshot);
        public Task<UsbDeviceNode?> RefreshDeviceAsync(string instanceId, CancellationToken ct) => Task.FromResult<UsbDeviceNode?>(null);
        public Task<UsbInventorySnapshot> UseNamesAsync(UsbIdsDatabase? names, CancellationToken ct) => Task.FromResult(Snapshot);
        public void Raise() => Changed?.Invoke(this, new UsbInventoryChangedEventArgs(Snapshot, []));
        public void Dispose() => Disposals++;
    }

    [TestMethod]
    public async Task TwoPages_ShareOneInventory_StartedOnce()
    {
        var share = new UsbInventoryShare();
        int built = 0;
        var inner = new Counting();
        IUsbDeviceInventory Build() { built++; return inner; }

        using var first = share.Acquire(Build);
        using var second = share.Acquire(Build);
        var starting = Task.WhenAll(first.StartAsync(CancellationToken.None), second.StartAsync(CancellationToken.None));
        inner.FirstPass.SetResult();
        await starting;

        Assert.AreEqual(1, built, "one inventory for the machine");
        Assert.AreEqual(1, inner.Starts, "and one first pass, however many pages wait on it");
        Assert.AreEqual(2, share.Holders);
    }

    [TestMethod]
    public void EveryHolder_HearsEveryChange_UntilItLetsGo()
    {
        var share = new UsbInventoryShare();
        var inner = new Counting();
        var first = share.Acquire(() => inner);
        using var second = share.Acquire(() => inner);
        int firstHeard = 0, secondHeard = 0;
        first.Changed += (_, _) => firstHeard++;
        second.Changed += (_, _) => secondHeard++;

        inner.Raise();
        first.Dispose();
        inner.Raise();

        Assert.AreEqual(1, firstHeard, "a page that closed hears nothing more");
        Assert.AreEqual(2, secondHeard);
        Assert.AreEqual(0, inner.Disposals, "the other page still holds it");
    }

    [TestMethod]
    public void TheLastToLetGo_DisposesIt_AndTheNextPageBuildsAnew()
    {
        var share = new UsbInventoryShare();
        var inner = new Counting();
        var a = share.Acquire(() => inner);
        var b = share.Acquire(() => inner);
        a.Dispose();
        a.Dispose();   // twice is once
        b.Dispose();

        Assert.AreEqual(1, inner.Disposals);
        Assert.AreEqual(0, share.Holders);

        var next = new Counting();
        using var c = share.Acquire(() => next);
        Assert.AreEqual(0, next.Disposals, "a page opened later gets a fresh inventory, not the disposed one");
    }

    [TestMethod]
    public async Task APageClosedWhileStarting_DoesNotCancelTheOthersStart()
    {
        var share = new UsbInventoryShare();
        var inner = new Counting();
        using var closing = new CancellationTokenSource();
        var first = share.Acquire(() => inner);
        using var second = share.Acquire(() => inner);

        var abandoned = first.StartAsync(closing.Token);
        var waiting = second.StartAsync(CancellationToken.None);
        closing.Cancel();
        first.Dispose();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => abandoned);

        inner.FirstPass.SetResult();
        await waiting;
        Assert.AreEqual(1, inner.Starts);
    }
}
