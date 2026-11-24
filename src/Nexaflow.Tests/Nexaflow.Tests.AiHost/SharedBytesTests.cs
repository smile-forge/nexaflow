using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Large blocks of bytes between an app and its host: small ones in their frame, large ones in a shared section only the host
/// makes, open to the app's account and its own, read in place at both ends and closed when the request that named them ends.
/// </summary>
[TestClass]
[DoNotParallelize]   // the measurements time and count what one transfer costs, with nothing beside it
[SupportedOSPlatform("windows")]
[CoversNode("ai-host")]
public class SharedBytesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(30_000)]
    public async Task ABlockUpToTheLimit_CrossesInItsFrame_AndALargerOne_IsReadInPlace_InASectionTheHostMade()
    {
        await using var joined = await Joined.OpenAsync();
        using var small = joined.HostSections.Create(SharedBytes.InlineLimit);
        var large = joined.HostSections.Create(SharedBytes.InlineLimit + 1);
        Assert.IsNull(small.SectionId, "a block up to the limit crosses in its frame");
        Assert.IsNotNull(large.SectionId, "past it, a section");
        large.Span[^1] = 7;

        var receiving = joined.App.ReceiveAsync();
        await joined.Host.SendAsync(new Drawn(1, 1, 1, large));
        var drawn = (Drawn)(await receiving)!;

        Assert.ThrowsExactly<ObjectDisposedException>(() => large.Span[0], "sent, the section is the app's: the host reads and writes it no more");
        using (drawn.Rgb)
        {
            Assert.AreEqual((large.SectionId, large.Length), (drawn.Rgb.SectionId, drawn.Rgb.Length));
            Assert.AreEqual(7, drawn.Rgb.Span[^1], "the app reads what the host wrote, where it wrote it");
        }
        Assert.ThrowsExactly<ObjectDisposedException>(() => drawn.Rgb.Span[0], "disposed, the block goes back to be reused");
        var release = (ReleaseSection)(await joined.Host.ReceiveAsync())!;
        Assert.AreEqual(large.SectionId, release.Id, "done with it, the app gives it back");
        joined.HostSections.Release(release.Id);
        Assert.ThrowsExactly<InvalidDataException>(() => joined.HostSections.Read(release.Id, large.SectionName!, large.Length, large.Capacity),
                                                   "given back, the app holds it no more, and may not name it");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ASectionGivenBack_IsReusedForTheNextBlockItHolds_TheAppsViewOfItWithIt()
    {
        await using var joined = await Joined.OpenAsync();
        var first = await RoundTripAsync(joined, 3 << 20, 1);
        var second = await RoundTripAsync(joined, (3 << 20) - 4096, 2);
        var larger = await RoundTripAsync(joined, 8 << 20, 3);

        Assert.AreEqual(first, second, "a block a little smaller goes in the section the last one left");
        Assert.AreNotEqual(first, larger, "one past what it holds gets a section of its own");
    }

    [TestMethod]
    public void ASection_IsMadeAQuarterLargerAtMost_SoPicturesOfAboutOneSizeShareIt()
    {
        Assert.AreEqual(5 << 20, HostSections.Capacity(5_000_000), "within the quarters of 4 MiB");
        Assert.AreEqual(HostSections.Capacity(4_800_000), HostSections.Capacity(5_200_000));
        foreach (int length in new[] { (1 << 20) + 1, 3_000_001, 64 << 20, 1_000_000_007 })
        {
            int capacity = HostSections.Capacity(length);
            Assert.IsTrue(capacity >= length && capacity <= length + length / 4 + Environment.SystemPageSize, $"{length} is made at {capacity}");
        }
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ASectionIdleAWhile_IsClosed_AtTheHostAndThenAtTheApp()
    {
        var clock = new SteppingClock();
        await using var joined = await Joined.OpenAsync(clock);
        long id = await RoundTripAsync(joined, 3 << 20, 1);
        string name = joined.HostSections.Allocate(3 << 20)!.SectionName!;
        joined.HostSections.Release(id);

        joined.HostSections.Trim();
        Assert.AreEqual(id, joined.HostSections.Allocate(3 << 20)!.SectionId, "idle a moment, it is kept and reused");
        joined.HostSections.Release(id);
        clock.Step(HostSections.IdleClose);
        var telling = joined.App.ReceiveAsync();
        joined.HostSections.Trim();
        var closed = (SectionClosed)(await telling)!;
        joined.AppSections.Closed(closed.Id);

        Assert.AreEqual(id, closed.Id);
        Assert.ThrowsExactly<System.ComponentModel.Win32Exception>(() => SharedSection.Open(name, HostSections.Capacity(3 << 20)), "closed at both ends, the section is gone");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ABlockTheAppStillReads_KeepsItsView_PastTheConnection_UntilItIsDisposed()
    {
        await using var joined = await Joined.OpenAsync();
        var made = joined.HostSections.Create(3 << 20);
        made.Span[100] = 9;
        var receiving = joined.App.ReceiveAsync();
        await joined.Host.SendAsync(new Drawn(1, 1, 1, made));
        var drawn = (Drawn)(await receiving)!;

        joined.AppSections.Lose("the host went");
        joined.HostSections.Dispose();

        Assert.AreEqual(9, drawn.Rgb.Span[100], "what the app holds stays readable while it holds it");
        string name = drawn.Rgb.SectionName!;
        drawn.Rgb.Dispose();
        Assert.ThrowsExactly<System.ComponentModel.Win32Exception>(() => SharedSection.Open(name, 3 << 20), "its last block disposed, the view goes too");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task AnAppsBlock_IsWrittenInASectionTheHostMadeForIt_AndReadThereByTheHost()
    {
        await using var joined = await Joined.OpenAsync();
        int length = SharedBytes.InlineLimit * 3;

        var allocating = joined.AppSections.AllocateAsync(length, CancellationToken.None);
        var asked = (AllocateSection)(await joined.Host.ReceiveAsync())!;
        var made = joined.HostSections.Allocate(asked.Length)!;
        var answering = joined.App.ReceiveAsync();
        await joined.Host.SendAsync(new SectionAllocated(asked.Ticket, made.SectionId!.Value, made.SectionName!, made.Capacity));
        joined.AppSections.Answer(asked.Ticket, (await answering)!);
        using var written = await allocating;
        written.Span[12345] = 42;

        var receiving = joined.Host.ReceiveAsync();
        await joined.App.SendAsync(new Drawn(1, 1, 1, written));
        var drawn = (Drawn)(await receiving)!;

        Assert.AreEqual(made.SectionId, drawn.Rgb.SectionId, "the host finds the section in its own table, by its id, never by a name it is sent");
        Assert.AreEqual(42, drawn.Rgb.Span[12345]);
        CollectionAssert.AreEqual(new[] { drawn.Rgb }, joined.Host.Sections.ToArray(), "the message names it, so the request it starts holds it");
    }

    [TestMethod]
    public void ARequestsSections_GoBackWhenItEnds_ThoseItMadeButNeverSentToo_ThoseItSentAreTheApps()
    {
        using var sections = new HostSections(WindowsIdentity.GetCurrent().User!, TimeProvider.System, _ => Task.CompletedTask);
        var lent = sections.Allocate(SharedBytes.InlineLimit + 1)!;
        var named = sections.Read(lent.SectionId!.Value, lent.SectionName!, lent.Length, lent.Capacity);
        var request = new RequestSections(sections);
        request.Take([named]);
        SharedBytes sent, unsent;
        using (request.Enter())
        {
            sent = RequestSections.Create(SharedBytes.InlineLimit + 1);
            unsent = RequestSections.Create(SharedBytes.InlineLimit + 1);
        }
        new HostChannel(new MemoryStream(), sections).SendAsync(new Drawn(1, 1, 1, sent)).GetAwaiter().GetResult();

        request.End();

        Assert.ThrowsExactly<InvalidDataException>(() => sections.Read(named.SectionId!.Value, named.SectionName!, named.Length, named.Capacity), "what the request was sent goes back with it");
        Assert.ThrowsExactly<InvalidDataException>(() => sections.Read(unsent.SectionId!.Value, unsent.SectionName!, unsent.Length, unsent.Capacity), "what it made and never sent too");
        Assert.AreEqual(sent.SectionId, sections.Read(sent.SectionId!.Value, sent.SectionName!, sent.Length, sent.Capacity).SectionId, "what it sent is the app's to give back");
        Assert.ThrowsExactly<ObjectDisposedException>(() => named.Span[0], "back in the pool, it is read through the request's bytes no more");
    }

    [TestMethod]
    public void ASection_IsOpenToTheHostsAccountAndTheAppsAlone()
    {
        var app = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
        using var sections = new HostSections(app, TimeProvider.System, _ => Task.CompletedTask);
        using var made = sections.Create(SharedBytes.InlineLimit + 1);

        var descriptor = DescriptorOf(made.SectionName!);

        Assert.IsTrue(descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected), "nothing inherited from the namespace");
        var allowed = descriptor.DiscretionaryAcl!.Cast<CommonAce>().Where(a => a.AceType == AceType.AccessAllowed).Select(a => a.SecurityIdentifier).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { WindowsIdentity.GetCurrent().User!, app }, allowed.ToArray());
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task BytesInASectionOfAnotherConnection_AreRefused_NotSentAsANameThatHostNeverMade()
    {
        await using var first = await Joined.OpenAsync();
        await using var second = await Joined.OpenAsync();
        using var made = first.HostSections.Create(SharedBytes.InlineLimit + 1);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => second.Host.SendAsync(new Drawn(1, 1, 1, made)));
    }

    [TestMethod]
    [Timeout(120_000)]
    public Task A64MegabytePicture_CrossesFasterInASection_ThanThroughThePipe_AndIsNeverCopiedIntoManagedMemory()
        => CompareAsync(64 << 20);

    /// <summary>A picture at the size of the largest the tools take: half a gigabyte held twice over in managed memory by the pipe.</summary>
    [TestMethod]
    [TestCategory("Interactive")]
    [Timeout(300_000)]
    public Task A512MegabytePicture_CrossesFasterInASection_ThanThroughThePipe_AndIsNeverCopiedIntoManagedMemory()
        => CompareAsync(512 << 20);

    /// <summary>Where a section starts to pay for itself: what a block costs either way at the sizes either side of the limit, each way
    /// run once at the size before it is timed.</summary>
    [TestMethod]
    [Timeout(120_000)]
    public async Task AroundTheLimit_EachWayIsMeasured()
    {
        await using var joined = await Joined.OpenAsync();
        foreach (int size in new[] { 256 << 10, (1 << 20) + 1, 4 << 20, 16 << 20 })
        {
            await ByPipeAsync(joined, size);
            await BySectionAsync(joined, size);
            var pipe = await ByPipeAsync(joined, size);
            var section = await BySectionAsync(joined, size);
            TestContext.WriteLine($"{size >> 10,6} KiB  pipe {pipe.Time.TotalMilliseconds,7:0.00} ms {pipe.Allocated >> 10,7} KiB managed   " +
                                  $"section {section.Time.TotalMilliseconds,7:0.00} ms {section.Allocated >> 10,7} KiB managed");
        }
    }

    /// <summary>Each way timed as it runs once its memory is in use — the pipe's arrays from the managed heap, the section from the pool, each
    /// end's view kept — after a first transfer at the size has paid for its pages.</summary>
    private async Task CompareAsync(int size)
    {
        await using var joined = await Joined.OpenAsync();
        await ByPipeAsync(joined, size);
        await BySectionAsync(joined, size);

        var pipe = await ByPipeAsync(joined, size);
        var section = await BySectionAsync(joined, size);

        TestContext.WriteLine($"{size >> 20} MiB: pipe {pipe.Time.TotalMilliseconds:0.0} ms, {pipe.Allocated >> 20} MiB managed; " +
                              $"section {section.Time.TotalMilliseconds:0.0} ms, {section.Allocated >> 10} KiB managed");
        Assert.IsTrue(section.Time * 2 < pipe.Time, $"a section ({section.Time.TotalMilliseconds:0.0} ms) is at least twice as fast as the pipe ({pipe.Time.TotalMilliseconds:0.0} ms)");
        Assert.IsTrue(pipe.Allocated >= size, "through the pipe, the picture is held in managed memory");
        Assert.IsTrue(section.Allocated < size / 64, $"in a section it is not: {section.Allocated >> 10} KiB managed");
    }

    /// <summary>A picture the host makes, sent through the pipe in its frame, read whole by the app.</summary>
    private static async Task<(TimeSpan Time, long Allocated)> ByPipeAsync(Joined joined, int size)
    {
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var bytes = new byte[size];
        bytes.AsSpan().Fill(3);
        bytes[^1] = 7;
        var receiving = joined.App.ReceiveAsync();
        await joined.Host.SendAsync(new Drawn(1, 1, 1, SharedBytes.Of(bytes)));
        var drawn = (Drawn)(await receiving)!;
        int odd = FirstNotThree(drawn.Rgb.Span);
        watch.Stop();
        Assert.AreEqual(size - 1, odd, "every byte as it was written, the last a 7");
        return (watch.Elapsed, GC.GetTotalAllocatedBytes(precise: true) - before);
    }

    /// <summary>A picture the host makes in a section, its name sent through the pipe, read whole by the app in place, then given back.</summary>
    private static async Task<(TimeSpan Time, long Allocated)> BySectionAsync(Joined joined, int size)
    {
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var made = joined.HostSections.Create(size);
        made.Span.Fill(3);
        made.Span[^1] = 7;
        var receiving = joined.App.ReceiveAsync();
        await joined.Host.SendAsync(new Drawn(1, 1, 1, made));
        var drawn = (Drawn)(await receiving)!;
        int odd = FirstNotThree(drawn.Rgb.Span);
        drawn.Rgb.Dispose();
        if (drawn.Rgb.SectionId is not null) joined.HostSections.Release(((ReleaseSection)(await joined.Host.ReceiveAsync())!).Id);
        watch.Stop();
        Assert.AreEqual(size - 1, odd, "every byte as it was written, the last a 7");
        return (watch.Elapsed, GC.GetTotalAllocatedBytes(precise: true) - before);
    }

    /// <summary>A block of <paramref name="size"/> bytes the host makes, the app reads and gives back; the section it went in.</summary>
    private static async Task<long> RoundTripAsync(Joined joined, int size, byte mark)
    {
        var made = joined.HostSections.Create(size);
        made.Span[^1] = mark;
        var receiving = joined.App.ReceiveAsync();
        await joined.Host.SendAsync(new Drawn(1, 1, 1, made));
        var drawn = (Drawn)(await receiving)!;
        Assert.AreEqual(mark, drawn.Rgb.Span[size - 1]);
        drawn.Rgb.Dispose();
        long id = ((ReleaseSection)(await joined.Host.ReceiveAsync())!).Id;
        joined.HostSections.Release(id);
        return id;
    }

    /// <summary>Real time, stepped on at will.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private long _ahead;

        public void Step(TimeSpan by) => Interlocked.Add(ref _ahead, (long)(by.TotalSeconds * TimestampFrequency));

        public override long GetTimestamp() => base.GetTimestamp() + Interlocked.Read(ref _ahead);
    }

    /// <summary>Every byte read, as the runtime's vectorised search reads them — as fast in a debug build as in a release, so neither way's
    /// time is the test's own loop: where the first byte that is not the picture's 3 lies.</summary>
    private static int FirstNotThree(ReadOnlySpan<byte> bytes) => bytes.IndexOfAnyExcept((byte)3);

    /// <summary>The security descriptor section <paramref name="name"/> was made with.</summary>
    private static RawSecurityDescriptor DescriptorOf(string name)
    {
        using var section = OpenFileMappingW(ReadControl, false, name);
        if (section.IsInvalid) throw new System.ComponentModel.Win32Exception();
        uint failed = GetSecurityInfo(section, KernelObject, DaclInformation, IntPtr.Zero, IntPtr.Zero, out _, IntPtr.Zero, out var descriptor);
        if (failed != 0) throw new System.ComponentModel.Win32Exception((int)failed);
        try
        {
            var bytes = new byte[GetSecurityDescriptorLength(descriptor)];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            return new RawSecurityDescriptor(bytes, 0);
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private const uint ReadControl = 0x00020000;
    private const int KernelObject = 6;
    private const uint DaclInformation = 4;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle OpenFileMappingW(uint access, bool inherit, string name);

    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityInfo(SafeHandle handle, int objectType, uint information, IntPtr owner, IntPtr group, out IntPtr dacl, IntPtr sacl,
                                               out IntPtr descriptor);

    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    /// <summary>An app and a host joined by a pipe, the host making sections open to the app's account as a host does.</summary>
    private sealed class Joined : IAsyncDisposable
    {
        public required HostChannel App { get; init; }

        public required HostChannel Host { get; init; }

        public required HostSections HostSections { get; init; }

        public required ClientSections AppSections { get; init; }

        public static async Task<Joined> OpenAsync(TimeProvider? clock = null)
        {
            string name = "nexaflow-test-" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                                                   SharedBytes.InlineLimit, SharedBytes.InlineLimit);
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync());
            HostChannel? app = null;
            var appSections = new ClientSections(message => app!.SendAsync(message));
            app = new HostChannel(server, appSections);
            HostChannel? host = null;
            var hostSections = new HostSections(WindowsIdentity.GetCurrent().User!, clock ?? TimeProvider.System, message => host!.SendAsync(message));
            host = new HostChannel(client, hostSections);
            return new Joined { App = app, Host = host, HostSections = hostSections, AppSections = appSections };
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await Host.DisposeAsync();
            HostSections.Dispose();
        }
    }
}
