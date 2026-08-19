using System;
using System.IO;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>The retired HKCR scan left synthetic <c>registry_*.json</c> mappings on disk (some promoted to
/// User-source by the old disable path). Initialize must delete them so they stop polluting the tree,
/// without touching real mappings.</summary>
[TestClass]
[DoNotParallelize]   // mutates the process-wide FileMapManager.Instance
[CoversNode("winfs-filemap-editor")]
public class FileMapLegacyRegistryCleanupTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexa-filemap-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "filemap"));
    }

    [TestCleanup]
    public void Cleanup()
    { try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { } }

    [TestMethod]
    public void Initialize_DeletesLeftoverRegistryMappings_KeepsOthers()
    {
        var filemap = Path.Combine(_dir, "filemap");
        var legacy  = Path.Combine(filemap, "registry_.docx.json");
        var keep    = Path.Combine(filemap, "image.json");
        File.WriteAllText(legacy, "{}");
        File.WriteAllText(keep, "{ \"ExperienceId\": \"/image\", \"Criteria\": [] }");

        FileMapManager.Instance.Initialize(_dir);

        Assert.IsFalse(File.Exists(legacy), "leftover registry_ mapping should be deleted");
        Assert.IsTrue(File.Exists(keep), "a real mapping should survive");
    }
}
