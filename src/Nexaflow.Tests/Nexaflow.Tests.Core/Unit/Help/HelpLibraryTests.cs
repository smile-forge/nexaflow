using System.Windows.Media.Imaging;
using Nexaflow.Core.Help;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// <see cref="HelpLibrary"/>: the help pages the packs hold — one per page kind, titled by their first heading — the
/// index shown for a page kind with none, English filling what a translation lacks, and pictures resolved against
/// the page's own pack folder, never outside its project.
/// </summary>
[TestClass]
[CoversNode("help-library")]
public class HelpLibraryTests
{
    [TestMethod]
    public void Topics_AreTheHelpPages_TitledByTheirHeading_WithoutTheIndex()
    {
        using var help = new HelpFixture();

        CollectionAssert.AreEqual(new[] { "Markdown", "Text" }, help.Library.Topics.Select(t => t.Topic).ToList());
        Assert.AreEqual("Text viewer", help.Library.Find("text")!.Title, "topics match page kinds case-insensitively");
        Assert.AreEqual("Nexaflow.Features.Text/help", help.Library.Find("Text")!.Folder);
    }

    [TestMethod]
    public void Load_AKnownPageKind_IsItsHelpPage()
    {
        using var help = new HelpFixture();

        var doc = help.Library.Load("Text");

        Assert.AreEqual("Text", doc.Topic.Topic);
        Assert.AreEqual(HelpFixture.TextPage, doc.Markdown);
        Assert.IsNull(doc.MissingTopic);
    }

    [TestMethod]
    public void Load_APageKindWithNoHelp_ShowsTheIndex_AndSaysWhatWasMissing()
    {
        using var help = new HelpFixture();

        var doc = help.Library.Load("Hex");

        Assert.AreEqual(HelpLibrary.IndexTopic, doc.Topic.Topic);
        Assert.AreEqual("Hex", doc.MissingTopic);
        StringAssert.StartsWith(doc.Markdown, "# Nexaflow help");
        StringAssert.Contains(doc.Markdown, "- [Text viewer](help:Text)");
        StringAssert.Contains(doc.Markdown, "- [Markdown](help:Markdown)");
    }

    [TestMethod]
    public void Load_Nothing_IsTheIndex_ListingEveryPage()
    {
        using var help = new HelpFixture();

        var doc = help.Library.Load(null);

        Assert.AreEqual("Nexaflow help", doc.Topic.Title, "the index is titled by its own heading");
        Assert.IsNull(doc.MissingTopic);
        StringAssert.Contains(doc.Markdown, "Every page has help.");
        StringAssert.Contains(doc.Markdown, "(help:Text)");
    }

    [TestMethod]
    public void English_FillsWhatTheActiveLanguageLacks()
    {
        using var help = new HelpFixture(withFrench: true);
        help.Language.Select("fr");

        Assert.AreEqual("Visionneuse de texte", help.Library.Find("Text")!.Title, "the translation wins");
        Assert.AreEqual("Markdown", help.Library.Find("Markdown")!.Title, "a page it lacks comes from English");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void ResolveImage_ReadsThePictureBesideThePage_FrozenAndKept() => UiThread.Run(() =>
    {
        using var help = new HelpFixture();
        var page = help.Library.Find("Text")!;

        var first = help.Library.ResolveImage(page, "images/shot.png");

        Assert.IsInstanceOfType(first, typeof(BitmapImage));
        Assert.IsTrue(first!.IsFrozen, "any window's UI thread may draw it");
        Assert.AreSame(first, help.Library.ResolveImage(page, "./images/shot.png"), "decoded once, then kept");
        Assert.IsNull(help.Library.ResolveImage(page, "images/missing.png"));
        Assert.IsNull(help.Library.ResolveImage(page, "https://example.com/shot.png"), "never fetched");
    });

    [TestMethod]
    public void Combine_CollapsesDots_ButNeverLeavesTheProject()
    {
        Assert.AreEqual("P/help/images/a.png", HelpLibrary.Combine("P/help", "images/a.png"));
        Assert.AreEqual("P/help/a/b.png",      HelpLibrary.Combine("P/help", "./a/./b.png"));
        Assert.AreEqual("P/images/a.png",      HelpLibrary.Combine("P/help", "../images/a.png"));
        Assert.IsNull(HelpLibrary.Combine("P/help", "../../Other/secret.png"), "a page reaches only its own project");
    }

    [TestMethod]
    public void TopicFromLink_KnowsHelpLinks_AndLeavesTheRestToTheBrowser()
    {
        Assert.AreEqual("Text", HelpLibrary.TopicFromLink("help:Text"));
        Assert.AreEqual(HelpLibrary.IndexTopic, HelpLibrary.TopicFromLink("help:"));
        Assert.AreEqual("Text", HelpLibrary.TopicFromLink("Text.md"));
        Assert.AreEqual("Text", HelpLibrary.TopicFromLink("../../Nexaflow.Features.Text/help/Text.md#searching"));
        Assert.IsNull(HelpLibrary.TopicFromLink("https://example.com/Text.md"));
        Assert.IsNull(HelpLibrary.TopicFromLink("mailto:someone@example.com"));
        Assert.IsNull(HelpLibrary.TopicFromLink("notes.txt"));
    }
}
