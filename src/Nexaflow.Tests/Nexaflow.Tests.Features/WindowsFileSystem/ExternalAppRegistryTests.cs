using System.Collections.Generic;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Features.WindowsFileSystem.ViewModels;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
[DoNotParallelize]   // mutates the process-wide ExternalAppRegistry.Instance
public class ExternalAppRegistryTests
{
    private static FileSystemEntry File(string path) =>
        new() { Name = System.IO.Path.GetFileName(path), FullPath = path, IsDirectory = false, IsDrive = false };

    private static int ResolveCount(ExternalAppDefinition def, params string[] paths)
    {
        ExternalAppRegistry.Instance.Update(new ExternalAppsConfig { Apps = [def] });
        var selected = new List<FileSystemEntry>();
        foreach (var p in paths) selected.Add(File(p));
        return ExternalAppRegistry.Instance.Resolve(selected).Count;
    }

    [TestCleanup]
    public void Cleanup() => ExternalAppRegistry.Instance.Update(new ExternalAppsConfig());

    [TestMethod]
    public void BackfillIds_AssignsMissing_PreservesExisting_Idempotent()
    {
        var withId = new ExternalAppDefinition { Id = "keep-me", DisplayName = "A" };
        var noId   = new ExternalAppDefinition { DisplayName = "B" };
        ExternalAppRegistry.Instance.Update(new ExternalAppsConfig { Apps = [withId, noId] });

        Assert.IsTrue(ExternalAppRegistry.Instance.BackfillIds(), "an app was missing an id");
        Assert.AreEqual("keep-me", withId.Id);
        Assert.IsFalse(string.IsNullOrEmpty(noId.Id), "missing id should have been assigned");

        Assert.IsFalse(ExternalAppRegistry.Instance.BackfillIds(), "second call has nothing to assign");
    }

    [TestMethod]
    public void FindById_ReturnsMatch_ElseNull()
    {
        var a = new ExternalAppDefinition { Id = "app-1", DisplayName = "One" };
        ExternalAppRegistry.Instance.Update(new ExternalAppsConfig { Apps = [a] });

        Assert.AreSame(a, ExternalAppRegistry.Instance.FindById("app-1"));
        Assert.IsNull(ExternalAppRegistry.Instance.FindById("missing"));
        Assert.IsNull(ExternalAppRegistry.Instance.FindById(""));
    }

    [TestMethod]
    public void LegacyExtension_MatchesThatExtensionOnly()
    {
        var def = new ExternalAppDefinition { DisplayName = "Editor", ApplicationPath = "x.exe", Extension = ".md" };
        Assert.AreEqual(1, ResolveCount(def, @"C:\a\note.md"));
        Assert.AreEqual(0, ResolveCount(def, @"C:\a\note.txt"));
    }

    [TestMethod]
    public void PathPatternCriteria_ScopesToFolderTree()
    {
        var def = new ExternalAppDefinition
        {
            DisplayName = "Editor", ApplicationPath = "x.exe", Extension = ".md",   // legacy ignored when criteria present
            Criteria = [new() { Type = CriteriaType.PathPattern, Value = @"C:\proj\**\*.md" }],
        };
        Assert.AreEqual(1, ResolveCount(def, @"C:\proj\sub\note.md"));
        Assert.AreEqual(0, ResolveCount(def, @"C:\other\note.md"));   // out of scope
        Assert.AreEqual(0, ResolveCount(def, @"C:\proj\sub\note.txt")); // wrong extension
    }

    [TestMethod]
    public void ExtensionCriteria_MatchesAnywhere()
    {
        var def = new ExternalAppDefinition
        {
            DisplayName = "Tailer", ApplicationPath = "x.exe",
            Criteria = [new() { Type = CriteriaType.Extension, Value = "*.log" }],
        };
        Assert.AreEqual(1, ResolveCount(def, @"C:\anywhere\server.log"));
        Assert.AreEqual(0, ResolveCount(def, @"C:\anywhere\server.txt"));
    }

    [TestMethod]
    public void MultipleSelection_RequiresEveryFileToMatch()
    {
        var def = new ExternalAppDefinition
        {
            DisplayName = "Editor", ApplicationPath = "x.exe", MultiFile = MultiFileMode.SingleLaunchAllFiles,
            Criteria = [new() { Type = CriteriaType.Extension, Value = "*.md" }],
        };
        Assert.AreEqual(1, ResolveCount(def, @"C:\a.md", @"C:\b.md"));
        Assert.AreEqual(0, ResolveCount(def, @"C:\a.md", @"C:\b.txt"));
    }
}
