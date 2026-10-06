using Nexaflow.IO.Hdf5;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Hdf5;

/// <summary>
/// The open-file cache: one open per unchanged file however many leases, a close once the last lease has
/// lingered, and a fresh open when the file changes underneath.
/// </summary>
[TestClass]
[CoversNode("hdf5-source-cache")]
public sealed class Hdf5SourceCacheTests
{
    private string _file = null!;
    private int _opens;

    [TestInitialize]
    public void CopySample()
    {
        _file = Path.Combine(Path.GetTempPath(), $"nexaflow-hdf5-cache-{Guid.NewGuid():N}.h5");
        File.Copy(TestSampleData.Path("hdf5", "experiment.h5"), _file);
    }

    [TestCleanup]
    public void DeleteSample() => File.Delete(_file);

    private Hdf5SourceCache Cache(TimeSpan linger) => new(linger, open: path => { _opens++; return Hdf5Source.Open(path); });

    [TestMethod]
    public void LeasesOnAnUnchangedFile_ShareOneOpenSource()
    {
        using var cache = Cache(TimeSpan.FromMinutes(1));
        using var a = cache.Acquire(_file);
        using var b = cache.Acquire(_file);

        Assert.AreSame(a.Source, b.Source);
        Assert.AreEqual(1, _opens);
    }

    [TestMethod]
    public void ASourceLingersAfterItsLastLease_ThenCloses()
    {
        using var cache = Cache(TimeSpan.Zero);
        var lease = cache.Acquire(_file);
        var source = lease.Source;
        lease.Dispose();
        lease.Dispose();

        cache.Sweep();
        Assert.AreEqual(0, cache.OpenCount);
        Assert.ThrowsExactly<ObjectDisposedException>(() => source.ListChildren("/", 0, 1));
    }

    [TestMethod]
    public void ALeasedSource_IsNeverSwept()
    {
        using var cache = Cache(TimeSpan.Zero);
        using var lease = cache.Acquire(_file);
        cache.Sweep();
        Assert.AreEqual(1, cache.OpenCount);
        Assert.IsNotNull(lease.Source.Stat("/calibration"));
    }

    [TestMethod]
    public void AChangedFile_OpensAfresh()
    {
        using var cache = Cache(TimeSpan.FromMinutes(1));
        using var before = cache.Acquire(_file);
        File.SetLastWriteTimeUtc(_file, DateTime.UtcNow.AddMinutes(1));
        using var after = cache.Acquire(_file);

        Assert.AreNotSame(before.Source, after.Source);
        Assert.AreEqual(2, _opens);
    }

    [TestMethod]
    public void AFileStream_JoinsTheCacheByPath_AndIsClosed()
    {
        using var cache = Cache(TimeSpan.FromMinutes(1));
        using var byPath = cache.Acquire(_file);
        var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var byStream = cache.Acquire(stream);

        Assert.AreSame(byPath.Source, byStream.Source);
        Assert.IsFalse(stream.CanRead, "the handed-over stream is closed");
    }

    [TestMethod]
    public void AnyOtherStream_IsItsOwnSource_ClosedWithItsLease()
    {
        using var cache = Cache(TimeSpan.FromMinutes(1));
        var lease = cache.Acquire(new MemoryStream(File.ReadAllBytes(_file)));
        var source = lease.Source;
        Assert.AreEqual(0, cache.OpenCount);

        lease.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => source.ListChildren("/", 0, 1));
    }
}
