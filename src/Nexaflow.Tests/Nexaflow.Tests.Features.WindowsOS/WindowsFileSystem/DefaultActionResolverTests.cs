using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>Candidate list the Default Actions tab shows for an extension: built-in viewers (only those
/// backed by a real action) and matching external apps. Windows shell verbs are machine-dependent and
/// not asserted here.</summary>
[TestClass]
[DoNotParallelize]   // mutates FileMapManager.Instance + ExternalAppRegistry.Instance
[CoversNode("winfs-open-entry")]
public class DefaultActionResolverTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexa-defaultact-" + Guid.NewGuid().ToString("N"));
        FileMapManager.Instance.Initialize(baseDir: _dir);
        FileMapManager.Instance.SaveMapping(new ExperienceMapping
        {
            ExperienceId = "/test/foo",
            Source       = MappingSource.User,
            Criteria     = [new FileSelectionCriteria { Type = CriteriaType.Extension, Value = "*.foo" }],
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        ExternalAppRegistry.Instance.Update(new ExternalAppsConfig());
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void IncludesInternalViewer_WhenExperienceBackedByAction()
    {
        var viewers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/test/foo" };
        var candidates = DefaultActionResolver.CandidatesFor(".foo", viewers);

        Assert.IsTrue(candidates.Any(c =>
            c.Kind == DefaultActionKind.InternalViewer && c.TargetId == "/test/foo"));
    }

    [TestMethod]
    public void ExcludesInternalViewer_WhenNoBackingAction()
    {
        // Same applicable experience, but not in the viewer set → not offered.
        var candidates = DefaultActionResolver.CandidatesFor(".foo", new HashSet<string>());
        Assert.IsFalse(candidates.Any(c => c.Kind == DefaultActionKind.InternalViewer));
    }

    [TestMethod]
    public void IncludesMatchingExternalApp_ById()
    {
        ExternalAppRegistry.Instance.Update(new ExternalAppsConfig
        {
            Apps =
            [
                new ExternalAppDefinition
                {
                    Id = "app-x", DisplayName = "Foo Editor", ApplicationPath = "x.exe",
                    Criteria = [new FileSelectionCriteria { Type = CriteriaType.Extension, Value = "*.foo" }],
                }
            ]
        });

        var candidates = DefaultActionResolver.CandidatesFor(".foo", new HashSet<string>());
        Assert.IsTrue(candidates.Any(c =>
            c.Kind == DefaultActionKind.ExternalApp && c.TargetId == "app-x" && c.Label == "Foo Editor"));
    }
}
