using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The menu a right-click opens on a document: the clipboard, and markdown's own formatting where the caret is in
/// markdown's own words — written as markdown, as an edit like any other, and taken back as one.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-format-toolbar")]
public class DocumentMenuTests
{
    [TestMethod]
    public void InWordsTheMenuOffersPastingAndFormatting() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Some words here.\n", editor =>
        {
            var offered = Offered(editor, "words");

            CollectionAssert.Contains(offered, MarkdownSurface.Menus.Paste);
            CollectionAssert.Contains(offered, MarkdownSurface.Menus.Bold);
            CollectionAssert.Contains(offered, MarkdownSurface.Menus.Heading1);
            CollectionAssert.DoesNotContain(offered, MarkdownSurface.Menus.Cut, "nothing is chosen to cut");
        }));

    [TestMethod]
    public void WithSomethingChosenItOffersToCutAndCopyIt() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Some words here.\n", editor =>
        {
            editor.Shown.Select("Some ".Length, "words".Length);

            var offered = Offered(editor, "words");

            CollectionAssert.Contains(offered, MarkdownSurface.Menus.Cut);
            CollectionAssert.Contains(offered, LayoutVerbs.Copy);
        }));

    [TestMethod]
    public void InAFormulaFormattingMeansNothingAndIsNotOffered() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("$$\nx + y\n$$\n", editor =>
        {
            Assert.IsTrue(editor.FocusFormulaAtCaret(), "precondition: the caret is in the formula");

            CollectionAssert.DoesNotContain(Menu(editor), MarkdownSurface.Menus.Heading1);
        }));

    [TestMethod]
    public void OnlyBeingReadItOffersToCopyAndNothingThatWrites() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Some words here.\n", editor =>
        {
            editor.IsReadOnly = true;
            editor.Shown.Select(0, 4);

            var offered = Menu(editor);

            CollectionAssert.Contains(offered, LayoutVerbs.Copy);
            CollectionAssert.DoesNotContain(offered, MarkdownSurface.Menus.Paste);
            CollectionAssert.DoesNotContain(offered, MarkdownSurface.Menus.Bold);
        }));

    [TestMethod]
    public void BoldPutsTheMarksRoundWhatIsChosen() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Some words here.\n", editor =>
        {
            editor.Shown.Select("Some ".Length, "words".Length);

            Choose(editor, MarkdownSurface.Menus.Bold);

            Assert.AreEqual("Some **words** here.\n", editor.Markdown);
        }));

    [TestMethod]
    public void AHeadingIsWrittenAtTheStartOfTheBlockTheCaretIsIn() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("First.\n\nSecond block.\n", editor =>
        {
            MarkdownEditorHarness.PlaceCaret(editor, "First.\n\nSec".Length);

            Choose(editor, MarkdownSurface.Menus.Heading2);

            Assert.AreEqual("First.\n\n## Second block.\n", editor.Markdown);
        }));

    [TestMethod]
    public void FormattingIsOneThingToTakeBack() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Some words here.\n", editor =>
        {
            editor.Shown.Select("Some ".Length, "words".Length);
            Choose(editor, MarkdownSurface.Menus.Italic);

            editor.Undo();

            Assert.AreEqual("Some words here.\n", editor.Markdown);
        }));

    // ── Reading the answers ─────────────────────────────────────────────────

    /// <summary>What the menu offers where the caret is.</summary>
    private static List<string> Menu(MarkdownSurface editor) =>
        [.. ((ILayoutActions)editor).Menu(default).Select(offer => offer.Verb)];

    /// <summary>What a right-click on <paramref name="words"/> offers, as the ribbon it opens lists it.</summary>
    private static List<string> Offered(MarkdownSurface editor, string words)
    {
        var piece = editor.Shown.Laid.Root.SelfAndDescendants()
            .First(one => one.Words is { } said && said.Glyphs.Text.Contains(words, StringComparison.Ordinal));
        var at = new Point(piece.Bounds.X + (piece.Bounds.Width / 2), piece.Bounds.Y + (piece.Bounds.Height / 2));

        var ribbon = editor.Shown.BuildRibbon(at) as DiagramRibbon;
        Assert.IsNotNull(ribbon, "a right-click in words opens a menu");

        return [.. ribbon!.Offers.Select(offer => offer.Verb)];
    }

    /// <summary>Picks <paramref name="verb"/> from the menu.</summary>
    private static void Choose(MarkdownSurface editor, string verb) =>
        Assert.IsTrue(((ILayoutActions)editor).Invoke(new LayoutAct(LayoutGesture.ContextMenu, new LayoutIntent(verb),
                                                                    default, null, null, [], default)),
                      $"{verb} was done");
}
