using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// Progress and cancellation on the two archive operations a file action reaches: extracting one and
/// building one. Both had neither, so a zip of any size froze whatever called it with nothing to look
/// at and no way to stop it.
/// </summary>
[TestClass]
[CoversNode("archive-progress-cancellation")]
public class ArchiveProgressTests
{
    private VirtualFileSystem _vfs = null!;
    private string _work = string.Empty;
    private readonly List<string> _temps = [];

    [TestInitialize]
    public void Setup()
    {
        // Isolated, never the process-wide Instance: these tests write archives.
        _vfs  = new VirtualFileSystem(NewTempDir("nexa-arcprog-temps-"));
        _vfs.RegisterHandler(new ZipTestHandler());
        _work = NewTempDir("nexa-arcprog-");
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var d in _temps)
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
    }

    private string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        _temps.Add(dir);
        return dir;
    }

    /// <summary>A source folder of <paramref name="sizes"/> files, each filled with that many bytes.</summary>
    private string SourceFolder(params int[] sizes)
    {
        var dir = Path.Combine(_work, "src" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        for (int i = 0; i < sizes.Length; i++)
            File.WriteAllBytes(Path.Combine(dir, $"f{i}.bin"), new byte[sizes[i]]);
        return dir;
    }

    private string BuildArchive(params int[] sizes)
    {
        var src  = SourceFolder(sizes);
        var dest = Path.Combine(_work, "a" + Guid.NewGuid().ToString("N")[..6] + ".zip");
        _vfs.CreateArchive(dest, src);
        return dest;
    }

    // ── Extract ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ExtractingReportsItsWayToTheTotalItAnnounced()
    {
        var archive = BuildArchive(1000, 2000, 3000);
        var dest    = Path.Combine(_work, "out");

        var reports = new List<TransferProgress>();
        _vfs.ExtractAll(archive, dest, new Probe(reports.Add));

        var running = reports.Where(r => r.Phase == TransferPhase.Running).ToList();
        Assert.IsTrue(running.Count > 0, "the run should report at least once past the scan");
        Assert.AreEqual(6000, running[0].BytesTotal, "the totals are known before the first byte moves");
        Assert.AreEqual(3, running[0].ItemsTotal);

        // Monotonic: a bar that goes backwards reads as a bug even when the operation is fine.
        for (int i = 1; i < reports.Count; i++)
            Assert.IsTrue(reports[i].BytesDone >= reports[i - 1].BytesDone,
                $"report {i} went backwards: {reports[i - 1].BytesDone} then {reports[i].BytesDone}");

        var last = reports[^1];
        Assert.AreEqual(TransferPhase.Finished, last.Phase);
        Assert.AreEqual(6000, last.BytesDone, "a finished extract has moved everything it said it would");
        Assert.AreEqual(3, last.ItemsDone);
    }

    [TestMethod]
    public void ExtractingSaysItIsMeasuringBeforeItSaysHowMuch()
    {
        var archive = BuildArchive(500);
        var reports = new List<TransferProgress>();

        _vfs.ExtractAll(archive, Path.Combine(_work, "out"), new Probe(reports.Add));

        Assert.AreEqual(TransferPhase.Scanning, reports[0].Phase,
            "reading a large archive's directory takes seconds, so it must not look like a stall");
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildingAnArchiveReportsAgainstTheSourceBytes()
    {
        var src  = SourceFolder(4000, 6000);
        var dest = Path.Combine(_work, "built.zip");

        var reports = new List<TransferProgress>();
        _vfs.CreateArchive(dest, src, new Probe(reports.Add));

        Assert.IsTrue(File.Exists(dest));
        var running = reports.Where(r => r.Phase == TransferPhase.Running).ToList();
        Assert.IsTrue(running.Count > 0);
        Assert.AreEqual(10000, running[0].BytesTotal, "FileEntry's stat supplies the length, so the bar fills");
        Assert.AreEqual(2, running[0].ItemsTotal);
        Assert.AreEqual(10000, reports[^1].BytesDone);
    }

    /// <summary>
    /// Stopping partway must keep what has already landed and take only the file that was mid-write —
    /// a truncated leftover is worse than either outcome.
    /// <para>
    /// The stop is tripped from the session rather than from a progress report, because reports are
    /// time-throttled: in a test this quick the only ones published are the forced phase changes, so a
    /// probe watching for "one item done" would wait for something that never arrives.
    /// </para>
    /// </summary>
    [TestMethod]
    public void StoppingAnExtractKeepsWhatAlreadyLandedAndDropsWhatDidNot()
    {
        var archive = BuildArchive(1000, 1000, 1000);
        var dest    = Path.Combine(_work, "out");

        using var cts = new CancellationTokenSource();
        var vfs = new VirtualFileSystem(NewTempDir("nexa-arcprog-trip-"));
        vfs.RegisterHandler(new TripWireHandler(tripOnEntry: 1, cts));

        Assert.ThrowsExactly<OperationCanceledException>(
            () => vfs.ExtractAll(archive, dest, null, cts.Token));

        var landed = Directory.GetFiles(dest);
        Assert.AreEqual(1, landed.Length,
            "the entry that finished is kept; the one interrupted mid-write is removed, not left truncated");
        Assert.AreEqual(1000, new FileInfo(landed[0]).Length, "a kept file is a whole file");
    }

    [TestMethod]
    public void StoppingABuildLeavesNeitherAnArchiveNorATemp()
    {
        var src  = SourceFolder(1000, 1000, 1000);
        var dest = Path.Combine(_work, "abandoned.zip");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(
            () => _vfs.CreateArchive(dest, src, null, cts.Token));

        Assert.IsFalse(File.Exists(dest), "a cancelled build must not publish a half-filled archive");
        Assert.AreEqual(0, Directory.GetFiles(_work, "*.nexatmp").Length,
            "the temp is the build's to clean up — leaving one turns every stop into litter");
    }

    [TestMethod]
    public void StoppingABuildOverAnExistingArchiveLeavesTheOldOneAlone()
    {
        var src  = SourceFolder(1000, 1000, 1000);
        var dest = Path.Combine(_work, "existing.zip");
        File.WriteAllBytes(dest, [1, 2, 3, 4]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(
            () => _vfs.CreateArchive(dest, src, null, cts.Token));

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(dest),
            "the swap only happens on success, so what was already there is untouched");
        Assert.AreEqual(0, Directory.GetFiles(_work, "*.nexatmp").Length);
    }

    // ── The counting stream itself ────────────────────────────────────────────

    /// <summary>
    /// A tar writer asks its source for <see cref="Stream.Length"/> to build the entry header before it
    /// reads a byte. A forward-only wrapper therefore breaks every <c>.tar*</c> write — and does it
    /// invisibly to a zip-only suite, which is why this is asserted directly.
    /// </summary>
    [TestMethod]
    public void TheCountingStreamIsSeekAndLengthTransparent()
    {
        var payload = new byte[64];
        using var inner = new MemoryStream(payload);
        using var counted = new CountingStream(inner, _ => { }, _ => { }, CancellationToken.None);

        Assert.IsTrue(counted.CanSeek);
        Assert.IsTrue(counted.CanRead);
        Assert.AreEqual(64, counted.Length);

        Assert.AreEqual(16, counted.Seek(16, SeekOrigin.Begin));
        Assert.AreEqual(16, counted.Position);

        var buffer = new byte[8];
        Assert.AreEqual(8, counted.Read(buffer, 0, 8));
        Assert.AreEqual(24, counted.Position);
    }

    [TestMethod]
    public void TheCountingStreamStopsReadingOnceCancelled()
    {
        using var cts = new CancellationTokenSource();
        using var inner = new MemoryStream(new byte[64]);
        using var counted = new CountingStream(inner, _ => { }, _ => { }, cts.Token);

        cts.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => counted.Read(new byte[8], 0, 8));
    }

    /// <summary>
    /// A handler may open one entry twice — a second pass for a checksum, say. Summing what the reads
    /// returned would then take the bar past its own total, so an entry is credited by identity.
    /// </summary>
    [TestMethod]
    public void AnEntryOpenedTwiceIsCountedOnce()
    {
        var reports = new List<TransferProgress>();
        var reporter = new ArchiveProgressReporter(new Probe(reports.Add), CancellationToken.None);
        reporter.Measured(100, 1);

        reporter.ItemStarted("a.bin");
        reporter.ItemFinished("a.bin", 100, 100);
        reporter.ItemStarted("a.bin");
        reporter.ItemFinished("a.bin", 100, 100);
        reporter.Finished();

        Assert.AreEqual(100, reports[^1].BytesDone, "the second pass must not be counted again");
        Assert.AreEqual(1, reports[^1].ItemsDone);
    }

    /// <summary>Reports straight through, on the calling thread — no <see cref="Progress{T}"/>, whose
    /// posting to a captured context would make the assertions racy.</summary>
    private sealed class Probe(Action<TransferProgress> onReport) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => onReport(value);
    }

    /// <summary>
    /// A zip backend that trips a cancellation as the run reaches entry number
    /// <paramref name="tripOnEntry"/> + 1 — the deterministic way to stop an extraction mid-flight, since
    /// progress reports are time-throttled and a fast test never sees an intermediate one.
    /// </summary>
    private sealed class TripWireHandler(int tripOnEntry, CancellationTokenSource cts) : IArchiveHandler
    {
        private readonly ZipTestHandler _inner = new();

        public string Name => _inner.Name;
        public ArchiveCapabilities Capabilities => _inner.Capabilities;
        public bool CanHandle(string fileName) => _inner.CanHandle(fileName);

        public void Write(Stream target, string fileName, IReadOnlyList<ArchiveWriteEntry> entries,
                          ArchiveWriteOptions? options = null)
            => _inner.Write(target, fileName, entries, options);

        public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
            => new Session(_inner.Open(container, fileName, options), tripOnEntry, cts);

        private sealed class Session(IArchiveSession inner, int tripOn, CancellationTokenSource cts) : IArchiveSession
        {
            private int _opened;

            public IReadOnlyList<VirtualEntry> Entries => inner.Entries;

            public Stream OpenEntry(string entryPath)
            {
                if (++_opened > tripOn) cts.Cancel();
                return inner.OpenEntry(entryPath);
            }

            public void Dispose() => inner.Dispose();
        }
    }
}
