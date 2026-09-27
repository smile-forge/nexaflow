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
        Assert.IsTrue(ContentLanguages.For("mermaid")!.Editing.Corner(Ask("mermaid", "pie\n")).Saves);
        Assert.IsFalse(ContentLanguages.For("csharp")!.Editing.Corner(Ask("csharp", "var x = 1;\n")).Saves);

        Assert.IsTrue(ContentLanguages.For("csharp")!.Editing.Corner(Ask("csharp", "var x = 1;\n")).Copies,
            "code is the one thing there most certainly is a point in copying");
    }

    [TestMethod]
    public void ALanguageThatHasSaidNothingOffersNothing()
    {
        // Nothing, rather than something guessed at — a language that has not been asked what its content
        // is made of has no business putting buttons in front of a reader.
        Assert.AreEqual(0, ContentLanguages.For("qr")!.Editing.Offers(Ask("qr", "text: hello\n")).Count);
        Assert.IsTrue(ContentLanguages.For("qr")!.Editing.Corner(Ask("qr", "text: hello\n")).Adds.Count == 0);
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
        var insert = buttons.Single(button => Id(button) == Inserting);
        var adds = buttons.Where(button => Id(button).StartsWith(Inserting + "_", System.StringComparison.Ordinal)).ToList();

        // Doing something to what is there is not browsing: a reader reaching for one of these knows what
        // they want, and hiding it a level down makes them hunt for it.
        CollectionAssert.AreEquivalent(new[] { "Copy", "Restyle this" },
                                       buttons.Except(adds).Where(button => button != insert).Select(Said).ToArray());

        Assert.AreEqual(DiagramRibbon.Inserts, Said(insert), "everything addable is behind one button");
        CollectionAssert.AreEquivalent(new[] { "A slice", "A row" }, adds.Select(Said).ToArray());
        Assert.IsTrue(adds.All(Hidden), "out of the way until it is opened");

        Press(insert);
        Assert.IsFalse(adds.Any(Hidden), "pressed, it opens a sub-ribbon of them");

        Press(insert);
        Assert.IsTrue(adds.All(Hidden), "and pressed again, closes it");
    });

    [TestMethod]
    public void AndNothingToAddMeansNoButtonToAddItWith() => UiThread.Run(() =>
    {
        var ribbon = new DiagramRibbon([new LayoutIntent(LayoutVerbs.Copy, null, "Copy")], _ => { });

        Assert.IsFalse(Descendants(ribbon).OfType<Button>().Any(button => Id(button) == Inserting));
    });

    [TestMethod]
    [CoversNode("markdown-context-menu")]
    public void NothingIsOfferedToAReaderWhoMayNotWrite() =>
        Assert.AreEqual(0, ContentLanguages.Markdown.Editing.Offers(new ContentAsk(string.Empty, "words\n") { IsReadOnly = true }).Count);

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

    /// <summary>What a button says it does, whether it is drawn as words or as a picture.</summary>
    private static string Said(Button button) => System.Windows.Automation.AutomationProperties.GetName(button);

    /// <summary>The handle a journey presses a button by.</summary>
    private static string Id(Button button) => System.Windows.Automation.AutomationProperties.GetAutomationId(button);

    /// <summary>What the ribbon's one button everything addable sits behind is found by.</summary>
    private const string Inserting = "Diagram_Ribbon_Insert";

    /// <summary>Whether a button is in a sub-ribbon still closed.</summary>
    private static bool Hidden(Button button) => button.Parent is UIElement { Visibility: not Visibility.Visible };

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

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
