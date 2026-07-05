using System.IO;
using System.Linq;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>Per-extension override lookup (longest-match, catch-all, form normalisation) that
/// <see cref="DefaultFileOpener"/> consults before its automatic resolution. Pure — no shell/registry.</summary>
[TestClass]
[DoNotParallelize]   // mutates the process-wide DefaultActionRegistry.Instance
public class DefaultActionRegistryTests
{
    [TestCleanup]
    public void Cleanup() => DefaultActionRegistry.Instance.Update(new DefaultActionsConfig());

    private static void SetOverrides(params DefaultActionOverride[] overrides)
        => DefaultActionRegistry.Instance.Update(new DefaultActionsConfig { Overrides = overrides.ToList() });

    private static DefaultActionOverride? Resolve(string fileName)
        => DefaultActionRegistry.Instance.Resolve(new FileInfo(fileName));

    private static DefaultActionOverride Verb(string ext, string verb) =>
        new() { Extension = ext, Kind = DefaultActionKind.WindowsVerb, TargetId = verb };

    [TestMethod]
    public void NoOverrides_ReturnsNull()
    {
        SetOverrides();
        Assert.IsNull(Resolve(@"C:\x\a.pdf"));
    }

    [TestMethod]
    public void ExactExtension_MatchesOnlyThatExtension()
    {
        SetOverrides(Verb("pdf", "open"));
        Assert.AreEqual("open", Resolve(@"C:\x\a.pdf")?.TargetId);
        Assert.IsNull(Resolve(@"C:\x\a.txt"));
    }

    [TestMethod]
    public void NormalizesStoredExtensionForms()
    {
        SetOverrides(Verb("*.PDF", "open"));   // stored as glob, upper-case
        Assert.IsNotNull(Resolve("a.pdf"));
    }

    [TestMethod]
    public void CompoundExtension_PrefersLongest()
    {
        SetOverrides(Verb("gz", "gz"), Verb("tar.gz", "targz"));
        Assert.AreEqual("targz", Resolve("archive.tar.gz")?.TargetId);
        Assert.AreEqual("gz",    Resolve("file.gz")?.TargetId);
    }

    [TestMethod]
    public void CatchAll_UsedWhenNoExactMatch()
    {
        SetOverrides(Verb("*", "any"));
        Assert.AreEqual("any", Resolve("weird.xyz")?.TargetId);
    }
}
