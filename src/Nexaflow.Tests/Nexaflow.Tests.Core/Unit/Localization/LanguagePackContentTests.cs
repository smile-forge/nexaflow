using System.IO;
using Nexaflow.Core.Localization;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The English pack as it ships: <c>Languages\Nexaflow.Language.en.dll</c> beside this test, deployed by the same
/// LanguagePacks.Deploy.targets as the app's. Pins the build — every file under a project's
/// <c>Localization/en/</c> is in the pack as <c>&lt;ProjectFolder&gt;/&lt;path&gt;</c>, forward-slashed, and nothing
/// leaked into a satellite assembly (which a culture infix in a file name, <c>x.fr.md</c>, would cause).
/// </summary>
[TestClass]
[CoversNode("lang-en")]
public class LanguagePackContentTests
{
    private static string LanguagesDir => Path.Combine(AppContext.BaseDirectory, "Languages");
    private static string EnglishPack  => Path.Combine(LanguagesDir, "Nexaflow.Language.en.dll");

    [TestMethod]
    public void EnglishPack_HoldsExactlyTheSourceFiles_UnderTheirLogicalNames()
    {
        Assert.IsTrue(File.Exists(EnglishPack), $"English pack not deployed beside the tests: {EnglishPack}");
        var pack = AssemblyLanguagePack.Load("en", EnglishPack);

        var expected = SourceFiles("en").Order(StringComparer.Ordinal).ToList();
        var actual   = pack.ResourceNames.Order(StringComparer.Ordinal).ToList();

        CollectionAssert.AreEqual(expected, actual,
            $"pack and source disagree.\n  only in source: {string.Join(", ", expected.Except(actual))}\n  only in pack: {string.Join(", ", actual.Except(expected))}");
        CollectionAssert.Contains(actual, "Nexaflow.Core/strings.json");
        foreach (var name in actual)
        {
            Assert.IsFalse(name.Contains('\\'), $"'{name}' is not forward-slashed");
            Assert.IsFalse(name.Contains("/Localization/", StringComparison.Ordinal), $"'{name}' kept its Localization/ segment");
        }
    }

    [TestMethod]
    public void NothingLeaksIntoASatelliteAssembly()
    {
        Assert.AreEqual(0, Directory.GetDirectories(LanguagesDir).Length, "a pack must never grow culture subfolders");
        Assert.IsFalse(
            Directory.EnumerateFiles(AppContext.BaseDirectory, "Nexaflow.Language.*.resources.dll", SearchOption.AllDirectories).Any(),
            "a file under Localization/ was split off into a satellite — does its name carry a culture infix?");
    }

    // What the pack should hold: every file in src/*/Localization/<code>/ and src/*/*/Localization/<code>/ (the
    // test projects excepted), named the way LanguagePack.targets names it.
    private static IEnumerable<string> SourceFiles(string code)
    {
        var src = Path.Combine(RepoRoot(), "src");
        var projects = Directory.GetDirectories(src)
            .Where(d => Path.GetFileName(d) != "Nexaflow.Tests")
            .SelectMany(d => Directory.GetDirectories(d).Prepend(d));

        foreach (var project in projects)
        {
            var root = Path.Combine(project, "Localization", code);
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                yield return $"{Path.GetFileName(project)}/{Path.GetRelativePath(root, file).Replace('\\', '/')}";
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
                return dir.FullName;

        throw new InvalidOperationException(
            $"Could not locate the repo root (no Nexaflow.slnx above '{AppContext.BaseDirectory}').");
    }
}
