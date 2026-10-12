using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Nexaflow.IO.Common.Downloads;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// A download whose size and hash are known: resumed where an earlier attempt stopped, started over when the server
/// will not resume, and kept only when it is the file it should be.
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

    [TestInitialize] public void Init() => _dir = Path.Combine(Path.GetTempPath(), "nexaflow-test-" + Guid.NewGuid().ToString("N"));
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
