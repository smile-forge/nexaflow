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
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Exercises every workspace write-to-disk action (clone, rename, delete, persist) end-to-end against a
/// temp <see cref="ConfigManager"/> base dir, asserting the bytes written read back as expected. Shares
/// the <see cref="WorkspaceManager"/>/<see cref="ConfigManager"/> singletons, so it runs in the
/// non-parallel phase and resets both per test.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("workspaces")]
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
        WorkspaceManager.Instance.Initialize(new WorkspacesConfig());   // baseline: one default workspace
    }

    [TestCleanup]
    public void Teardown()
    {
        ConfigManager.Instance.Initialize(_origBaseDir);
        try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true); }
        catch { /* best effort */ }
    }

    // Initialise the manager with a known set of workspaces (each carries its own on-disk folder under temp).
    private static void InitWith(params Workspace[] workspaces)
        => WorkspaceManager.Instance.Initialize(new WorkspacesConfig { Contexts = [.. workspaces] });

    // Reads back the persisted workspaces list straight from disk to verify what SaveWorkspaces wrote.
    private WorkspacesConfig LoadPersisted()
    {
        var file = Directory.GetFiles(Path.Combine(_baseDir, "workcontexts"), "config_*.json").Single();
        return JsonSerializer.Deserialize<WorkspacesConfig>(File.ReadAllText(file))!;
    }

    // Skips disk-loading of shared services so a workspace's hand-set AiConfig/Persona survive into CloneWorkspace.
    private static void StubSharedServices(Workspace p)
        => typeof(Workspace).GetProperty(nameof(Workspace.RibbonService))!
            .SetValue(p, new RibbonLayoutService(p.Dir));

    // ── UniqueWorkspaceName ──

    [TestMethod]
    public void UniqueWorkspaceName_AppendsSuffixOnCollision()
    {
        InitWith(new Workspace { Name = "Dev" });
        Assert.AreEqual("New",   WorkspaceManager.Instance.UniqueWorkspaceName("New"));
        Assert.AreEqual("Dev 2", WorkspaceManager.Instance.UniqueWorkspaceName("Dev"));
    }

    // ── CloneWorkspace ──

    [TestMethod]
    [CoversNode("ws-clone")]
    public void CloneWorkspace_CopiesConfig_RoundTrips_AndExcludesConversations()
    {
        var source = new Workspace { Name = "Source" };
        source.AiConfig.Columns.Add(new ProviderModelPair { Id = "c1", ProviderName = "Prov", Model = "m1" });
        source.AiConfig.Assignments["Conversation"] = "c1";
        source.Persona.Name         = "TestPersona";
        source.Persona.SystemPrompt = "Be brief.";
        StubSharedServices(source);

        // A conversation in the source must NOT follow the clone.
        ConversationStore.Save(source.ConversationsDir,
            new ConversationRecord { Id = "conv", Title = "secret" });

        WorkspaceManager.Instance.CloneWorkspace(source, "Clone");

        var destDir = WorkspaceManager.WorkspaceDir("Clone");

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

    // ── RenameWorkspace ──

    [TestMethod]
    public void RenameWorkspace_MovesDataFolder_AndPersistsNewName()
    {
        var p = new Workspace { Name = "Original" };
        InitWith(p);
        Directory.CreateDirectory(p.Dir);
        File.WriteAllText(Path.Combine(p.Dir, "marker.txt"), "payload");
        var oldDir = p.Dir;

        WorkspaceManager.Instance.RenameWorkspace(p, "Renamed");

        Assert.AreEqual("Renamed", p.Name);
        Assert.IsFalse(Directory.Exists(oldDir), "Old folder should be gone after the move.");
        Assert.IsTrue(File.Exists(Path.Combine(p.Dir, "marker.txt")), "Data should follow the rename.");
        Assert.AreEqual("payload", File.ReadAllText(Path.Combine(p.Dir, "marker.txt")));

        CollectionAssert.AreEqual(new[] { "Renamed" }, LoadPersisted().Contexts.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void RenameWorkspace_SameName_StillPersists()
    {
        var p = new Workspace { Name = "Keep", Color = "#222222" };
        InitWith(p);

        WorkspaceManager.Instance.RenameWorkspace(p, "Keep");   // no folder move, but must save

        var saved = LoadPersisted();
        Assert.AreEqual("Keep",    saved.Contexts.Single().Name);
        Assert.AreEqual("#222222", saved.Contexts.Single().Color);
    }

    // ── DeleteWorkspace ──

    [TestMethod]
    [CoversNode("ws-delete")]
    public void DeleteWorkspace_RemovesFolderConversations_AndList_AndPersists()
    {
        var keep   = new Workspace { Name = "Keep" };
        var doomed = new Workspace { Name = "Doomed" };
        InitWith(keep, doomed);

        ConversationStore.Save(doomed.ConversationsDir,
            new ConversationRecord { Id = "conv1", Title = "t" });
        Assert.IsTrue(Directory.Exists(doomed.Dir));

        Assert.IsTrue(WorkspaceManager.Instance.DeleteWorkspace(doomed));

        Assert.IsFalse(Directory.Exists(doomed.Dir), "Folder + conversations must be deleted.");
        Assert.IsFalse(WorkspaceManager.Instance.Workspaces.Contains(doomed));
        Assert.IsTrue(WorkspaceManager.Instance.Workspaces.Contains(keep));
        CollectionAssert.AreEqual(new[] { "Keep" }, LoadPersisted().Contexts.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    [CoversNode("ws-delete")]
    public void DeleteWorkspace_LastRemaining_IsRefused()
    {
        var only = new Workspace { Name = "Only" };
        InitWith(only);
        Directory.CreateDirectory(only.Dir);

        Assert.IsFalse(WorkspaceManager.Instance.DeleteWorkspace(only));
        Assert.IsTrue(Directory.Exists(only.Dir));
        Assert.IsTrue(WorkspaceManager.Instance.Workspaces.Contains(only));
    }

    [TestMethod]
    [CoversNode("ws-delete")]
    public void DeleteWorkspace_Force_StillRefusesLastRemaining()
    {
        // force bypasses the in-use guard, but never the last-remaining guard.
        var only = new Workspace { Name = "Only" };
        InitWith(only);
        Directory.CreateDirectory(only.Dir);

        Assert.IsFalse(WorkspaceManager.Instance.DeleteWorkspace(only, force: true));
        Assert.IsTrue(Directory.Exists(only.Dir));
    }

    // ── RemoveWorkspace (Options batch path) ──

    [TestMethod]
    public void RemoveWorkspace_DeletesDataFolder_AndList()
    {
        var a = new Workspace { Name = "A" };
        var b = new Workspace { Name = "B" };
        InitWith(a, b);
        Directory.CreateDirectory(b.Dir);
        File.WriteAllText(Path.Combine(b.Dir, "x.txt"), "1");

        Assert.IsTrue(WorkspaceManager.Instance.RemoveWorkspace(b));
        Assert.IsFalse(Directory.Exists(b.Dir));
        Assert.IsFalse(WorkspaceManager.Instance.Workspaces.Contains(b));
    }

    // ── SaveWorkspaces ──

    [TestMethod]
    public void SaveWorkspaces_PersistsNameColourIcon()
    {
        InitWith(new Workspace { Name = "One", Color = "#111111", Icon = "A" });
        WorkspaceManager.Instance.AddWorkspace("Two");

        WorkspaceManager.Instance.SaveWorkspaces();

        var saved = LoadPersisted();
        CollectionAssert.AreEqual(new[] { "One", "Two" }, saved.Contexts.Select(c => c.Name).ToArray());
        var one = saved.Contexts.First(c => c.Name == "One");
        Assert.AreEqual("#111111", one.Color);
        Assert.AreEqual("A",       one.Icon);
    }

    [TestMethod]
    public void SaveWorkspaces_PersistsEveryStoredField_AndNothingRuntimeOnly()
    {
        var workspace = new Workspace
        {
            Name                   = "Dev",
            Color                  = "#ABCDEF",
            Icon                   = "\u25C6",
            SuppressSessionRestore = true,
            DefaultTabs            = [new DefaultTabDescriptor { PageKind = "Terminal", Pane = 1, IsActive = true }],
            LastSessionTabs        = [new DefaultTabDescriptor { PageKind = "Markdown", Title = "notes.md" }],
            IsInUse                = true,   // transient UI flag — must not reach the file
        };
        InitWith(workspace);

        WorkspaceManager.Instance.SaveWorkspaces();

        var saved = LoadPersisted().Contexts.Single();
        Assert.AreEqual("Dev",      saved.Name);
        Assert.AreEqual("#ABCDEF",  saved.Color);
        Assert.AreEqual("\u25C6",   saved.Icon);
        Assert.IsTrue(saved.SuppressSessionRestore);
        Assert.AreEqual("Terminal", saved.DefaultTabs.Single().PageKind);
        Assert.AreEqual(1,          saved.DefaultTabs.Single().Pane);
        Assert.IsTrue(saved.DefaultTabs.Single().IsActive);
        Assert.AreEqual("Markdown", saved.LastSessionTabs?.Single().PageKind);
        Assert.AreEqual("notes.md", saved.LastSessionTabs?.Single().Title);
        Assert.IsFalse(saved.IsInUse, "IsInUse marks a live row in the editor and is never persisted");
    }

    // ── A workspace list the app cannot run on ──

    [TestMethod]
    public void AWorkspaceListThatIsNotUsable_IsMadeUsable_RatherThanTakingTheAppDown()
    {
        // Everything a hand-edited or half-written workcontexts.json can hold that would otherwise throw:
        // the name is the data folder (Path.Combine on null), and startup indexes the first workspace.
        var config = new WorkspacesConfig
        {
            Contexts =
            [
                null!,
                new Workspace { Name = null!, Color = null!, Icon = "  " },
                new Workspace { Name = "  Dev  " },
                new Workspace { Name = "DEV" },                              // the same folder, case-insensitively
                new Workspace { Name = "Re:ports/Q1" },                      // not a folder name
                new Workspace { Name = "Tabs", DefaultTabs = [null!], LastSessionTabs = [null!] },
            ],
        };

        var names = config.Contexts.Select(c => c.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "Default", "Dev", "DEV 2", "ReportsQ1", "Tabs" }, names);
        Assert.AreEqual(new Workspace().Color, config.Contexts[0].Color, "a blank colour would not bind to a brush");
        Assert.AreEqual(new Workspace().Icon,  config.Contexts[0].Icon);

        var tabs = config.Contexts.Single(c => c.Name == "Tabs");
        Assert.IsFalse(names.Contains("Re:ports/Q1"), "the characters a folder name cannot hold are dropped");
        Assert.AreEqual(0, tabs.DefaultTabs.Count, "an entry that is not there cannot open a tab");
        Assert.IsNull(tabs.LastSessionTabs, "and a session holding nothing is no offer at all");

        // The folder for every surviving name resolves, which is what startup does before anything else.
        foreach (var workspace in config.Contexts)
            Assert.IsFalse(string.IsNullOrWhiteSpace(WorkspaceManager.WorkspaceDir(workspace.Name)));

        WorkspaceManager.Instance.Initialize(config);
        Assert.AreEqual(5, WorkspaceManager.Instance.Workspaces.Count);
    }

    [TestMethod]
    public void AnEmptyWorkspaceList_StillYieldsOne_BecauseStartupOpensTheFirst()
    {
        var config = new WorkspacesConfig { Contexts = [] };

        Assert.AreEqual(1, config.Contexts.Count);
        Assert.AreEqual("Default", config.Contexts[0].Name);
    }

    [TestMethod]
    public void AListThatCouldNotBeRead_IsRebuiltFromTheDataFolders_WithTheirContentsIntact()
    {
        // What is left after workcontexts.json is lost: the folders, each holding a workspace's notes,
        // conversations and settings, and a list that names none of them.
        var root = Path.Combine(_baseDir, "Contexts");
        Directory.CreateDirectory(Path.Combine(root, "Dev", "Conversations"));
        File.WriteAllText(Path.Combine(root, "Dev", "Conversations", "chat.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "Research"));
        Directory.CreateDirectory(Path.Combine(root, WorkspaceManager.OrphanQuarantineDir, "Old"));

        var config = new WorkspacesConfig { Contexts = [new Workspace { Name = "Dev" }] };

        var recovered = WorkspaceManager.RecoverWorkspacesFromDataFolders(config);

        CollectionAssert.AreEqual(new[] { "Research" }, recovered.ToArray(),
            "only a folder no entry names; the quarantine bin was put aside deliberately");
        CollectionAssert.AreEquivalent(new[] { "Dev", "Research" },
                                       config.Contexts.Select(c => c.Name).ToArray());
        Assert.IsTrue(File.Exists(Path.Combine(root, "Dev", "Conversations", "chat.json")),
            "the folders are read for their names and never touched");

        // And the recovered list is what a save then writes, so the recovery survives the session.
        WorkspaceManager.Instance.Initialize(config);
        WorkspaceManager.Instance.SaveWorkspaces();
        CollectionAssert.AreEquivalent(new[] { "Dev", "Research" },
                                       LoadPersisted().Contexts.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void RecoveringAListThatNamesEveryFolder_ChangesNothing()
    {
        var root = Path.Combine(_baseDir, "Contexts");
        Directory.CreateDirectory(Path.Combine(root, "Dev"));
        var config = new WorkspacesConfig { Contexts = [new Workspace { Name = "Dev", Color = "#111111" }] };

        Assert.AreEqual(0, WorkspaceManager.RecoverWorkspacesFromDataFolders(config).Count);
        Assert.AreEqual("#111111", config.Contexts.Single().Color, "a workspace already in the list is left alone");
    }

    // ── QuarantineOrphanedDataFolders ──

    private string QuarantineDir => Path.Combine(_baseDir, "Contexts", WorkspaceManager.OrphanQuarantineDir);

    [TestMethod]
    public void QuarantineOrphanedDataFolders_MovesUnreferenced_KeepsLive_WhenAuthoritative()
    {
        var keep = new Workspace { Name = "Keep" };
        InitWith(keep);
        Directory.CreateDirectory(keep.Dir);                        // referenced → survives

        var orphan = WorkspaceManager.WorkspaceDir("GhostFromReset"); // no matching workspace → quarantined
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "conv.json"), "{}");

        var moved = WorkspaceManager.Instance.QuarantineOrphanedDataFolders(listIsAuthoritative: true);

        Assert.IsTrue(Directory.Exists(keep.Dir), "A live workspace's folder must never be moved.");
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
        var keep = new Workspace { Name = "Keep" };
        InitWith(keep);

        var orphan = WorkspaceManager.WorkspaceDir("GhostFromReset");
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
        var a = new Workspace { Name = "A" };
        var b = new Workspace { Name = "B" };
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
        var keep = new Workspace { Name = "Keep" };
        InitWith(keep);

        // A prior quarantine already holds a "Ghost"; a fresh orphan of the same name must not collide.
        Directory.CreateDirectory(Path.Combine(QuarantineDir, "Ghost"));
        var orphan = WorkspaceManager.WorkspaceDir("Ghost");
        Directory.CreateDirectory(orphan);

        var moved = WorkspaceManager.Instance.QuarantineOrphanedDataFolders(listIsAuthoritative: true);

        CollectionAssert.AreEqual(new[] { "Ghost" }, moved.ToArray());
        Assert.IsFalse(Directory.Exists(orphan));
        Assert.IsTrue(Directory.Exists(Path.Combine(QuarantineDir, "Ghost")),     "Pre-existing bin entry untouched.");
        Assert.IsTrue(Directory.Exists(Path.Combine(QuarantineDir, "Ghost (2)")), "Collision gets a suffixed slot.");
    }
}
