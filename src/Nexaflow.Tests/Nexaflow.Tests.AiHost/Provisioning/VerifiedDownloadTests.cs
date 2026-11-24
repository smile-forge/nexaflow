using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Provisioning;

/// <summary>
/// A download whose size and hash are known: resumed where an earlier attempt stopped, and at once where its connection drops,
/// started over when the server will not resume, given up when it keeps dropping gaining nothing or is refused, and kept only when
/// it is the file it should be.
/// </summary>
[TestClass]
[CoversNode("ai-model-assets")]
public class VerifiedDownloadTests
{
    private static readonly byte[] Body = [.. Enumerable.Range(0, 300_000).Select(i => (byte)(i * 31))];
    private static readonly string BodyHash = Convert.ToHexStringLower(SHA256.HashData(Body));
    private static readonly Uri Source = new("https://example.test/model.bin");

    private string _dir = null!;
    private string Destination => Path.Combine(_dir, "models", "model.bin");

    [TestInitialize] public void Init() => _dir = Path.Combine(TestTemp.Root, "nexaflow-test-" + Guid.NewGuid().ToString("N"));
    [TestCleanup]    public void Done() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    /// <summary>Serves <see cref="Body"/>, honouring a range request when <paramref name="resumes"/>.</summary>
    private sealed class Server(bool resumes, byte[]? body = null) : HttpMessageHandler
    {
        public List<long?> RangesAsked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var bytes = body ?? Body;
            var from  = request.Headers.Range?.Ranges.Single().From;
            RangesAsked.Add(from);

            var response = resumes && from is long start
                ? new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[(int)start..]) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            return Task.FromResult(response);
        }
    }

    private void LeavePartial(int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        File.WriteAllBytes(Destination + ".partial", Body[..bytes]);
    }

    [TestMethod]
    public async Task AFreshDownload_ArrivesWhole_AndTakesItsName()
    {
        var progress = new List<DownloadProgress>();
        await VerifiedDownload.DownloadAsync(new HttpClient(new Server(resumes: true)), Source, Destination, Body.Length, BodyHash,
            new SyncProgress(progress.Add));

        CollectionAssert.AreEqual(Body, File.ReadAllBytes(Destination));
        Assert.IsFalse(File.Exists(Destination + ".partial"));
        Assert.AreEqual(Body.Length, progress[^1].Received);
    }

    [TestMethod]
    public async Task AnInterruptedDownload_ResumesFromWhereItStopped()
    {
        LeavePartial(120_000);
        var server = new Server(resumes: true);

        await VerifiedDownload.DownloadAsync(new HttpClient(server), Source, Destination, Body.Length, BodyHash);

        CollectionAssert.AreEqual(new long?[] { 120_000 }, server.RangesAsked);
        CollectionAssert.AreEqual(Body, File.ReadAllBytes(Destination), "the kept bytes and the rest make the file");
    }

    /// <summary>Serves <see cref="Body"/> from where each request asks, dropping the connection after <paramref name="dropAfter"/> bytes of
    /// a response — for <paramref name="drops"/> responses, then serving whole — or refusing every request with <paramref name="refusal"/>.</summary>
    private sealed class Dropping(int dropAfter, int drops, HttpStatusCode? refusal = null) : HttpMessageHandler
    {
        private int _served;

        public List<long?> RangesAsked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var from = request.Headers.Range?.Ranges.Single().From;
            RangesAsked.Add(from);
            if (refusal is { } status) return Task.FromResult(new HttpResponseMessage(status));
            byte[] rest = Body[(int)(from ?? 0)..];
            Stream body = _served++ < drops ? new DroppedAfter(rest, dropAfter) : new MemoryStream(rest);
            var response = new HttpResponseMessage(from is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = new StreamContent(body) };
            return Task.FromResult(response);
        }
    }

    /// <summary>A response body whose connection is dropped after <paramref name="count"/> bytes, as a server resetting it leaves one.</summary>
    private sealed class DroppedAfter(byte[] bytes, int count) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int length)
        {
            if (Position >= count) throw new IOException("An existing connection was forcibly closed by the remote host.");
            return base.Read(buffer, offset, (int)Math.Min(length, count - Position));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (Position >= count) throw new IOException("An existing connection was forcibly closed by the remote host.");
            return base.ReadAsync(buffer[..(int)Math.Min(buffer.Length, count - Position)], ct);
        }
    }

    /// <summary>A clock whose waits are over at once: the pauses between passes, without their time.</summary>
    private sealed class NoWaiting : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new Fired();
        }

        private sealed class Fired : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }

    [TestMethod]
    public async Task AConnectionDroppedMidDownload_IsResumedAtOnce_InTheSameDownload()
    {
        var server = new Dropping(dropAfter: 100_000, drops: 2);

        await VerifiedDownload.DownloadAsync(new HttpClient(server), Source, Destination, Body.Length, BodyHash, time: new NoWaiting());

        CollectionAssert.AreEqual(new long?[] { null, 100_000, 200_000 }, server.RangesAsked, "each pass asks for what the last did not bring");
        CollectionAssert.AreEqual(Body, File.ReadAllBytes(Destination));
    }

    [TestMethod]
    public async Task AConnectionThatDropsGainingNothing_IsGivenUp_AfterTheLimit_KeepingWhatCame()
    {
        LeavePartial(120_000);
        var server = new Dropping(dropAfter: 0, drops: int.MaxValue);

        await Assert.ThrowsAsync<IOException>(() => VerifiedDownload.DownloadAsync(new HttpClient(server), Source, Destination, Body.Length, BodyHash,
                                                                                   time: new NoWaiting()));

        Assert.AreEqual(VerifiedDownload.FruitlessLimit, server.RangesAsked.Count);
        Assert.AreEqual(120_000, new FileInfo(Destination + ".partial").Length, "what came is kept for the next download to resume");
    }

    [TestMethod]
    public async Task AServerThatRefusesIt_IsNotAskedAgain()
    {
        var server = new Dropping(0, 0, refusal: HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(() => VerifiedDownload.DownloadAsync(new HttpClient(server), Source, Destination, Body.Length, BodyHash,
                                                                                            time: new NoWaiting()));

        Assert.AreEqual(1, server.RangesAsked.Count, "a refusal is the answer, not a dropped connection");
    }

    [TestMethod]
    public async Task AServerThatWillNotResume_IsDownloadedFromTheStart()
    {
        LeavePartial(120_000);

        await VerifiedDownload.DownloadAsync(new HttpClient(new Server(resumes: false)), Source, Destination, Body.Length, BodyHash);

        CollectionAssert.AreEqual(Body, File.ReadAllBytes(Destination));
    }

    [TestMethod]
    public async Task APartialAlreadyWhole_IsCheckedWithoutAsking()
    {
        LeavePartial(Body.Length);
        var server = new Server(resumes: true);

        await VerifiedDownload.DownloadAsync(new HttpClient(server), Source, Destination, Body.Length, BodyHash);

        Assert.AreEqual(0, server.RangesAsked.Count);
        Assert.IsTrue(File.Exists(Destination));
    }

    [TestMethod]
    public async Task TheWrongFile_IsRefused_AndNothingIsKept()
    {
        var other = Body.Select(b => (byte)~b).ToArray();

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            VerifiedDownload.DownloadAsync(new HttpClient(new Server(resumes: true, other)), Source, Destination, Body.Length, BodyHash));

        Assert.IsFalse(File.Exists(Destination));
        Assert.IsFalse(File.Exists(Destination + ".partial"), "a wrong file would poison the next resume");
    }

    /// <summary>Reports on the caller's thread, so a test reads what was reported as soon as the download returns.</summary>
    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
