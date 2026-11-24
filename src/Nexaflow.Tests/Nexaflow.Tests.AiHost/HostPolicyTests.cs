using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The host's policy file: what it says, what it leaves to the host, what it grants whom, and a file the host would misread.</summary>
[TestClass]
[CoversNode("ai-host")]
public class HostPolicyTests
{
    private static string Write(string json)
    {
        string path = Path.Combine(TestTemp.CreateSubdirectory("nexaflow-policy-").FullName, HostPolicy.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    [TestMethod]
    public void NoFile_IsTheHostsOwnDefaults()
    {
        var policy = HostPolicy.Read(Path.Combine(TestTemp.CreateSubdirectory("nexaflow-policy-").FullName, HostPolicy.FileName));

        Assert.AreEqual((null, null, HostPolicy.DefaultIdleUnload), (policy.MemoryShare, policy.LogFolder, policy.IdleUnload));
        Assert.AreEqual(RequestPriority.Voice, policy.HighestFor(new ClientIdentity("S-1-5-21-1", "Any")), "every priority, for everyone");
    }

    [TestMethod]
    public void AFile_SaysTheShareTheLogTheIdleTime_AndGrantsAnAppBeforeItsAccount()
    {
        var policy = HostPolicy.Read(Write("""
            {
              // a comment, as an administrator writes one
              "memoryShare": 30,
              "logFolder": "C:\\Logs\\AiHost",
              "idleUnloadMinutes": 5,
              "grants": { "S-1-5-21-1": "Interactive", "S-1-5-21-1/Indexer": "Job" },
            }
            """));

        Assert.AreEqual((30, @"C:\Logs\AiHost", TimeSpan.FromMinutes(5)), (policy.MemoryShare, policy.LogFolder, policy.IdleUnload));
        Assert.AreEqual(RequestPriority.Job, policy.HighestFor(new ClientIdentity("S-1-5-21-1", "Indexer")), "its app's grant");
        Assert.AreEqual(RequestPriority.Interactive, policy.HighestFor(new ClientIdentity("S-1-5-21-1", "Assistant")), "its account's");
        Assert.AreEqual(RequestPriority.Voice, policy.HighestFor(new ClientIdentity("S-1-5-21-2", "Assistant")), "another account's: everything");
    }

    [TestMethod]
    [DataRow("""{ "memoryShare": 95 }""")]
    [DataRow("""{ "idleUnloadMinutes": -1 }""")]
    [DataRow("""{ "memoryshares": 30 }""")]
    [DataRow("""{ "grants": { "S-1-5-21-1": "Urgent" } }""")]
    [DataRow("not json")]
    public void AFileTheHostWouldMisread_IsRefused(string json)
        => Assert.ThrowsExactly<InvalidDataException>(() => HostPolicy.Read(Write(json)));

    [TestMethod]
    public void AClient_AskingAboveItsGrant_IsServedAtIt_AndBelowAtWhatItAsked()
    {
        var client = new ClientIdentity("S-1-5-21-1", "Indexer") { Highest = RequestPriority.Background };

        Assert.AreEqual(RequestPriority.Background, client.Granted(RequestPriority.Voice));
        Assert.AreEqual(RequestPriority.Job, client.Granted(RequestPriority.Job));
    }
}
