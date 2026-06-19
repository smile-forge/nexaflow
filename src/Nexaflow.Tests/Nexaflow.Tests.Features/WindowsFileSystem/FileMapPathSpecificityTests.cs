using System;
using System.IO;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
[DoNotParallelize]   // mutates the process-wide FileMapManager.Instance
public class FileMapPathSpecificityTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexaflow-filemap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        FileMapManager.Instance.Initialize(useRegistryMapping: false, baseDir: _dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void PathPatternScope_ScoresTopSpecificity()
    {
        const string expId = "/test-scope-exp";
        string folder  = Path.Combine(_dir, "watched");
        string pattern = Path.Combine(folder, "**", "*.zzt");

        FileMapManager.Instance.SaveMapping(new ExperienceMapping
        {
            ExperienceId = expId,
            Source       = MappingSource.User,
            Criteria     = [new() { Type = CriteriaType.PathPattern, Value = pattern }],
        });

        var inScope    = new FileInfo(Path.Combine(folder, "sub", "a.zzt"));
        var outOfScope = new FileInfo(@"C:\nowhere\a.zzt");

        // PathPattern is the top of the ladder (5) — it beats an extension match (4),
        // so DefaultFileOpener (internal = spec*2+1) wins over the shell verb (4*2).
        Assert.AreEqual(5, FileMapManager.Instance.GetMatchSpecificity(inScope, expId));
        Assert.AreEqual(-1, FileMapManager.Instance.GetMatchSpecificity(outOfScope, expId));
    }
}
