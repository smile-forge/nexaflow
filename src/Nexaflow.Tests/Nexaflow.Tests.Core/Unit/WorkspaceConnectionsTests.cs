using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The connections a workspace keeps: saved with it through the versioned config store, carried over from an earlier
/// version's file, edited without their id ever changing, and their secrets kept in Credential Manager — never in the
/// file — and forgotten with them.
/// </summary>
[TestClass]
[CoversNode("io-locations-connections")]
public sealed class WorkspaceConnectionsTests
{
    private string _dir = string.Empty;
    private RecordingSecrets _secrets = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir     = TestTemp.CreateSubdirectory("nexa-connections-").FullName;
        _secrets = new RecordingSecrets();
    }

    private sealed class RecordingSecrets : ISecretStore
    {
        public readonly Dictionary<string, (string? User, string Secret)> Stored = [];
        public string? Read(string target) => Stored.TryGetValue(target, out var s) ? s.Secret : null;
        public void Write(string target, string? userName, string secret) => Stored[target] = (userName, secret);
        public bool Delete(string target) => Stored.Remove(target);
    }

    private static readonly ConnectionProfile Nas = new("sftp-nas", "sftp", "NAS", "nas")
    {
        Port    = 2222,
        User    = "adam",
        Root    = "/home/adam",
        Options = new Dictionary<string, string> { ["keyFile"] = @"C:\Users\adam\.ssh\id_ed25519" },
    };

    private static readonly ConnectionProfile Share = new("smb-docs", "smb", "Docs", "fileserver");

    private WorkspaceConnections Open() => new(_dir, _secrets);

    [TestMethod]
    public void ASavedConnectionIsThereWhenTheWorkspaceLoadsAgain()
    {
        Open().Save(Nas);

        Assert.AreEqual(Nas, Open().All.Single(), "every field, the options included, comes back as saved");
    }

    [TestMethod]
    public void TheFileHoldsTheEndpoint_AndTheSecretGoesToCredentialManager()
    {
        var connections = Open();
        connections.Save(Nas);
        connections.SetSecret(Nas.Id, SecretKind.Password, "hunter2");

        var file = Directory.GetFiles(Path.Combine(_dir, ConnectionsConfig.Name), "config_*.json").Single();
        var text = File.ReadAllText(file);
        StringAssert.Contains(text, "nas");
        Assert.IsFalse(text.Contains("hunter2"), "a secret never reaches the file");
        Assert.AreEqual(("adam", "hunter2"), _secrets.Stored["nexaflow:sftp-nas"]);
    }

    [TestMethod]
    public void EditingAConnectionKeepsItsId_AndItsPlace()
    {
        var connections = Open();
        connections.Save(Nas);
        connections.Save(Share);
        connections.Save(Nas with { Label = "Home NAS" });

        CollectionAssert.AreEqual(new[] { "sftp-nas", "smb-docs" }, connections.All.Select(c => c.Id).ToArray());
        Assert.AreEqual("Home NAS", connections.All[0].Label);
    }

    [TestMethod]
    public void AnIdThatIsNotOnePathSegmentIsRefused()
    {
        var connections = Open();
        foreach (var id in new[] { "", "a/b", @"a\b", "a:b" })
            Assert.ThrowsExactly<ArgumentException>(() => connections.Save(Nas with { Id = id }), $"'{id}'");
        Assert.ThrowsExactly<ArgumentException>(() => connections.Save(Nas with { Host = " " }), "a connection reaches a host");
        Assert.AreEqual(0, connections.All.Count);
    }

    [TestMethod]
    public void RemovingAConnectionForgetsEverySecretOfIt()
    {
        var connections = Open();
        connections.Save(Nas);
        connections.SetSecret(Nas.Id, SecretKind.Password, "hunter2");
        connections.SetSecret(Nas.Id, SecretKind.KeyPassphrase, "open sesame");

        Assert.IsTrue(connections.Remove(Nas.Id));

        Assert.AreEqual(0, _secrets.Stored.Count);
        Assert.AreEqual(0, Open().All.Count, "the removal is saved");
        Assert.IsFalse(connections.Remove(Nas.Id), "there is nothing left to remove");
    }

    [TestMethod]
    public void ASecretIsStoredOnlyForASavedConnection()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Open().SetSecret("sftp-nowhere", SecretKind.Password, "x"));
        Assert.AreEqual(0, _secrets.Stored.Count);
    }

    [TestMethod]
    public void SavingAndRemovingSaySo()
    {
        var connections = Open();
        int changes = 0;
        connections.Changed += () => changes++;

        connections.Save(Nas);
        connections.Remove(Nas.Id);
        connections.Remove(Nas.Id);

        Assert.AreEqual(2, changes, "a removal that removed nothing is no change");
    }

    [TestMethod]
    public void ConnectionsSavedByAnEarlierVersionCarryOver()
    {
        var current = typeof(ConnectionsConfig).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        var prior   = current > new Version(0, 0, 0, 0) ? new Version(0, 0, 0, 0) : new Version(99, 0, 0, 0);
        var folder  = Directory.CreateDirectory(Path.Combine(_dir, ConnectionsConfig.Name)).FullName;
        var earlier = Path.Combine(folder, $"config_{prior}.json");
        File.WriteAllText(earlier,
            """{"Connections":[{"Id":"sftp-nas","Kind":"sftp","Label":"NAS","Host":"nas","Port":2222,"User":"adam"}]}""");

        var carried = Open().All.Single();

        Assert.AreEqual(new ConnectionProfile("sftp-nas", "sftp", "NAS", "nas") { Port = 2222, User = "adam" }, carried);
        Assert.IsFalse(File.Exists(earlier), "the earlier file is rewritten at this version");
        Assert.IsTrue(File.Exists(Path.Combine(folder, $"config_{current}.json")));
    }
}
