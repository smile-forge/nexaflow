using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What may be done where a gesture landed, who says so, and who does it.
///
/// <para>
/// <strong>The renderer says what a thing means; the host does it.</strong> Copying is the clear case: which
/// characters were chosen and what they amount to as markdown, as words and as marked-up text is what the
/// renderer knows — and a clipboard is the application's, shared with every other thing in the window. So
/// what comes back is handed over rather than set.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-copy")]
public class ContentOffersTests
{
    [TestMethod]
    public void WhatWouldBeCopiedIsSaidWithoutAnythingBeingCopied()
    {
        const string source = "# Title\n\nWords with **bold** in them.\n";

        var whole = MarkdownClipboard.Copied(source, null);

        Assert.AreEqual(source, whole.Markdown, "nothing chosen means the document");
        StringAssert.Contains(whole.Text, "Words with bold in them", "the words a reader sees, marks taken off");
        StringAssert.Contains(whole.Html, "<strong", "and the same marked up, for anywhere that wants it that way");
    }

    [TestMethod]
    public void AndAChoiceIsTheCharactersThatWereChosen()
    {
        const string source = "# Title\n\nWords with **bold** in them.\n";
        var at = source.IndexOf("**bold**", System.StringComparison.Ordinal);

        var picked = MarkdownClipboard.Copied(source, (at, "**bold**".Length));

        Assert.AreEqual("**bold**", picked.Markdown, "the markdown as written, not as this renderer would write it");
        StringAssert.Contains(picked.Text, "bold");
    }

    [TestMethod]
    public void AChoiceThatMakesNoSenseIsTheWholeDocumentRatherThanAThrow()
    {
        const string source = "words\n";

        Assert.AreEqual(source, MarkdownClipboard.Copied(source, (900, 5)).Markdown);
        Assert.AreEqual(source, MarkdownClipboard.Copied(source, (-3, 5)).Markdown);
        Assert.AreEqual(source, MarkdownClipboard.Copied(source, (0, 0)).Markdown);
    }

    [TestMethod]
    public void ABlockSaysWhichOfTheUsualButtonsMakeSenseForIt()
    {
        // A picture of a diagram is worth keeping. A picture of a code fence is a worse copy of the code.
        Assert.IsTrue(ContentLanguages.For("mermaid")!.Corner(Ask("mermaid", "pie\n")).Saves);
        Assert.IsFalse(ContentLanguages.For("csharp")!.Corner(Ask("csharp", "var x = 1;\n")).Saves);

        Assert.IsTrue(ContentLanguages.For("csharp")!.Corner(Ask("csharp", "var x = 1;\n")).Copies,
            "code is the one thing there most certainly is a point in copying");
    }

    [TestMethod]
    public void ALanguageThatHasSaidNothingOffersNothing()
    {
        // Nothing, rather than something guessed at — a language that has not been asked what its content
        // is made of has no business putting buttons in front of a reader.
        Assert.AreEqual(0, ContentLanguages.For("qr")!.Offers(Ask("qr", "text: hello\n")).Count);
        Assert.IsTrue(ContentLanguages.For("qr")!.Corner(Ask("qr", "text: hello\n")).Adds.Count == 0);
    }

    [TestMethod]
    public void AddingSomethingIsBrowsingSoItAllSitsBehindOneButton() => UiThread.Run(() =>
    {
        var ribbon = new DiagramRibbon(
        [
            new LayoutIntent("slice", "a slice", "A slice") { Offer = LayoutOffer.Insert },
            new LayoutIntent("row", "a row", "A row") { Offer = LayoutOffer.Insert },
            new LayoutIntent(LayoutVerbs.Copy, null, "Copy"),
            new LayoutIntent("restyle", null, "Restyle this"),
        ], _ => { });

        var buttons = Descendants(ribbon).OfType<Button>().ToList();
        var menus = Descendants(ribbon).OfType<MenuItem>().ToList();

        // Doing something to what is there is not browsing: a reader reaching for one of these knows what
        // they want, and hiding it a level down makes them hunt for it.
        CollectionAssert.AreEquivalent(new[] { "Copy", "Restyle this" },
                                       buttons.Select(Said).ToArray());

        Assert.AreEqual(DiagramRibbon.Inserts, menus[0].Header, "everything addable is behind one button");
        CollectionAssert.AreEquivalent(new[] { "A slice", "A row" },
                                       menus.Skip(1).Select(item => (string)item.Header).ToArray());
    });

    [TestMethod]
    public void AndNothingToAddMeansNoButtonToAddItWith() => UiThread.Run(() =>
    {
        var ribbon = new DiagramRibbon([new LayoutIntent(LayoutVerbs.Copy, null, "Copy")], _ => { });

        Assert.AreEqual(0, Descendants(ribbon).OfType<MenuItem>().Count());
    });

    [TestMethod]
    public void EveryOfferIsSomethingAReaderCanRead() => UiThread.Run(() =>
    {
        // A verb is what the renderer calls it; what a reader sees is words.
        Assert.AreEqual("Copy", DiagramRibbon.Names(new LayoutIntent(LayoutVerbs.Copy)));
        Assert.AreEqual("Save as a picture", DiagramRibbon.Names(new LayoutIntent(LayoutVerbs.Save)));
        Assert.AreEqual("Open link", DiagramRibbon.Names(new LayoutIntent(LayoutVerbs.Navigate)));

        // And a content's own verb says whatever it said for itself.
        Assert.AreEqual("Sharpen", DiagramRibbon.Names(new LayoutIntent("sharpen", null, "Sharpen")));
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    private static ContentAsk Ask(string named, string source) => new(named, source);

    private static string Said(Button button) =>
        button.Content is TextBlock words ? words.Text : button.Content?.ToString() ?? string.Empty;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>([root]);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node)) continue;

            yield return node;

            if (node is FrameworkElement element)
                foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
                    pending.Push(child);

            if (node is ItemsControl items)
                foreach (var child in items.Items.OfType<DependencyObject>())
                    pending.Push(child);

            if (node is ContentControl { Content: DependencyObject content }) pending.Push(content);
            if (node is System.Windows.Controls.Border { Child: { } inner }) pending.Push(inner);
            if (node is Panel panel)
                foreach (DependencyObject child in panel.Children) pending.Push(child);
        }
    }
}
