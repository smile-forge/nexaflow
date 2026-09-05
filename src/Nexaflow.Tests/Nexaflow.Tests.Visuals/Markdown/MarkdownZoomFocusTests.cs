using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The one markdown-zoom behaviour that needs a real window: applying a size change while the caret is
/// in the editor. Keyboard focus needs an active window, so this cannot be asserted off-screen with the
/// rest of <see cref="MarkdownTypographyTests"/> — and the guard categorises by file, so it lives here
/// rather than pulling that whole class onto an interactive session.
/// <para>Run with <c>--filter "TestCategory=Desktop"</c>; takes focus, so never in parallel.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-zoom")]
public class MarkdownZoomFocusTests
{
    /// <summary>
    /// Clicking into a document is enough to hold keyboard focus, so an editor that waited for focus to
    /// leave before re-rendering did nothing at all in the ordinary case — the zoom label moved and the
    /// text did not. That shipped once; this is what stops it shipping again.
    /// </summary>
    [TestMethod]
    public void BaseFontSize_AppliesEvenWhileTheEditorHasFocus() => UiThread.Run(() =>
    {
        var editor = new InlineMarkdownEditor { Markdown = "Body text.\n", BaseFontSize = 15 };
        var window = new Window
        {
            Content = editor, Width = 600, Height = 400,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = -4000,
        };
        window.Show();
        try
        {
            editor.Focus();
            Settle(editor);
            Assert.IsTrue(Rtb(editor).IsKeyboardFocusWithin, "precondition: the caret is in the editor");

            editor.BaseFontSize = 30;
            Settle(editor);
            Assert.AreEqual(30d, FirstParagraphSize(editor), 1e-9,
                "zoom is a gesture the reader just made — it cannot wait for focus to leave");
        }
        finally { window.Close(); }
    });

    private static RichTextBox Rtb(InlineMarkdownEditor editor)
        => Descendants(editor).OfType<RichTextBox>().First();

    /// <summary>Font size of the first rendered paragraph — what the reader actually sees. The document's
    /// own FontSize is not enough: every block carries an explicit size, so only a re-render moves them.</summary>
    private static double FirstParagraphSize(InlineMarkdownEditor editor)
        => Rtb(editor).Document.Blocks.OfType<Paragraph>().First().FontSize;

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static void Settle(FrameworkElement fe)
    {
        fe.Measure(new Size(600, 400));
        fe.Arrange(new Rect(0, 0, 600, 400));
        fe.UpdateLayout();
    }
}
