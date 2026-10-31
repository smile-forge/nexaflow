using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;
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

[TestCategory("Desktop")]
[DoNotParallelize]
public class ContentOffersTests
{
    [TestMethod]
    [CoversNode("markdown-copy")]
    public void WhatWouldBeCopiedIsSaidWithoutAnythingBeingCopied()
    {
        const string source = "# Title\n\nWords with **bold** in them.\n";

        var whole = MarkdownClipboard.Copied(source, null);

        Assert.AreEqual(source, whole.Markdown, "nothing chosen means the document");
        StringAssert.Contains(whole.Text, "Words with bold in them", "the words a reader sees, marks taken off");
        StringAssert.Contains(whole.Html, "<strong", "and the same marked up, for anywhere that wants it that way");
    }

    [TestMethod]
    [CoversNode("markdown-copy")]
    public void AndAChoiceIsTheCharactersThatWereChosen()
    {
        const string source = "# Title\n\nWords with **bold** in them.\n";
        var at = source.IndexOf("**bold**", System.StringComparison.Ordinal);

        var picked = MarkdownClipboard.Copied(source, (at, "**bold**".Length));

        Assert.AreEqual("**bold**", picked.Markdown, "the markdown as written, not as this renderer would write it");
        StringAssert.Contains(picked.Text, "bold");
    }

    [TestMethod]
    [CoversNode("markdown-copy")]
    public void AChoiceThatMakesNoSenseIsTheWholeDocumentRatherThanAThrow()
    {
        const string source = "words\n";

        Assert.AreEqual(source, MarkdownClipboard.Copied(source, (900, 5)).Markdown);
        Assert.AreEqual(source, MarkdownClipboard.Copied(source, (-3, 5)).Markdown);
        Assert.AreEqual(source, MarkdownClipboard.Copied(source, (0, 0)).Markdown);
    }

    [TestMethod]
    [CoversNode("markdown-copy")]
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
        Assert.AreEqual(0, ContentLanguages.For("qr")!.Editing.Ribbon(Ask("qr", "text: hello\n")).Count);
        Assert.IsTrue(ContentLanguages.For("qr")!.Editing.Corner(Ask("qr", "text: hello\n")).Adds.Count == 0);
    }

    [TestMethod]
    public void TheRibbonIsTwoRows_WhatSetsSomethingAboveWhatIsDoneAtOnce() => UiThread.Run(() =>
    {
        var ribbon = new DiagramRibbon(
        [
            new MiniRibbonButton("delete", "Delete", _ => null),
            new MiniRibbonChoice("size", "Size", [new("s", "Small"), new("m", "Medium"), new("l", "Large")], (_, _) => null) { Current = "m" },
            new MiniRibbonEntry("width", "Width", ["1", "2"], "2", (_, _) => null),
            new MiniRibbonButton("copy", "Copy", _ => null),
        ], _ => { });

        var rows = ((Panel)((System.Windows.Controls.Border)ribbon.Content).Child).Children.OfType<StackPanel>().ToList();

        Assert.AreEqual(2, rows.Count, "never more than two rows");
        CollectionAssert.IsSubsetOf(new[] { "Diagram_Ribbon_size.s", "Diagram_Ribbon_size.m", "Diagram_Ribbon_size.l", "Diagram_Ribbon_width_List" },
                                    Descendants(rows[0]).OfType<Button>().Select(Id).ToArray(), "what sets something on the first row");
        CollectionAssert.AreEqual(new[] { "Diagram_Ribbon_delete", "Diagram_Ribbon_copy" }, rows[1].Children.OfType<Button>().Select(Id).ToArray(),
                                  "what is done at once on the second, in the order offered");
    });

    [TestMethod]
    public void AChoiceOfMoreThanARowHoldsDropsDownFromOneButton_AndUsingAnyControlHandsBackItsOwnCallback() => UiThread.Run(() =>
    {
        MiniRibbonOption? chosen = null;
        var pressed = false;
        Func<ContentEdit, ContentChange?>? handed = null;

        var options = Enumerable.Range(1, 12).Select(at => new MiniRibbonOption("o" + at, "Option " + at)).ToList();
        var ribbon = new DiagramRibbon(
        [
            new MiniRibbonChoice("many", "Many", options, (_, option) => { chosen = option; return null; }),
            new MiniRibbonButton("go", "Go", _ => { pressed = true; return null; }),
        ], made => handed = made);

        var window = new Window { Content = ribbon, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Top = -10000 };
        window.Show();

        try
        {
            var buttons = Descendants(ribbon).OfType<Button>().ToList();
            var list = Descendants(ribbon).OfType<System.Windows.Controls.Primitives.Popup>().Single();

            Assert.IsFalse(list.IsOpen, "out of the way until it is opened");
            Press(buttons.Single(button => Id(button) == "Diagram_Ribbon_many"));
            Assert.IsTrue(list.IsOpen, "pressed, it drops every option down");

            Press(buttons.Single(button => Id(button) == "Diagram_Ribbon_many.o7"));
            Assert.IsFalse(list.IsOpen, "and closes on the one chosen");
            handed!(null!);
            Assert.AreEqual("o7", chosen?.Id, "the choice's own callback, run with the option chosen");

            Press(buttons.Single(button => Id(button) == "Diagram_Ribbon_go"));
            handed!(null!);
            Assert.IsTrue(pressed, "a button's own callback");
        }
        finally
        {
            window.Close();
        }
    });

    [TestMethod]
    public void AnEntryHandsBackOnlyAValueTheLanguageTakes_TypedOrPickedFromItsList() => UiThread.Run(() =>
    {
        var entered = new List<string>();
        var ribbon = new DiagramRibbon(
        [
            new MiniRibbonEntry("bits", "Bits", ["8", "16"], "4", (_, value) => { entered.Add(value); return null; })
            {
                Accepts = value => int.TryParse(value, out var bits) && bits > 0,
            },
        ], made => made(null!));

        var window = new Window { Content = ribbon, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Top = -10000 };
        window.Show();

        try
        {
            var box = Descendants(ribbon).OfType<TextBox>().Single(found => System.Windows.Automation.AutomationProperties.GetAutomationId(found) == "Diagram_Ribbon_bits");
            Assert.AreEqual("4", box.Text, "it holds the value the content has now");

            box.Text = "0";
            Enter(box);
            Assert.AreEqual(0, entered.Count, "a value the language refuses goes nowhere");

            box.Text = "12";
            Enter(box);
            CollectionAssert.AreEqual(new[] { "12" }, entered, "one it takes is handed back as typed");

            Press(Descendants(ribbon).OfType<Button>().Single(button => Id(button) == "Diagram_Ribbon_bits.16"));
            CollectionAssert.AreEqual(new[] { "12", "16" }, entered, "and one picked from the list the same");
        }
        finally
        {
            window.Close();
        }

        static void Enter(TextBox box) =>
            box.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, System.Windows.Input.Key.Enter)
            {
                RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent,
            });
    });

    [TestMethod]
    [CoversNode("markdown-context-menu")]
    public void NothingIsOfferedToAReaderWhoMayNotWrite() =>
        Assert.AreEqual(0, ContentLanguages.Markdown.Editing.Ribbon(new ContentAsk(string.Empty, "words\n") { IsReadOnly = true }).Count);

    [TestMethod]
    public void EveryOfferIsSomethingAReaderCanRead() => UiThread.Run(() =>
    {
        // A verb is what the renderer calls it; what a reader sees is words.
        Assert.AreEqual(Str.Get("Text.Ribbon.Copy"), DiagramRibbon.Names(new LayoutIntent(LayoutVerbs.Copy)));
        Assert.AreEqual(Str.Get("Text.Ribbon.SavePicture"), DiagramRibbon.Names(new LayoutIntent(LayoutVerbs.Save)));
        Assert.AreEqual(Str.Get("Text.Ribbon.OpenLink"), DiagramRibbon.Names(new LayoutIntent(LayoutVerbs.Navigate)));

        // And a content's own verb says whatever it said for itself.
        Assert.AreEqual("Sharpen", DiagramRibbon.Names(new LayoutIntent("sharpen", null, "Sharpen")));
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    private static ContentAsk Ask(string named, string source) => new(named, source);

    /// <summary>The handle a journey presses a button by.</summary>
    private static string Id(Button button) => System.Windows.Automation.AutomationProperties.GetAutomationId(button);

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
