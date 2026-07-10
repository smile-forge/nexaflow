using System.IO;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The snaplink integrity check — the same validation the Product tab's "Validate snaplinks" and the
/// installer build gate both run. The bar is <b>proof</b>: it must catch a genuinely dead target and must
/// never invent one, because a false positive fails a release build.
/// </summary>
[TestClass]
[CoversNode("product-snaplinks")]
public class SnaplinkValidatorTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"snaplink_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void WriteFile(string relative, string content)
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>A one-node tree carrying the given node-level snaplinks.</summary>
    private static ProductState TreeWith(params Snaplink[] links) => new()
    {
        Nodes = new Dictionary<string, ProductNode>
        {
            ["n"] = new() { Title = "Node", Snaplinks = [.. links] }
        }
    };

    private IntegrityReport Validate(ProductState state) => SnaplinkValidator.Validate(state, _root);

    private const string Csharp = """
        namespace Demo;
        public class Widget
        {
            public void Spin() { }
        }
        """;

    // ── sound links produce nothing ──────────────────────────────────────────

    [TestMethod]
    public void WholeFileCodeLink_ThatExists_IsClean()
    {
        WriteFile("src/Widget.cs", Csharp);
        var report = Validate(TreeWith(new Snaplink { Type = "code", Doc = "src/Widget.cs" }));
        Assert.IsTrue(report.IsClean, string.Join("; ", report.Issues.Select(i => i.Detail)));
    }

    [TestMethod]
    public void CodeLink_ToDeclaredClassAndMethod_IsClean()
    {
        WriteFile("src/Widget.cs", Csharp);
        var report = Validate(TreeWith(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Spin" }));
        Assert.IsTrue(report.IsClean, string.Join("; ", report.Issues.Select(i => i.Detail)));
    }

    [TestMethod]
    public void MarkdownLink_ToExistingHeadingPath_IsClean()
    {
        WriteFile("docs/guide.md", "# Top\n\n## Nested\n\ntext\n");
        var report = Validate(TreeWith(new Snaplink { Type = "markdown", Doc = "docs/guide.md", TitlePath = ["Top", "Nested"] }));
        Assert.IsTrue(report.IsClean, string.Join("; ", report.Issues.Select(i => i.Detail)));
    }

    [TestMethod]
    public void UrlLink_ThatIsAbsolute_IsClean()
    {
        var report = Validate(TreeWith(new Snaplink { Type = "url", Target = "https://example.com/spec" }));
        Assert.IsTrue(report.IsClean);
    }

    // ── real breakage is caught ──────────────────────────────────────────────

    [TestMethod]
    public void MissingFile_IsReported()
    {
        var report = Validate(TreeWith(new Snaplink { Type = "code", Doc = "src/Gone.cs" }));
        Assert.AreEqual(1, report.IssueCount);
        Assert.AreEqual(IntegrityKind.MissingFile, report.Issues[0].Kind);
    }

    [TestMethod]
    public void UndeclaredClass_IsReported()
    {
        WriteFile("src/Widget.cs", Csharp);
        var report = Validate(TreeWith(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Gadget" }));
        Assert.AreEqual(1, report.IssueCount);
        Assert.AreEqual(IntegrityKind.MissingClass, report.Issues[0].Kind);
    }

    [TestMethod]
    public void UndeclaredMethod_OnRealClass_IsReported()
    {
        WriteFile("src/Widget.cs", Csharp);
        var report = Validate(TreeWith(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" }));
        Assert.AreEqual(1, report.IssueCount);
        Assert.AreEqual(IntegrityKind.MissingMethod, report.Issues[0].Kind);
    }

    [TestMethod]
    public void RenamedHeading_IsReported()
    {
        WriteFile("docs/guide.md", "# Top\n\n## Renamed\n");
        var report = Validate(TreeWith(new Snaplink { Type = "markdown", Doc = "docs/guide.md", TitlePath = ["Top", "Nested"] }));
        Assert.AreEqual(1, report.IssueCount);
        Assert.AreEqual(IntegrityKind.MissingHeading, report.Issues[0].Kind);
    }

    [TestMethod]
    public void UrlLink_EmptyOrRelative_IsReported()
    {
        var report = Validate(TreeWith(
            new Snaplink { Type = "url", Target = "" },
            new Snaplink { Type = "url", Target = "not a url" }));
        CollectionAssert.AreEquivalent(
            new[] { IntegrityKind.EmptyTarget, IntegrityKind.InvalidUrl },
            report.Issues.Select(i => i.Kind).ToArray());
    }

    // ── conservatism: never invent breakage ──────────────────────────────────

    [TestMethod]
    public void ClassInFileWithNoTreeSitterGrammar_IsUnverifiable_NotBroken()
    {
        // .xaml has no bundled grammar, so its structure cannot be resolved. Reporting it would fail every
        // release build on links the product tree legitimately carries (e.g. TabularView.xaml::TabularView).
        WriteFile("src/View.xaml", "<UserControl x:Class=\"View\" />");
        var report = Validate(TreeWith(new Snaplink { Type = "code", Doc = "src/View.xaml", Class = "AnythingAtAll" }));
        Assert.IsTrue(report.IsClean, "a file with no grammar must be treated as unverifiable, not broken");
    }

    [TestMethod]
    public void HeadingInsideFencedCodeBlock_DoesNotSatisfyOrBreakAPath()
    {
        WriteFile("docs/guide.md", "# Top\n\n```\n## Fenced\n```\n");
        var report = Validate(TreeWith(new Snaplink { Type = "markdown", Doc = "docs/guide.md", TitlePath = ["Top", "Fenced"] }));
        Assert.AreEqual(IntegrityKind.MissingHeading, report.Issues.Single().Kind);
    }

    // ── coverage of both snaplink homes ──────────────────────────────────────

    [TestMethod]
    public void ConcernLevelSnaplinks_AreScanned_AndAttributedToTheConcern()
    {
        var state = new ProductState
        {
            Nodes = new Dictionary<string, ProductNode>
            {
                ["n"] = new()
                {
                    Title = "Node",
                    Concerns = [new ConcernLink { Tag = "tests", Snaplinks = [new Snaplink { Type = "code", Doc = "src/Gone.cs" }] }]
                }
            }
        };

        var issue = Validate(state).Issues.Single();
        Assert.AreEqual("tests", issue.Concern);
        Assert.AreEqual("tests", issue.Scope);
        Assert.AreEqual(IntegrityKind.MissingFile, issue.Kind);
    }

    [TestMethod]
    public void Report_CountsEverySnaplinkItScanned()
    {
        WriteFile("src/Widget.cs", Csharp);
        var report = Validate(TreeWith(
            new Snaplink { Type = "code", Doc = "src/Widget.cs" },
            new Snaplink { Type = "url", Target = "https://example.com" }));
        Assert.AreEqual(2, report.ScannedSnaplinks);
        Assert.AreEqual(1, report.ScannedNodes);
    }

    // ── single-link recheck (what the Integrity page's "Apply fix" uses) ─────

    [TestMethod]
    public void CheckLink_OnASoundLink_ReportsNoDetail()
    {
        WriteFile("src/Widget.cs", Csharp);
        var (_, detail) = SnaplinkValidator.CheckLink(
            new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Spin" }, _root);
        Assert.IsNull(detail);
    }

    [TestMethod]
    public void CheckLink_AgreesWithTheFullScan()
    {
        WriteFile("src/Widget.cs", Csharp);
        var link = new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" };

        var fromScan = Validate(TreeWith(link)).Issues.Single();
        var (kind, detail) = SnaplinkValidator.CheckLink(link, _root);

        Assert.AreEqual(fromScan.Kind, kind);
        Assert.AreEqual(fromScan.Detail, detail);
    }

    [TestMethod]
    public void CheckLink_ConfirmsARepair_SoThePageNeedNotRescan()
    {
        WriteFile("src/Widget.cs", Csharp);
        var link = new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" };
        Assert.IsNotNull(SnaplinkValidator.CheckLink(link, _root).Detail, "precondition: link is broken");

        link.Method = "Spin";   // the user re-points it in the Integrity page
        Assert.IsNull(SnaplinkValidator.CheckLink(link, _root).Detail, "repair should verify without a full scan");
    }
}
