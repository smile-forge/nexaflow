using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The toolbar over a rendered block: the host's buttons, shown over a block drawn on the shared layout tree while the
/// pointer is on it, faint until the pointer comes near them, and handing a pressed button the block — whose picture is
/// what it draws, without what is drawn only for whoever is writing in it.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-block-picture")]
public class BlockToolbarTests
{
    private const string Document = "Pets:\n\n```mermaid\npie showData\n  \"Dogs\" : 30\n  \"Cats\" : 10\n```\n\nThe end.\n";

    /// <summary>The document in an editor offering two buttons, and what each was pressed on.</summary>
    private static void Offered(Action<InlineMarkdownEditor, RichTextBox, ContentElement, List<(string Button, RenderedBlock Block)>> test) =>
        MarkdownEditorHarness.Run(Document, (editor, rtb) =>
        {
            var pressed = new List<(string, RenderedBlock)>();
            editor.BlockActions =
            [
                new BlockAction("Copy", "Test_Copy", block => pressed.Add(("Copy", block))),
                new BlockAction("Save", "Test_Save", block => pressed.Add(("Save", block))),
            ];

            var pie = Find<ContentElement>(editor);
            Assert.IsNotNull(pie, "the pie did not render as content");

            test(editor, rtb, pie!, pressed);
        });

    private static Rect Bounds(FrameworkElement element, Visual host) =>
        element.TransformToVisual(host).TransformBounds(new Rect(element.RenderSize));

    [TestMethod]
    public void OnARenderedBlockTheHostsButtonsShowOverIt_FaintUntilThePointerComesNear() => UiThread.Run(() =>
        Offered((editor, rtb, pie, pressed) =>
        {
            var block = Bounds(pie, rtb);

            editor.HoverAt(new Point(block.Left + 20, block.Bottom - 20));
            var toolbar = editor.Toolbar()!;
            editor.UpdateLayout();

            Assert.AreEqual(Visibility.Visible, toolbar.Visibility);
            Assert.AreSame(pie, toolbar.Block);
            CollectionAssert.AreEqual(new[] { "Test_Copy", "Test_Save" },
                                      toolbar.Buttons.Select(AutomationProperties.GetAutomationId).ToArray());
            Assert.AreEqual(0.3, toolbar.ButtonOpacity, 1e-9, "faint, away from its corner");

            var buttons = toolbar.Buttons.Select(button => Bounds(button, rtb)).Aggregate(Rect.Union);
            Assert.IsTrue(block.Contains(buttons), $"in the block's own corner: {buttons} in {block}");
            Assert.IsTrue(buttons.Right > block.Right - 40 && buttons.Top < block.Top + 40, $"its top right: {buttons} in {block}");

            editor.HoverAt(new Point(block.Right - 10, block.Top + 10));
            Assert.AreEqual(1.0, toolbar.ButtonOpacity, "and all there once the pointer is near");
        }));

    [TestMethod]
    public void OffEveryBlockThereIsNoToolbar() => UiThread.Run(() =>
        Offered((editor, rtb, pie, pressed) =>
        {
            var block = Bounds(pie, rtb);
            editor.HoverAt(new Point(block.Left + 20, block.Top + 20));

            editor.HoverAt(new Point(block.Left + 5, block.Bottom + 30));
            Assert.AreEqual(Visibility.Collapsed, editor.Toolbar()!.Visibility, "over the text after the block");
            Assert.IsNull(editor.Toolbar()!.Block);
        }));

    [TestMethod]
    public void WithNothingOfferedThereIsNoToolbar() => UiThread.Run(() =>
        MarkdownEditorHarness.Run(Document, (editor, rtb) =>
        {
            var pie = Find<ContentElement>(editor)!;
            var block = Bounds(pie, rtb);

            editor.HoverAt(new Point(block.Left + 20, block.Top + 20));
            Assert.IsTrue(editor.Toolbar() is null or { Visibility: Visibility.Collapsed });
        }));

    [TestMethod]
    public void AFormulaOnALineOfItsOwnHasTheToolbar_AndOneInASentenceDoesNot() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Area $\\pi r^2$ of a circle.\n\n$$\n\\frac{a}{b}\n$$\n", (editor, rtb) =>
        {
            editor.BlockActions = [new BlockAction("Copy", "Test_Copy", _ => { })];

            var formulas = FindAll<Nexaflow.Visuals.Text.Markdown.Latex.FormulaElement>(editor).ToList();
            Assert.AreEqual(2, formulas.Count, "precondition: both formulas are drawn");

            foreach (var formula in formulas)
            {
                var bounds = Bounds(formula, rtb);
                editor.HoverAt(new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2)));

                var inline = formula.Source.Contains("pi", StringComparison.Ordinal);
                Assert.AreEqual(inline ? null : formula, editor.Toolbar()?.Block, inline ? "in a sentence, none" : "on its own line, it has one");
            }
        }));

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) yield return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            foreach (var found in FindAll<T>(VisualTreeHelper.GetChild(root, at)))
                yield return found;
    }

    [TestMethod]
    public void AButtonPressedIsHandedTheBlock_AndItsPicture() => UiThread.Run(() =>
        Offered((editor, rtb, pie, pressed) =>
        {
            var block = Bounds(pie, rtb);
            editor.HoverAt(new Point(block.Left + 20, block.Top + 20));

            var save = editor.Toolbar()!.Buttons.Single(button => AutomationProperties.GetAutomationId(button) == "Test_Save");
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            var (button, rendered) = pressed.Single();
            Assert.AreEqual("Save", button);
            Assert.AreEqual("mermaid", rendered.Language, "the language of its fence");
            StringAssert.StartsWith(rendered.Source, "pie showData");

            var picture = rendered.Picture(Brushes.White);
            var scale = VisualTreeHelper.GetDpi(pie).PixelsPerDip;
            Assert.AreEqual(Math.Ceiling(Math.Ceiling(pie.Laid.Size.Width) * scale), picture.PixelWidth, 1, "as big as the block is drawn");
            Assert.AreEqual(Math.Ceiling(Math.Ceiling(pie.Laid.Size.Height) * scale), picture.PixelHeight, 1);
        }));

    [TestMethod]
    public void APictureLeavesOutWhatIsChosen() => UiThread.Run(() =>
        Offered((editor, rtb, pie, pressed) =>
        {
            Assert.IsTrue(editor.FocusBlockAtCaret());
            var plain = Pixels(pie.Picture(Brushes.White));

            pie.Select(pie.Source.IndexOf("Dogs", StringComparison.Ordinal), 4);
            Assert.IsTrue(pie.SelectionLength > 0, "precondition: something is chosen");

            CollectionAssert.AreEqual(plain, Pixels(pie.Picture(Brushes.White)), "a selection is not in the picture");
        }));

    [TestMethod]
    public void APictureIsTheContentAsItReadsWithNobodyWritingInIt() => UiThread.Run(() =>
    {
        var writable = Shown(new ContentElement("x", StyleFormat.Dark, new Marked()));
        var reading = Shown(new ContentElement("x", StyleFormat.Dark, new Marked()) { IsReadOnly = true });

        var picture = writable.Picture(Brushes.White);

        Assert.IsTrue(Differing(picture, OnPage(reading)) < 0.005, "the picture is what the content draws for a reader");
        Assert.IsTrue(Differing(picture, OnPage(writable)) > 0.005, "and not the mark it draws only where it can be written in");
    });

    /// <summary>Content that draws a block for everybody, and a second only where somebody can write in it.</summary>
    private sealed class Marked : Nexaflow.Visuals.Text.Editing.IContent
    {
        public Nexaflow.Visuals.Text.Editing.Laid Lay(Nexaflow.Visuals.Text.Editing.EditState state, double room, bool readOnly)
        {
            var build = new Nexaflow.Visuals.Text.Editing.LayoutBuilder();
            build.Open("content");
            build.Draw(new Nexaflow.Visuals.Text.Editing.RuleMark(new Rect(0, 0, 40, 20), Brushes.Blue));
            if (!readOnly) build.Draw(new Nexaflow.Visuals.Text.Editing.RuleMark(new Rect(50, 0, 20, 20), Brushes.Red));
            build.Close();

            return new Nexaflow.Visuals.Text.Editing.Laid(build.Seal(), new Size(80, 20), []);
        }
    }

    /// <summary>An element measured and arranged, as it would be on a page.</summary>
    private static ContentElement Shown(ContentElement element)
    {
        element.Measure(new Size(200, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        return element;
    }

    /// <summary>The block as the page shows it, on white — caret, holes and all.</summary>
    private static BitmapSource OnPage(ContentElement element)
    {
        var scale = VisualTreeHelper.GetDpi(element).PixelsPerDip;
        var size = element.RenderSize;

        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                             null, new Rect(size));
        }

        var page = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
                                          96 * scale, 96 * scale, PixelFormats.Pbgra32);
        page.Render(drawing);
        return page;
    }

    /// <summary>What share of two pictures' pixels differ by more than antialiasing does — every pixel, where their sizes do.</summary>
    private static double Differing(BitmapSource one, BitmapSource other)
    {
        if (one.PixelWidth != other.PixelWidth || one.PixelHeight != other.PixelHeight) return 1;

        var (left, right) = (Pixels(one), Pixels(other));
        var differing = 0;
        for (var at = 0; at < left.Length; at += 4)
            if (Enumerable.Range(0, 3).Any(channel => Math.Abs(left[at + channel] - right[at + channel]) > 48)) differing++;

        return differing / (left.Length / 4.0);
    }

    private static byte[] Pixels(BitmapSource picture)
    {
        var pixels = new byte[picture.PixelWidth * picture.PixelHeight * 4];
        picture.CopyPixels(pixels, picture.PixelWidth * 4, 0);
        return pixels;
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}
