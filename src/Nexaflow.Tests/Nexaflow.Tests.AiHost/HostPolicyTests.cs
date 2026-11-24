using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The host's policy file: what it says, what it leaves to the host, what it grants whom, and a file the host would misread;
/// and the log it keeps, model calls only where the file asks for them.</summary>
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
        Assert.IsFalse(policy.LogsModelCalls, "what the host does for its apps is kept only where the administrator asks for it");
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
              "logModelCalls": true,
              "idleUnloadMinutes": 5,
              "grants": { "S-1-5-21-1": "Interactive", "S-1-5-21-1/Indexer": "Job" },
            }
            """));

        Assert.AreEqual((30, @"C:\Logs\AiHost", true, TimeSpan.FromMinutes(5)), (policy.MemoryShare, policy.LogFolder, policy.LogsModelCalls, policy.IdleUnload));
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

    [TestMethod]
    public void ALogNotKeepingModelCalls_KeepsTheHostsStartStopAndErrors_AndNothingItDidForAnApp()
    {
        string folder = TestTemp.CreateSubdirectory("nexaflow-host-log-").FullName;
        using (var log = new HostLog(folder, modelCalls: false))
        {
            log.Write("service started");
            log.WriteActivity("S-1-5-21-1/Nexaflow connected");
            log.WriteActivity("loaded gemma-4-e4b-it/chat on GPU 0");
            log.Write("Generate #4 for S-1-5-21-1/Nexaflow failed: the model is not in the store");
        }

        string kept = File.ReadAllText(Directory.GetFiles(folder).Single());
        StringAssert.Contains(kept, "service started");
        StringAssert.Contains(kept, "failed: the model is not in the store");
        Assert.IsFalse(kept.Contains("connected") || kept.Contains("loaded"), $"no model call is kept: {kept}");
    }

    [TestMethod]
    public void ALogKeepingModelCalls_KeepsWhatTheHostDidForItsApps_Too()
    {
        string folder = TestTemp.CreateSubdirectory("nexaflow-host-log-").FullName;
        using (var log = new HostLog(folder, modelCalls: true))
            log.WriteActivity("loaded gemma-4-e4b-it/chat on GPU 0");

        StringAssert.Contains(File.ReadAllText(Directory.GetFiles(folder).Single()), "loaded gemma-4-e4b-it/chat on GPU 0");
    }
}
