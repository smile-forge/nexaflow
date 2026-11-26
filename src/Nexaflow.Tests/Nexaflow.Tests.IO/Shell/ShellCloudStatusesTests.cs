using System.Collections.Concurrent;
using Nexaflow.IO.Common.Cloud;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Shell;

/// <summary>
/// A sync provider's word on the rows on screen: read once per question and kept, read again when the row's listing or
/// revision moves on, never handed to a row that has moved on, and a provider with nothing to say answers no one.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[CoversNode("vcommon-cloud-status")]
public class ShellCloudStatusesTests
{
    private const string Report = @"C:\cloud\report.docx";

    private readonly HeldAnswers _thread = new();

    [TestMethod]
    public void AFileIsReadOnce_AndThenAnsweredAtOnce()
    {
        var provider = new FakeProvider { [Report] = new(CloudFileState.Error, CloudSharing.Shared) };
        var statuses = Statuses(provider);

        var first = Ask(statuses, Report, CloudFileState.Available, revision: 0, out var arrived);
        Assert.IsNull(first.Status);
        Assert.IsNotNull(first.Pending);
        _thread.PumpUntil(() => arrived.Count == 1);

        var again = Ask(statuses, Report, CloudFileState.Available, revision: 0, out _);
        Assert.AreEqual(new CloudFileStatus(CloudFileState.Error, CloudSharing.Shared), again.Status);
        Assert.IsNull(again.Pending, "kept, so a row scrolled back to is not read again");
        Assert.AreEqual(1, provider.Reads.Count);
    }

    [TestMethod]
    public void ANewListingOrRevision_IsReadAgain()
    {
        var provider = new FakeProvider { [Report] = new(CloudFileState.Available, CloudSharing.None) };
        var statuses = Statuses(provider);

        Ask(statuses, Report, CloudFileState.Available, revision: 0, out var first);
        _thread.PumpUntil(() => first.Count == 1);

        Assert.IsNotNull(Ask(statuses, Report, CloudFileState.Available, revision: 1, out var second).Pending,
                         "the shell said the file changed");
        _thread.PumpUntil(() => second.Count == 1);
        Assert.IsNotNull(Ask(statuses, Report, CloudFileState.OnlineOnly, revision: 1, out _).Pending,
                         "its listing changed");
    }

    [TestMethod]
    public void ARowThatMovesOn_IsNeverAnswered()
    {
        var provider = new FakeProvider
        {
            [Report] = new(CloudFileState.Error, CloudSharing.None),
            [@"C:\cloud\other.txt"] = new(CloudFileState.Available, CloudSharing.None),
        };
        var statuses = Statuses(provider);

        var left = Ask(statuses, Report, CloudFileState.Available, revision: 0, out var leftArrived);
        left.Pending!.Dispose();
        Ask(statuses, @"C:\cloud\other.txt", CloudFileState.Available, revision: 0, out var stayed);
        _thread.PumpUntil(() => stayed.Count == 1);

        Assert.AreEqual(0, leftArrived.Count);
        CollectionAssert.DoesNotContain(provider.Reads.ToArray(), Report, "and its provider is not asked");
    }

    [TestMethod]
    public void AProviderWithNothingToSay_AnswersNoOne_AndIsNotAskedAgain()
    {
        var provider = new FakeProvider();
        var statuses = Statuses(provider);

        Ask(statuses, Report, CloudFileState.Available, revision: 0, out var arrived);
        _thread.PumpUntil(() => provider.Reads.Count == 1, "the provider was not asked");
        _thread.PumpFor(TimeSpan.FromMilliseconds(150));

        Assert.AreEqual(0, arrived.Count, "the row keeps what its listing said");
        var again = Ask(statuses, Report, CloudFileState.Available, revision: 0, out _);
        Assert.IsNull(again.Status);
        Assert.IsNull(again.Pending);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static ShellCloudStatuses Statuses(FakeProvider provider) =>
        new(provider, quiet: TimeSpan.FromMilliseconds(30), patience: TimeSpan.FromMilliseconds(100), capacity: 64);

    private CloudStatusLookup Ask(ShellCloudStatuses statuses, string path, CloudFileState listed, long revision,
                                  out List<CloudFileStatus> arrived)
    {
        var landed = new List<CloudFileStatus>();
        arrived = landed;
        return statuses.Request(new CloudStatusQuery(path, listed, revision), _thread, landed.Add);
    }

    /// <summary>A provider that knows the files it is given, and records each it is asked about.</summary>
    private sealed class FakeProvider : Dictionary<string, CloudFileStatus>, ICloudStatusSource
    {
        public ConcurrentQueue<string> Reads { get; } = new();

        public CloudFileStatus? Read(string path)
        {
            Reads.Enqueue(path);
            return TryGetValue(path, out var status) ? status : null;
        }
    }
}
