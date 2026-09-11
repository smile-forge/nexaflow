using System.IO;
using Nexaflow.Core.Localization;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The string table behind <see cref="Str"/> and <c>{loc:Str}</c>: every project's <c>strings.json</c> merged,
/// the active language over English, a missing key shown as itself — and each pack read once, however often it is
/// switched to. <see cref="Str.Source"/> is process-wide, hence no parallelism here.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LocalizedStringsTests
{
    private string _dir = "";

    [TestInitialize]
    public void CreateDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nf-strings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void DeleteDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private LanguageManager Manager(IReadOnlyDictionary<string, FakePack> packs, List<string>? loads = null)
    {
        foreach (var code in packs.Keys)
            File.WriteAllBytes(Path.Combine(_dir, $"Nexaflow.Language.{code}.dll"), []);
        return new LanguageManager(_dir, (code, _) => { loads?.Add(code); return packs[code]; });
    }

    [TestMethod]
    [CoversNode("languages-strings")]
    public void Find_PrefersTheActiveLanguage_ThenEnglish_ThenNothing()
    {
        var manager = Manager(new Dictionary<string, FakePack>
        {
            ["en"] = FakePack.WithStrings("en",
                ("Nexaflow.Core", """{ "Help.A": "A", "Help.B": "B" }"""),
                ("Nexaflow.Features.Text", """{ "Text.C": "C" }""")),
            ["fr"] = FakePack.WithStrings("fr", ("Nexaflow.Core", """{ "Help.A": "A-fr" }""")),
        });
        manager.Select("fr");

        Assert.AreEqual("A-fr", manager.Find("Help.A"), "the active language wins");
        Assert.AreEqual("B", manager.Find("Help.B"), "a key the translation lacks keeps its English text");
        Assert.AreEqual("C", manager.Find("Text.C"), "every project's table is merged");
        Assert.IsNull(manager.Find("Help.Nope"));
    }

    [TestMethod]
    [CoversNode("languages-strings")]
    public void EachPack_LoadsOnce_AndSelectSwapsTheTable()
    {
        var loads = new List<string>();
        var manager = Manager(new Dictionary<string, FakePack>
        {
            ["en"] = FakePack.WithStrings("en", ("Nexaflow.Core", """{ "K": "en" }""")),
            ["fr"] = FakePack.WithStrings("fr", ("Nexaflow.Core", """{ "K": "fr" }""")),
        }, loads);

        Assert.AreEqual("en", manager.Find("K"));
        manager.Select("fr");
        Assert.AreEqual("fr", manager.Find("K"));
        manager.Select("en");
        Assert.AreEqual("en", manager.Find("K"));
        manager.Select("fr");
        Assert.AreEqual("fr", manager.Find("K"));

        CollectionAssert.AreEquivalent(new[] { "en", "fr" }, loads, "each pack is loaded once, however often it is selected");
    }

    [TestMethod]
    [CoversNode("languages-strings")]
    public void OnlyAProjectRootTableCounts_AndABrokenOneCostsOnlyItsOwnKeys()
    {
        var manager = Manager(new Dictionary<string, FakePack>
        {
            ["en"] = new FakePack("en",
                ("Nexaflow.Core/strings.json", """{ "Good": "yes", /* comments and trailing commas are fine */ }"""),
                ("Nexaflow.Core/help/strings.json", """{ "Nested": "no" }"""),
                ("Nexaflow.Features.Text/strings.json", "{ not json")),
        });

        Assert.AreEqual("yes", manager.Find("Good"));
        Assert.IsNull(manager.Find("Nested"), "only <Project>/strings.json is a string table");
    }

    [TestMethod]
    [CoversNode("languages-strings")]
    public void Str_ResolvesThroughItsSource_AndFallsBackToTheKey()
    {
        var previous = Str.Source;
        try
        {
            Str.Source = null;
            Assert.AreEqual("Help.Tab.Title", Str.Get("Help.Tab.Title"), "with no source a key shows as itself");

            Str.Source = Manager(new Dictionary<string, FakePack>
            {
                ["en"] = FakePack.WithStrings("en",
                    ("Nexaflow.Core", """{ "Help.Tab.TitleFormat": "Help: {0}", "Bad.Format": "{1}" }""")),
            });

            Assert.AreEqual("Help: Text", Str.Format("Help.Tab.TitleFormat", "Text"));
            Assert.AreEqual("Missing.Key", Str.Get("Missing.Key"));
            Assert.AreEqual("{1}", Str.Format("Bad.Format", "x"), "a translation that doesn't fit its arguments shows as written");
            Assert.AreEqual("Help: {0}", new StrExtension("Help.Tab.TitleFormat").ProvideValue(null!));
        }
        finally
        {
            Str.Source = previous;
        }
    }
}
