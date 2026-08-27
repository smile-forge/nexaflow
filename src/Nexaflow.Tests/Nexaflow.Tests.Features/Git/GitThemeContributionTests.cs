using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Features.Git.Theming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// The Git logo strip's feature-owned theme tokens. Worth pinning because the failure mode is silent: a
/// mistyped pack URI or a renamed key doesn't throw — the <c>DynamicResource</c> simply resolves to nothing
/// and the logo paints transparent, which no build or unit test would otherwise notice.
/// </summary>
[TestClass]
[CoversNode("git-viewlet")]
public class GitThemeContributionTests
{
    private static readonly string[] ExpectedKeys = ["Git.LogoFill", "Git.LogoSurface", "Git.LogoBorder"];

    /// <summary>
    /// Registers the <c>pack:</c> URI scheme. In the app WPF's <c>Application</c> does this during startup;
    /// headless there is no Application, so constructing a pack URI would throw <c>UriFormatException</c>.
    /// Touching <c>PackUriHelper</c> is the documented way to register it without a UI thread.
    /// </summary>
    [ClassInitialize]
    public static void RegisterPackScheme(TestContext testContext)
        => _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

    [TestMethod]
    [TestCategory("Unit")]
    public void ContributionIsDiscoverable_AndNamesOneDictionaryInThisAssembly()
    {
        var contribution = new GitThemeContribution();

        Assert.IsInstanceOfType(contribution, typeof(IThemeContribution),
            "FeatureManager finds these by reflection over the interface");

        var uri = contribution.ResourceDictionaryUris.Single();
        StringAssert.Contains(uri.OriginalString, "Nexaflow.Features.Git;component/Theming/GitTheme.xaml",
            "the pack URI must name this assembly and the dictionary's path within it");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TheContributedDictionaryIsCompiledIntoTheAssembly_AtThePathTheUriNames()
    {
        // Actually *loading* the pack URI needs a live WPF Application (it resolves "application:" against
        // the running app), which a headless unit test has no way to provide. What can be checked here is
        // the failure that actually bites: the dictionary not being compiled in as a Page at that path —
        // a wrong build action or a renamed/moved file — which would leave every token unresolved.
        var assembly = typeof(GitThemeContribution).Assembly;
        using var resources = assembly.GetManifestResourceStream("Nexaflow.Features.Git.g.resources");
        Assert.IsNotNull(resources, "the Git assembly ships no compiled WPF resources at all");

        using var reader = new System.Resources.ResourceReader(resources!);
        var compiled = reader.Cast<System.Collections.DictionaryEntry>()
                             .Select(entry => (string)entry.Key)
                             .ToArray();

        // Pack URI "…;component/Theming/GitTheme.xaml" → resource key "theming/gittheme.baml".
        CollectionAssert.Contains(compiled, "theming/gittheme.baml",
            "GitTheme.xaml is not compiled into the assembly at the path the pack URI names");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TheDictionaryDeclaresEveryLogoToken_AsABrush()
    {
        var xaml = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(
            RepoRoot(), "src", "Nexaflow.Features", "Nexaflow.Features.Git", "Theming", "GitTheme.xaml"));

        System.Xml.Linq.XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
        var declared = xaml.Root!.Elements()
            .Select(e => (Key: (string?)e.Attribute(xamlNs + "Key"), Element: e.Name.LocalName))
            .Where(entry => entry.Key is not null)
            .ToDictionary(entry => entry.Key!, entry => entry.Element);

        foreach (var key in ExpectedKeys)
        {
            Assert.IsTrue(declared.ContainsKey(key), $"'{key}' is not declared in GitTheme.xaml");
            StringAssert.EndsWith(declared[key], "Brush", $"'{key}' should be a brush, not {declared[key]}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TheViewBindsExactlyTheKeysTheContributionShips()
    {
        // Guards the seam in both directions: a key added to the view without a token (transparent at
        // runtime), or a token renamed without updating the view.
        var xaml = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepoRoot(), "src", "Nexaflow.Features", "Nexaflow.Features.Git",
                                   "Viewlets", "GitViewletView.xaml"));

        foreach (var key in ExpectedKeys)
            StringAssert.Contains(xaml, $"{{DynamicResource {key}}}", $"the view no longer binds '{key}'");

        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(xaml, @"=""#[0-9A-Fa-f]{6,8}"""),
            "the Git viewlet must not hard-code a colour — see docs/theming.md");
    }

    /// <summary>Walks up to the directory holding the solution file.</summary>
    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Nexaflow.slnx")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repo root from the test output directory");
        return dir!.FullName;
    }
}
