using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// One block of any language as a whole editing surface — the arrangement that lets a file <em>be</em> a
/// tune rather than a markdown document that contains one.
///
/// <para>
/// The editor owns the fence. What goes in is the language's own text, what comes out is the same, and the
/// <c>```abc</c> exists only for as long as it takes to render. So the claim worth testing is a round
/// trip: text in, rendered as the thing it is, edited, and the text back out with nothing added. A wrapper
/// that leaked would be invisible on screen and permanent on disk, which is the worst combination a defect
/// can have.
/// </para>
/// <para>
/// LaTeX has its own suite (<c>SingleFormulaEditorTests</c>) covering this seam from before it had a
/// second language. This is the general case, and ABC is what made it general.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class SingleBlockEditorTests
{
    private const string Tune = "X:1\nL:1/8\nK:C\nCDEF GABc|\n";

    [TestMethod]
    public void ATuneIsRenderedAsMusicWithoutTheFenceBeingInTheText() => UiThread.Run(() =>
        MarkdownEditorHarness.Run(Tune, (editor, _) =>
        {
            // Engraved, so the fence went on…
            Assert.IsNotNull(Find<AbcElement>(editor), "the block did not render as music");

            // …and came off again, so what the host holds is the tune and nothing else.
            Assert.AreEqual(Tune, editor.Markdown);
            Assert.IsFalse(editor.Markdown.Contains("```"), $"a fence leaked into the text: {editor.Markdown}");
        },
        e => e.SingleBlock = "abc"));

    [TestMethod]
    public void AndAnEditToItComesBackAsTheTuneAlone() => UiThread.Run(() =>
        MarkdownEditorHarness.Run(Tune, (editor, _) =>
        {
            var block = Find<AbcElement>(editor)!;
            Assert.IsNotNull(block);

            // Sharpen the last note through the block's own editing seam — the path the caret drives.
            var at = block.Source.LastIndexOf('c');
            Assert.IsTrue(at > 0, block.Source);

            // Through the editor, not straight at the block: the host has to have adopted it, or the edit
            // never reaches the text. That adoption is the thing this is really testing.
            Assert.IsTrue(editor.FocusBlockAtCaret(), "the editor did not give the tune the caret");

            var editable = (IEditableBlock)block;
            editable.SelectRange(at, 1);
            editable.Type('#');
            MarkdownEditorHarness.Pump();

            Assert.AreEqual("X:1\nL:1/8\nK:C\nCDEF GAB^c|\n", editor.Markdown,
                            "the edit landed somewhere other than where the note was");
        },
        e => e.SingleBlock = "abc"));

    [TestMethod]
    public void AndTheLanguageIsWhatDecidesTheFence() => UiThread.Run(() =>
    {
        // The same text, told it is two different things. With a language it engraves; without one it is a
        // markdown document, and four lines of ABC are four lines of prose.
        MarkdownEditorHarness.Run(Tune, (editor, _) =>
            Assert.IsNotNull(Find<AbcElement>(editor)), e => e.SingleBlock = "abc");

        MarkdownEditorHarness.Run(Tune, (editor, _) =>
        {
            Assert.IsNull(Find<AbcElement>(editor), "with no language it should not have engraved anything");
            Assert.AreEqual(Tune, editor.Markdown, "and the text is untouched either way");
        });
    });

    // ── Finding what it drew ────────────────────────────────────────────────

    private static T? Find<T>(DependencyObject root) where T : DependencyObject =>
        Descendants(root).OfType<T>().FirstOrDefault();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var at = stack.Pop();
            if (!seen.Add(at)) continue;
            yield return at;

            // Both trees, because an element that has never been measured has no visual children and is
            // only reachable logically — and each thing once, because the two overlap.
            if (at is Visual or System.Windows.Media.Media3D.Visual3D)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(at); i++)
                    stack.Push(VisualTreeHelper.GetChild(at, i));

            foreach (var child in LogicalTreeHelper.GetChildren(at).OfType<DependencyObject>())
                stack.Push(child);
        }
    }
}
