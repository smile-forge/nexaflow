using System;
using System.IO;
using System.Linq;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// An <see cref="CriteriaType.OptionalExtension"/> criterion advertises a secondary capability: the
/// experience is available (action strip, Default-Actions candidate) but is hidden from the right-click
/// menu and never wins the automatic default-open decision. Lets <c>/archive</c> claim <c>*.docx</c>
/// without hijacking the Windows "Open" verb.
/// </summary>
[TestClass]
[DoNotParallelize]   // mutates the process-wide FileMapManager.Instance
[CoversNode("winfs-filemap-editor")]
public class OptionalExtensionTests
{
    private const string OptExp = "/test-optional-archive";
    private const string ReqExp = "/test-required-archive";

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexa-filemap-" + Guid.NewGuid().ToString("N"));
        FileMapManager.Instance.Initialize(baseDir: dir);

        FileMapManager.Instance.SaveMapping(new ExperienceMapping
        {
            ExperienceId = OptExp,
            Source = MappingSource.User,
            Criteria = [new FileSelectionCriteria { Type = CriteriaType.OptionalExtension, Value = "*.docx" }],
        });
        FileMapManager.Instance.SaveMapping(new ExperienceMapping
        {
            ExperienceId = ReqExp,
            Source = MappingSource.User,
            Criteria = [new FileSelectionCriteria { Type = CriteriaType.Extension, Value = "*.docx" }],
        });
    }

    private static FileInfo Docx => new(@"C:\x\report.docx");

    [TestMethod]
    public void Optional_IsAvailable_WhenOptionalIncluded()
    {
        var ids = FileMapManager.Instance.GetExperiencesForFile(Docx, includeOptional: true);
        Assert.IsTrue(ids.Contains(OptExp), "action strip should offer the OptionalExtension experience");
    }

    [TestMethod]
    public void Optional_IsHidden_FromRightClick()
    {
        var ids = FileMapManager.Instance.GetExperiencesForFile(Docx, includeOptional: false);
        Assert.IsFalse(ids.Contains(OptExp), "right-click menu must not show the OptionalExtension experience");
        Assert.IsTrue(ids.Contains(ReqExp), "a regular Extension experience is still shown on right-click");
    }

    [TestMethod]
    public void Optional_DoesNotParticipateInDefaultAction()
    {
        // A required Extension match is level 4; an OptionalExtension one never scores (‑1), so it can't
        // out-rank the shell "Open" verb in DefaultFileOpener.
        Assert.AreEqual(-1, FileMapManager.Instance.GetMatchSpecificity(Docx, OptExp));
        Assert.AreEqual(4,  FileMapManager.Instance.GetMatchSpecificity(Docx, ReqExp));
    }
}
