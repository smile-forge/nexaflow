using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
[DoNotParallelize]   // mutates the process-wide FileMapManager.Instance
[CoversNode("winfs-filemap-editor")]
public class FileMapDefaultsMergeTests
{
    private string _dir = string.Empty;
    private string _filemapDir = string.Empty;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        Converters                  = { new JsonStringEnumConverter() },
    };

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexaflow-filemap-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        // No bundled default-filemap.json in the test output, so this is an empty, isolated seed.
        FileMapManager.Instance.Initialize(baseDir: _dir);
        _filemapDir = Path.Combine(_dir, "filemap");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void NewDefault_IsSeeded()
    {
        FileMapManager.Instance.ApplyBundledDefaults([Mapping("/alpha", "*.a")], "h1");
        Assert.AreEqual("*.a", ReadExt("/alpha"));
    }

    [TestMethod]
    public void UnmodifiedDefault_IsRefreshed_WhenBundleChanges()
    {
        FileMapManager.Instance.ApplyBundledDefaults([Mapping("/alpha", "*.a")], "h1");
        // User hasn't touched it → a changed bundle updates it.
        bool changed = FileMapManager.Instance.ApplyBundledDefaults([Mapping("/alpha", "*.b")], "h2");
        Assert.IsTrue(changed);
        Assert.AreEqual("*.b", ReadExt("/alpha"));
    }

    [TestMethod]
    public void UserCustomizedDefault_IsPreserved_WhenBundleChanges()
    {
        FileMapManager.Instance.ApplyBundledDefaults([Mapping("/alpha", "*.a")], "h1");
        WriteRaw(Mapping("/alpha", "*.user"));   // simulate a user edit
        FileMapManager.Instance.ApplyBundledDefaults([Mapping("/alpha", "*.b")], "h2");
        Assert.AreEqual("*.user", ReadExt("/alpha"), "a user-customized mapping must not be clobbered");
    }

    [TestMethod]
    public void SameBundleHash_IsFastNoOp()
    {
        FileMapManager.Instance.ApplyBundledDefaults([Mapping("/alpha", "*.a")], "h1");
        // Same hash, even with different content, is treated as "unchanged" and skipped.
        bool changed = FileMapManager.Instance.ApplyBundledDefaults([Mapping("/alpha", "*.b")], "h1");
        Assert.IsFalse(changed);
        Assert.AreEqual("*.a", ReadExt("/alpha"));
    }

    [TestMethod]
    public void AddedDefault_IsSeeded_ExistingUnmodifiedPreserved()
    {
        FileMapManager.Instance.ApplyBundledDefaults([Mapping("/alpha", "*.a")], "h1");
        FileMapManager.Instance.ApplyBundledDefaults(
            [Mapping("/alpha", "*.a"), Mapping("/beta", "*.x")], "h2");
        Assert.AreEqual("*.a", ReadExt("/alpha"));
        Assert.AreEqual("*.x", ReadExt("/beta"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ExperienceMapping Mapping(string id, string ext) => new()
    {
        ExperienceId = id,
        Source       = MappingSource.User,
        Criteria     = [new FileSelectionCriteria { Type = CriteriaType.Extension, Value = ext }],
    };

    private string FilePath(string id)
    {
        string safe = id.TrimStart('/').Replace('/', '_');
        if (string.IsNullOrEmpty(safe)) safe = "root";
        return Path.Combine(_filemapDir, safe + ".json");
    }

    private void WriteRaw(ExperienceMapping m) =>
        File.WriteAllText(FilePath(m.ExperienceId), JsonSerializer.Serialize(m, _opts));

    private string? ReadExt(string id)
    {
        var m = JsonSerializer.Deserialize<ExperienceMapping>(File.ReadAllText(FilePath(id)), _opts);
        return m!.Criteria[0].Value;
    }
}
