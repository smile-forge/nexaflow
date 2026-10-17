using System.Net;
using System.Text;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// The store against a fake linux-usb.org: what it adopts, what it refuses, and when it asks. Nothing here
/// touches the network or the user's own usb.ids folder.
/// </summary>
[TestClass]
[CoversNode("io-usb-names")]
public class UsbIdsStoreTests
{
    string _dir = "";
    readonly Clock _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    readonly Server _server = new();

    [TestInitialize]
    public void Setup() => _dir = TestTemp.CreateSubdirectory("nexaflow-usbids-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    /// <summary>A usb.ids file with enough vendors to pass for the real thing, and one name that says which copy it is.</summary>
    static string Ids(string version, string marker) =>
        $"# Version: {version}\n" + string.Concat(Enumerable.Range(0, 1200).Select(i => $"{i:x4}  Vendor {i}\n")) + $"ffff  {marker}\n";

    static UsbIdsDatabase Database(string version, string marker) => UsbIdsDatabase.Parse(new StringReader(Ids(version, marker)));

    UsbIdsStore Store(UsbIdsDatabase? bundled) => new(_dir, () => bundled, _server, _clock);

    /// <summary>A store that recognises the fake server's name-mismatch failure as the real handler's handshake record would.</summary>
    UsbIdsStore StoreSeeingNameMismatch(UsbIdsDatabase? bundled) => new(_dir, () => bundled, _server, _clock, e => e.Message == NameMismatch);

    const string NameMismatch = "RemoteCertificateNameMismatch";

    [TestMethod]
    public void NewerDownload_IsAdoptedAndAnnounced()
    {
        var bundled = Database("2026.06.26", "bundled");
        var store = Store(bundled);
        int changed = 0;
        store.Changed += (_, _) => changed++;
        _server.Respond(HttpStatusCode.OK, Ids("2026.10.10", "downloaded"), etag: "\"v2\"");

        var result = store.UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.AreEqual(UsbIdsUpdateOutcome.Updated, result.Outcome);
        Assert.AreEqual("2026.10.10", store.Current?.Version);
        Assert.AreEqual("downloaded", store.Current?.VendorName(0xFFFF));
        Assert.IsTrue(store.CurrentIsDownloaded);
        Assert.AreEqual(1, changed);
        Assert.IsTrue(File.Exists(store.DatabasePath));
        Assert.IsFalse(File.Exists(store.DatabasePath + ".tmp"));

        // A fresh process prefers the newer copy on disk over the one that shipped.
        Assert.AreEqual("downloaded", Store(bundled).Current?.VendorName(0xFFFF));
    }

    [TestMethod]
    public void OlderOrSameDownload_ChangesNothing()
    {
        var store = Store(Database("2026.06.26", "bundled"));
        _server.Respond(HttpStatusCode.OK, Ids("2026.06.26", "downloaded"));

        var result = store.UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.AreEqual(UsbIdsUpdateOutcome.AlreadyCurrent, result.Outcome);
        Assert.AreEqual("bundled", store.Current?.VendorName(0xFFFF));
        Assert.IsFalse(File.Exists(store.DatabasePath));
        Assert.IsFalse(store.IsCheckDue(), "a check that found nothing newer still counts as a check");
    }

    [TestMethod]
    public void NotADatabase_IsRefused()
    {
        var store = Store(Database("2026.06.26", "bundled"));
        _server.Respond(HttpStatusCode.OK, "<html>maintenance</html>");

        var result = store.UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.AreEqual(UsbIdsUpdateOutcome.Failed, result.Outcome);
        Assert.AreEqual("bundled", store.Current?.VendorName(0xFFFF));
        Assert.IsFalse(File.Exists(store.DatabasePath));
    }

    [TestMethod]
    public void SecondCheck_IsConditional()
    {
        var store = Store(Database("2026.06.26", "bundled"));
        _server.Respond(HttpStatusCode.OK, Ids("2026.10.10", "downloaded"), etag: "\"v2\"");
        store.UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

        _server.Respond(HttpStatusCode.NotModified, "");
        var result = store.UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.AreEqual(UsbIdsUpdateOutcome.AlreadyCurrent, result.Outcome);
        Assert.AreEqual("\"v2\"", _server.Last?.Headers.IfNoneMatch.Single().ToString());
        Assert.AreEqual(UsbIdsStore.Source, _server.Last?.RequestUri);
    }

    [TestMethod]
    public void ACertificateForAnotherHost_FallsBackToHttp_AndSaysSo()
    {
        var store = StoreSeeingNameMismatch(Database("2026.06.26", "bundled"));
        _server.HttpsFailure = NameMismatch;
        _server.Respond(HttpStatusCode.OK, Ids("2026.10.10", "downloaded"));

        var result = store.UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.AreEqual(UsbIdsUpdateOutcome.Updated, result.Outcome);
        CollectionAssert.AreEqual(new[] { UsbIdsStore.Source, UsbIdsStore.PlainSource }, _server.Asked);
        StringAssert.Contains(result.Message, "over HTTP");
        Assert.AreEqual("downloaded", store.Current?.VendorName(0xFFFF));
    }

    [TestMethod]
    public void AnyOtherCertificateFailure_IsAFailure_NotAFallback()
    {
        var store = StoreSeeingNameMismatch(Database("2026.06.26", "bundled"));
        _server.HttpsFailure = "RemoteCertificateChainErrors";
        _server.Respond(HttpStatusCode.OK, Ids("2026.10.10", "downloaded"));

        var result = store.UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.AreEqual(UsbIdsUpdateOutcome.Failed, result.Outcome);
        CollectionAssert.AreEqual(new[] { UsbIdsStore.Source }, _server.Asked, "an untrusted chain is never answered by asking over HTTP");
        Assert.AreEqual("bundled", store.Current?.VendorName(0xFFFF));
    }

    [TestMethod]
    public void CheckIsDue_MonthlyAfterSuccess_DailyAfterFailure()
    {
        var store = Store(Database("2026.06.26", "bundled"));
        Assert.IsTrue(store.IsCheckDue(), "never checked");

        _server.Respond(HttpStatusCode.OK, Ids("2026.06.26", "same"));
        store.UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();
        _clock.Advance(TimeSpan.FromDays(29));
        Assert.IsFalse(store.IsCheckDue());
        _clock.Advance(TimeSpan.FromDays(2));
        Assert.IsTrue(store.IsCheckDue(), "more than a month since the last check");

        _server.Respond(HttpStatusCode.ServiceUnavailable, "");
        Assert.AreEqual(UsbIdsUpdateOutcome.Failed, store.UpdateAsync(CancellationToken.None).GetAwaiter().GetResult().Outcome);
        Assert.IsFalse(store.IsCheckDue(), "a failure is not retried on every open");
        _clock.Advance(TimeSpan.FromDays(1.5));
        Assert.IsTrue(store.IsCheckDue());
    }

    sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    sealed class Server : HttpMessageHandler
    {
        HttpStatusCode _status = HttpStatusCode.NotFound;
        string _body = "";
        string? _etag;

        public HttpRequestMessage? Last { get; private set; }
        public List<Uri> Asked { get; } = [];

        /// <summary>Set to make every HTTPS request fail as a TLS handshake would, with this message.</summary>
        public string? HttpsFailure { get; set; }

        public void Respond(HttpStatusCode status, string body, string? etag = null) => (_status, _body, _etag) = (status, body, etag);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            Asked.Add(request.RequestUri!);
            if (HttpsFailure is { } failure && request.RequestUri!.Scheme == Uri.UriSchemeHttps)
                throw new HttpRequestException(failure, new System.Security.Authentication.AuthenticationException(failure));
            var response = new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8) };
            if (_etag is not null) response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(_etag);
            return Task.FromResult(response);
        }
    }
}
