using System;
using System.IO;
using System.Linq;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The File Type Actions "Reset to Default" button: <see cref="FileMapManager.ResetToDefault"/> restores a
/// customized experience to its bundled default criteria, and <see cref="FileMapManager.GetBundledDefault"/>
/// reports whether an experience has a default at all (drives the button's visibility).
/// </summary>
[TestClass]
[DoNotParallelize]   // mutates the process-wide FileMapManager.Instance
[CoversNode("winfs-filemap-editor")]
public class FileMapResetTests
{
    [ClassInitialize]
    public static void Init(TestContext _)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexa-filemap-" + Guid.NewGuid().ToString("N"));
        FileMapManager.Instance.Initialize(baseDir: dir);
    }

    private static FileInfo Docx => new(@"C:\x\report.docx");

    [TestMethod]
    public void GetBundledDefault_KnownVsUserCreated()
    {
        Assert.IsNotNull(FileMapManager.Instance.GetBundledDefault("/archive"),
            "/archive is a bundled experience");
        Assert.IsNull(FileMapManager.Instance.GetBundledDefault("/no-such-user-experience"),
            "a user-created experience has no bundled default");
    }

    [TestMethod]
    public void ResetToDefault_RestoresBundledArchiveCriteria()
    {
        const string id = "/archive";

        // Customize /archive away from its default so docx no longer resolves to it at all.
        FileMapManager.Instance.SaveMapping(new ExperienceMapping
        {
            ExperienceId = id,
            Source = MappingSource.User,
            Criteria = [new FileSelectionCriteria { Type = CriteriaType.Extension, Value = "*.myzip" }],
        });
        Assert.IsFalse(FileMapManager.Instance.GetExperiencesForFile(Docx).Contains(id),
            "customized /archive no longer claims docx");

        var reset = FileMapManager.Instance.ResetToDefault(id);
        Assert.IsNotNull(reset);

        // Restored: docx claims /archive again — but only as an OptionalExtension (action strip, not
        // right-click) and without winning the default-open decision.
        Assert.IsTrue(FileMapManager.Instance.GetExperiencesForFile(Docx, includeOptional: true).Contains(id));
        Assert.IsFalse(FileMapManager.Instance.GetExperiencesForFile(Docx, includeOptional: false).Contains(id));
        Assert.AreEqual(-1, FileMapManager.Instance.GetMatchSpecificity(Docx, id));
    }

    [TestMethod]
    public void ResetToDefault_UserCreatedId_IsNoOp()
    {
        Assert.IsNull(FileMapManager.Instance.ResetToDefault("/no-such-user-experience"));
    }
}
