using System.IO;
using System.Linq;
using Nexaflow.Core;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// <c>IShellServices.GetFileTextExtractor</c> composes catalog discovery, construction through the feature DI,
/// and per-workspace caching. All three are asserted against the real <see cref="ShellServices"/> method
/// because every way this breaks is silent: a PDF simply stops being searchable, or — worse — is searched
/// with settings nobody configured. Nothing throws, nothing logs.
/// <para>
/// Driven through <see cref="FeatureManager.RegisterFeatures"/> rather than a locally built catalog with a
/// no-op activation callback. That shortcut looks equivalent and isn't: activation is <em>what registers a
/// feature's global config</em>, so a test that skips it can only ever prove an extractor resolves when its
/// constructor takes nothing. Config writes are already redirected to a temp root by
/// <see cref="TestConfigIsolation"/>.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("shell-side discovery + DI plumbing for a feature contract — no single product node")]
public class FileTextExtractorResolutionTests
{
    private const string PdfExtractor = "Nexaflow.Features.Pdf.Search.PdfTextExtractor";
    private const string PdfConfigType = "Nexaflow.Features.Pdf.PdfConfig";

    [ClassInitialize]
    public static void Init(TestContext _) => FeatureManager.Instance.RegisterFeatures();

    private static ShellServices NewShell() => new(new WorkspaceRuntime());

    [TestMethod]
    public void PdfPath_ResolvesToTheFeaturesExtractor()
    {
        var extractor = NewShell().GetFileTextExtractor(@"C:\x\report.pdf");

        Assert.IsNotNull(extractor,
            "no extractor means PDFs fall through to the raw byte scan, which can never find text held in a "
            + "compressed stream — and reports the miss as inconclusive rather than as a miss");
        Assert.AreEqual(PdfExtractor, extractor.GetType().FullName);
    }

    [TestMethod]
    public void UnclaimedPath_ResolvesToNothing()
    {
        // Null is the ordinary answer — most formats have no extractor — and tells the verifier to read the
        // file as plain text.
        Assert.IsNull(NewShell().GetFileTextExtractor(@"C:\x\notes.txt"));
        Assert.IsNull(NewShell().GetFileTextExtractor(string.Empty));
    }

    [TestMethod]
    public void Extractor_IsCachedPerWorkspace_NotRebuiltPerLookup()
    {
        var shell = NewShell();

        var first  = shell.GetFileTextExtractor(@"C:\x\a.pdf");
        var second = shell.GetFileTextExtractor(@"C:\x\b.pdf");

        // Asked once per candidate file in a sweep of thousands. This shared lifetime is also exactly why the
        // contract requires implementations to be stateless or thread-safe.
        Assert.AreSame(first, second);
        Assert.AreNotSame(first, NewShell().GetFileTextExtractor(@"C:\x\a.pdf"),
            "a different workspace gets its own instance, so eviction stays per workspace");
    }

    [TestMethod]
    public async Task TheRegisteredConfigIsTheOneInjected_NotAFreshDefault()
    {
        // The guard that matters most here. An extractor that quietly ran on defaults would look completely
        // healthy while ignoring every setting the user changed, and no other test in the suite would notice.
        // Flipping the registered instance and watching behaviour change is the only proof that the object
        // the DI injected is the object the Options panel edits.
        var config = FeatureManager.Instance.Configs
            .Single(kv => kv.Key.FullName == PdfConfigType).Value;
        var includeMetadata = config.GetType().GetProperty("IncludeMetadata")!;

        var extractor = NewShell().GetFileTextExtractor(TestSampleData.Path("pdf", "text.pdf"))!;
        var path = TestSampleData.Path("pdf", "text.pdf");

        try
        {
            includeMetadata.SetValue(config, true);
            var withMetadata = await extractor.ExtractAsync(path, 4 * 1024 * 1024, default);
            StringAssert.Contains(withMetadata, PdfSamples.MetadataTitle);

            includeMetadata.SetValue(config, false);
            var withoutMetadata = await extractor.ExtractAsync(path, 4 * 1024 * 1024, default);
            Assert.IsFalse(withoutMetadata!.Contains(PdfSamples.MetadataTitle, StringComparison.Ordinal),
                "the extractor is reading a stale or defaulted config rather than the registered one");
            StringAssert.Contains(withoutMetadata, PdfSamples.BodyNeedle, "page text is unaffected either way");
        }
        finally
        {
            includeMetadata.SetValue(config, true);   // shared instance — restore for anything running after
        }
    }
}
