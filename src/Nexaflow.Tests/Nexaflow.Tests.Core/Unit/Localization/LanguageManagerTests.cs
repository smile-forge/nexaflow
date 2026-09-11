using System.IO;
using Nexaflow.Core.Localization;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// <see cref="LanguageManager"/>'s bookkeeping: which languages are installed (read from file names, loading
/// nothing), how a configured value becomes a code — including the "English" an earlier build stored — and when a
/// selection is a real change. The packs are fakes; <see cref="LanguagePackContentTests"/> reads the shipped one.
/// </summary>
[TestClass]
public class LanguageManagerTests
{
    private string _dir = "";

    [TestInitialize]
    public void CreateDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nf-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void DeleteDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private void Touch(string fileName) => File.WriteAllBytes(Path.Combine(_dir, fileName), []);

    [TestMethod]
    [CoversNode("languages-packs")]
    public void Available_ReadsFileNamesOnly_AndSkipsWhatIsNotAPack()
    {
        Touch("Nexaflow.Language.en.dll");
        Touch("Nexaflow.Language.fr.dll");
        Touch("Nexaflow.Language.not-a-culture-xyz.dll");
        Touch("Nexaflow.Language.de.txt");
        Touch("Nexaflow.Features.Text.dll");
        var loads = 0;
        var manager = new LanguageManager(_dir, (code, _) => { loads++; return new FakePack(code); });

        CollectionAssert.AreEquivalent(new[] { "en", "fr" }, manager.Available.Select(l => l.Code).ToList());
        Assert.AreEqual("Français", manager.Available.Single(l => l.Code == "fr").DisplayName,
            "a language is offered under its own name for itself");
        Assert.AreEqual(0, loads, "listing the languages must not load a pack");
    }

    [TestMethod]
    [CoversNode("languages-packs")]
    public void Available_AlwaysOffersEnglish()
    {
        var manager = new LanguageManager(_dir, (code, _) => new FakePack(code));

        Assert.AreEqual("en", manager.Available.Single().Code, "English is the fallback even with no pack file");
    }

    [TestMethod]
    [CoversNode("languages-switch")]
    public void Normalize_MapsBlankJunkAndTheLegacyEnumToEnglish()
    {
        Assert.AreEqual("en", LanguageManager.Normalize(null));
        Assert.AreEqual("en", LanguageManager.Normalize(""));
        Assert.AreEqual("en", LanguageManager.Normalize("English"), "what the old LanguageOption enum stored");
        Assert.AreEqual("en", LanguageManager.Normalize("definitely not a language"));
        Assert.AreEqual("fr", LanguageManager.Normalize("FR"));
        Assert.AreEqual("pt-BR", LanguageManager.Normalize("pt-br"));
    }

    [TestMethod]
    [CoversNode("languages-switch")]
    public void Select_ALanguageWithNoPack_FallsBackToEnglish()
    {
        Touch("Nexaflow.Language.en.dll");
        var manager = new LanguageManager(_dir, (code, _) => new FakePack(code));

        manager.Select("de");

        Assert.AreEqual("en", manager.Current);
        Assert.IsTrue(manager.IsCurrent("de"), "saving a language with no pack again is no change, so no restart");
    }

    [TestMethod]
    [CoversNode("languages-switch")]
    public void Changed_FiresOnlyOnARealChange()
    {
        Touch("Nexaflow.Language.en.dll");
        Touch("Nexaflow.Language.fr.dll");
        var manager = new LanguageManager(_dir, (code, _) => new FakePack(code));
        var raised = 0;
        manager.Changed += () => raised++;

        manager.Select("en");
        manager.Select("English");
        Assert.AreEqual(0, raised, "English is already current");

        manager.Select("fr");
        manager.Select("FR");
        Assert.AreEqual(1, raised);
        Assert.AreEqual("fr", manager.Current);
        Assert.IsFalse(manager.IsCurrent("en"));
    }

    [TestMethod]
    [CoversNode("languages-packs")]
    public void APackThatWillNotLoad_LeavesItsLanguageOnEnglish()
    {
        Touch("Nexaflow.Language.en.dll");
        Touch("Nexaflow.Language.fr.dll");
        var english = new FakePack("en", ("Nexaflow.Core/help/index.md", "# English"));
        var manager = new LanguageManager(_dir,
            (code, _) => code == "fr" ? throw new BadImageFormatException("not an assembly") : english);
        manager.Select("fr");

        using var reader = new StreamReader(manager.OpenResource("Nexaflow.Core/help/index.md")!);
        Assert.AreEqual("# English", reader.ReadToEnd());
    }
}
