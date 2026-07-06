using System;
using System.IO;
using System.Linq;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>A compound-extension criterion like <c>*.tar.gz</c> must match <c>file.tar.gz</c> — the file
/// map used to look only at the last extension (<c>.gz</c>), so "As Archive" never appeared for it.</summary>
[TestClass]
[DoNotParallelize]   // mutates the process-wide FileMapManager.Instance
public class CompoundExtensionMatchTests
{
    private const string Exp = "/test-compound-archive";

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexa-filemap-" + Guid.NewGuid().ToString("N"));
        FileMapManager.Instance.Initialize(baseDir: dir);
        FileMapManager.Instance.SaveMapping(new ExperienceMapping
        {
            ExperienceId = Exp,
            Source = MappingSource.User,
            Criteria = [new FileSelectionCriteria { Type = CriteriaType.Extension, Value = "*.tar.gz" }],
        });
    }

    [TestMethod]
    public void CompoundExtension_Matches()
    {
        var ids = FileMapManager.Instance.GetExperiencesForFile(new FileInfo(@"C:\x\backup.tar.gz"));
        Assert.IsTrue(ids.Contains(Exp), "*.tar.gz should match backup.tar.gz");
        Assert.AreEqual(4, FileMapManager.Instance.GetMatchSpecificity(new FileInfo(@"C:\x\backup.tar.gz"), Exp));
    }

    [TestMethod]
    public void BareExtension_DoesNotMatchCompound()
    {
        var ids = FileMapManager.Instance.GetExperiencesForFile(new FileInfo(@"C:\x\plain.gz"));
        Assert.IsFalse(ids.Contains(Exp), "*.tar.gz must not match a plain .gz");
    }
}
