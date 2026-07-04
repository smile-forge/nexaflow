using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nexaflow.Core;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Exercises every workspace write-to-disk action (clone, rename, delete, persist) end-to-end against a
/// temp <see cref="ConfigManager"/> base dir, asserting the bytes written read back as expected. Shares
/// the <see cref="WorkspaceManager"/>/<see cref="ConfigManager"/> singletons, so it runs in the
/// non-parallel phase and resets both per test.
/// </summary>
[TestClass]
[DoNotParallelize]
public class WorkspaceManagerTests
{
    private string _origBaseDir = string.Empty;
    private string _baseDir     = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _origBaseDir = ConfigManager.Instance.BaseDir;
        _baseDir     = Path.Combine(Path.GetTempPath(), "nexaflow-ws-" + Guid.NewGuid().ToString("N"));
        ConfigManager.Instance.Initialize(_baseDir);
        WorkspaceManager.Instance.Initialize(new WorkspacesConfig());   // baseline: one default profile
    }

    [TestCleanup]
    public void Teardown()
    {
        ConfigManager.Instance.Initialize(_origBaseDir);
        try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true); }
        catch { /* best effort */ }
    }

    // Initialise the manager with a known set of profiles (each carries its own on-disk folder under temp).
    private static void InitWith(params Profile[] profiles)
        => WorkspaceManager.Instance.Initialize(new WorkspacesConfig { Contexts = [.. profiles] });

    // Reads back the persisted workspaces list straight from disk to verify what SaveProfiles wrote.
    private WorkspacesConfig LoadPersisted()
    {
        var file = Directory.GetFiles(Path.Combine(_baseDir, "workcontexts"), "config_*.json").Single();
        return JsonSerializer.Deserialize<WorkspacesConfig>(File.ReadAllText(file))!;
    }

    // Skips disk-loading of shared services so a profile's hand-set AiConfig/Persona survive into CloneProfile.
    private static void StubSharedServices(Profile p)
        => typeof(Profile).GetProperty(nameof(Profile.RibbonService))!
            .SetValue(p, new RibbonLayoutService(p.Dir));

    // ── UniqueProfileName ──

    [TestMethod]
    public void UniqueProfileName_AppendsSuffixOnCollision()
    {
        InitWith(new Profile { Name = "Dev" });
        Assert.AreEqual("New",   WorkspaceManager.Instance.UniqueProfileName("New"));
        Assert.AreEqual("Dev 2", WorkspaceManager.Instance.UniqueProfileName("Dev"));
    }

    // ── CloneProfile ──

    [TestMethod]
    public void CloneProfile_CopiesConfig_RoundTrips_AndExcludesConversations()
    {
        var source = new Profile { Name = "Source" };
        source.AiConfig.Columns.Add(new ProviderModelPair { Id = "c1", ProviderName = "Prov", Model = "m1" });
        source.AiConfig.Assignments["Conversation"] = "c1";
        source.Persona.Name         = "TestPersona";
        source.Persona.SystemPrompt = "Be brief.";
        StubSharedServices(source);

        // A conversation in the source must NOT follow the clone.
        ConversationStore.Save(source.ConversationsDir,
            new ConversationRecord { Id = "conv", Title = "secret" });

        WorkspaceManager.Instance.CloneProfile(source, "Clone");

        var destDir = WorkspaceManager.ProfileDir("Clone");

        var ai = new AiConfig();
        ConfigManager.Instance.LoadFrom(destDir, ai, ai.ConfigName);
        Assert.AreEqual(1,    ai.Columns.Count);
        Assert.AreEqual("c1", ai.Columns[0].Id);
        Assert.AreEqual("c1", ai.Assignments["Conversation"]);

        var persona = new AiPersonaConfig();
        ConfigManager.Instance.LoadFrom(destDir, persona, persona.ConfigName);
        Assert.AreEqual("TestPersona", persona.Name);
        Assert.AreEqual("Be brief.",   persona.SystemPrompt);

        Assert.IsFalse(Directory.Exists(Path.Combine(destDir, "Conversations")),
            "Clone must not copy the source's conversations.");
    }

    // ── RenameProfile ──

    [TestMethod]
    public void RenameProfile_MovesDataFolder_AndPersistsNewName()
    {
        var p = new Profile { Name = "Original" };
        InitWith(p);
        Directory.CreateDirectory(p.Dir);
        File.WriteAllText(Path.Combine(p.Dir, "marker.txt"), "payload");
        var oldDir = p.Dir;

        WorkspaceManager.Instance.RenameProfile(p, "Renamed");

        Assert.AreEqual("Renamed", p.Name);
        Assert.IsFalse(Directory.Exists(oldDir), "Old folder should be gone after the move.");
        Assert.IsTrue(File.Exists(Path.Combine(p.Dir, "marker.txt")), "Data should follow the rename.");
        Assert.AreEqual("payload", File.ReadAllText(Path.Combine(p.Dir, "marker.txt")));

        CollectionAssert.AreEqual(new[] { "Renamed" }, LoadPersisted().Contexts.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void RenameProfile_SameName_StillPersists()
    {
        var p = new Profile { Name = "Keep", Color = "#222222" };
        InitWith(p);

        WorkspaceManager.Instance.RenameProfile(p, "Keep");   // no folder move, but must save

        var saved = LoadPersisted();
        Assert.AreEqual("Keep",    saved.Contexts.Single().Name);
        Assert.AreEqual("#222222", saved.Contexts.Single().Color);
    }

    // ── DeleteProfile ──

    [TestMethod]
    public void DeleteProfile_RemovesFolderConversations_AndList_AndPersists()
    {
        var keep   = new Profile { Name = "Keep" };
        var doomed = new Profile { Name = "Doomed" };
        InitWith(keep, doomed);

        ConversationStore.Save(doomed.ConversationsDir,
            new ConversationRecord { Id = "conv1", Title = "t" });
        Assert.IsTrue(Directory.Exists(doomed.Dir));

        Assert.IsTrue(WorkspaceManager.Instance.DeleteProfile(doomed));

        Assert.IsFalse(Directory.Exists(doomed.Dir), "Folder + conversations must be deleted.");
        Assert.IsFalse(WorkspaceManager.Instance.Profiles.Contains(doomed));
        Assert.IsTrue(WorkspaceManager.Instance.Profiles.Contains(keep));
        CollectionAssert.AreEqual(new[] { "Keep" }, LoadPersisted().Contexts.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void DeleteProfile_LastRemaining_IsRefused()
    {
        var only = new Profile { Name = "Only" };
        InitWith(only);
        Directory.CreateDirectory(only.Dir);

        Assert.IsFalse(WorkspaceManager.Instance.DeleteProfile(only));
        Assert.IsTrue(Directory.Exists(only.Dir));
        Assert.IsTrue(WorkspaceManager.Instance.Profiles.Contains(only));
    }

    [TestMethod]
    public void DeleteProfile_Force_StillRefusesLastRemaining()
    {
        // force bypasses the in-use guard, but never the last-remaining guard.
        var only = new Profile { Name = "Only" };
        InitWith(only);
        Directory.CreateDirectory(only.Dir);

        Assert.IsFalse(WorkspaceManager.Instance.DeleteProfile(only, force: true));
        Assert.IsTrue(Directory.Exists(only.Dir));
    }

    // ── RemoveProfile (Options batch path) ──

    [TestMethod]
    public void RemoveProfile_DeletesDataFolder_AndList()
    {
        var a = new Profile { Name = "A" };
        var b = new Profile { Name = "B" };
        InitWith(a, b);
        Directory.CreateDirectory(b.Dir);
        File.WriteAllText(Path.Combine(b.Dir, "x.txt"), "1");

        Assert.IsTrue(WorkspaceManager.Instance.RemoveProfile(b));
        Assert.IsFalse(Directory.Exists(b.Dir));
        Assert.IsFalse(WorkspaceManager.Instance.Profiles.Contains(b));
    }

    // ── SaveProfiles ──

    [TestMethod]
    public void SaveProfiles_PersistsNameColourIcon()
    {
        InitWith(new Profile { Name = "One", Color = "#111111", Icon = "A" });
        WorkspaceManager.Instance.AddProfile("Two");

        WorkspaceManager.Instance.SaveProfiles();

        var saved = LoadPersisted();
        CollectionAssert.AreEqual(new[] { "One", "Two" }, saved.Contexts.Select(c => c.Name).ToArray());
        var one = saved.Contexts.First(c => c.Name == "One");
        Assert.AreEqual("#111111", one.Color);
        Assert.AreEqual("A",       one.Icon);
    }

    // ── QuarantineOrphanedDataFolders ──

    private string QuarantineDir => Path.Combine(_baseDir, "Contexts", WorkspaceManager.OrphanQuarantineDir);

    [TestMethod]
    public void QuarantineOrphanedDataFolders_MovesUnreferenced_KeepsLive_WhenAuthoritative()
    {
        var keep = new Profile { Name = "Keep" };
        InitWith(keep);
        Directory.CreateDirectory(keep.Dir);                        // referenced → survives

        var orphan = WorkspaceManager.ProfileDir("GhostFromReset"); // no matching profile → quarantined
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "conv.json"), "{}");

        var moved = WorkspaceManager.Instance.QuarantineOrphanedDataFolders(listIsAuthoritative: true);

        Assert.IsTrue(Directory.Exists(keep.Dir), "A live profile's folder must never be moved.");
        Assert.IsFalse(Directory.Exists(orphan),  "An unreferenced folder must leave its original location.");
        CollectionAssert.AreEqual(new[] { "GhostFromReset" }, moved.ToArray());

        // Data is preserved, not deleted — it (and its contents) sit in the quarantine bin.
        var landed = Path.Combine(QuarantineDir, "GhostFromReset");
        Assert.IsTrue(Directory.Exists(landed));
        Assert.AreEqual("{}", File.ReadAllText(Path.Combine(landed, "conv.json")));
    }

    [TestMethod]
    public void QuarantineOrphanedDataFolders_PreservesInPlace_WhenListNotAuthoritative()
    {
        var keep = new Profile { Name = "Keep" };
        InitWith(keep);

        var orphan = WorkspaceManager.ProfileDir("GhostFromReset");
        Directory.CreateDirectory(orphan);

        var moved = WorkspaceManager.Instance.QuarantineOrphanedDataFolders(listIsAuthoritative: false);

        Assert.AreEqual(0, moved.Count);
        Assert.IsTrue(Directory.Exists(orphan),
            "A defaulted/untrusted list must never move folders — every folder looks orphaned then.");
        Assert.IsFalse(Directory.Exists(QuarantineDir));
    }

    [TestMethod]
    public void QuarantineOrphanedDataFolders_NoOp_WhenAllFoldersReferenced()
    {
        var a = new Profile { Name = "A" };
        var b = new Profile { Name = "B" };
        InitWith(a, b);
        Directory.CreateDirectory(a.Dir);
        Directory.CreateDirectory(b.Dir);

        var moved = WorkspaceManager.Instance.QuarantineOrphanedDataFolders(listIsAuthoritative: true);

        Assert.AreEqual(0, moved.Count);
        Assert.IsTrue(Directory.Exists(a.Dir));
        Assert.IsTrue(Directory.Exists(b.Dir));
        Assert.IsFalse(Directory.Exists(QuarantineDir));
    }

    [TestMethod]
    public void QuarantineOrphanedDataFolders_SkipsBin_AndDeDupesNameCollision()
    {
        var keep = new Profile { Name = "Keep" };
        InitWith(keep);

        // A prior quarantine already holds a "Ghost"; a fresh orphan of the same name must not collide.
        Directory.CreateDirectory(Path.Combine(QuarantineDir, "Ghost"));
        var orphan = WorkspaceManager.ProfileDir("Ghost");
        Directory.CreateDirectory(orphan);

        var moved = WorkspaceManager.Instance.QuarantineOrphanedDataFolders(listIsAuthoritative: true);

        CollectionAssert.AreEqual(new[] { "Ghost" }, moved.ToArray());
        Assert.IsFalse(Directory.Exists(orphan));
        Assert.IsTrue(Directory.Exists(Path.Combine(QuarantineDir, "Ghost")),     "Pre-existing bin entry untouched.");
        Assert.IsTrue(Directory.Exists(Path.Combine(QuarantineDir, "Ghost (2)")), "Collision gets a suffixed slot.");
    }
}
